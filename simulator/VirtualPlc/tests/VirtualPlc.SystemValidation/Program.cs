using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

var options = ValidationOptions.Parse(args);
var suite = new ValidationSuite(options);
var report = await suite.RunAsync();

Directory.CreateDirectory(Path.GetDirectoryName(options.JsonOutput)!);
await File.WriteAllTextAsync(
    options.JsonOutput,
    JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));
await File.WriteAllTextAsync(options.MarkdownOutput, MarkdownReport.Render(report));

var passed = report.Checks.Count(x => x.Status == "PASS");
var failed = report.Checks.Count - passed;
Console.WriteLine($"VALIDATION {report.Verdict}: {passed} passed, {failed} failed");
Console.WriteLine($"JSON: {options.JsonOutput}");
Console.WriteLine($"Markdown: {options.MarkdownOutput}");
return report.Verdict == "PASS" ? 0 : 1;

internal sealed record ValidationOptions(
    Uri HttpBase,
    string ModbusHost,
    int ModbusPort,
    string RepoRoot,
    string JsonOutput,
    string MarkdownOutput,
    string SdkVersion,
    string ExecutionTarget,
    string Mode,
    string ProductionGateStatus,
    string ProductionGateReason)
{
    public static ValidationOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Arguments must be --name value pairs.");
            }

            values[args[index][2..]] = args[index + 1];
        }

        string Required(string name) => values.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"Missing --{name}.");

        return new ValidationOptions(
            new Uri(Required("http")),
            values.GetValueOrDefault("modbus-host", "127.0.0.1"),
            int.Parse(Required("modbus-port"), CultureInfo.InvariantCulture),
            Path.GetFullPath(Required("repo-root")),
            Path.GetFullPath(Required("json-output")),
            Path.GetFullPath(Required("markdown-output")),
            Required("sdk-version"),
            Required("execution-target"),
            Required("mode"),
            Required("production-gate-status"),
            Required("production-gate-reason"));
    }
}

internal sealed class ValidationSuite
{
    private static readonly string[] Limitations =
    {
        "V6.0 点表没有故障码寄存器，只能通过 PLC_System_Fault 和管理 API 表达详细故障。",
        "点表没有分拣目标区域和区域占用量，无法自动闭环 NG/Pending 满盘判断。",
        "点表没有实际抓取槽位反馈，无法验证命令槽位与实际槽位冲突。",
        "X/Y/Z 坐标的单位、比例、符号和溢出规则尚未定义。",
        "Retry_Cmd 的参数保留、确认与断线幂等规则尚未定义。",
        "虚拟进程不能替代真实编码器断电位置保持验收。"
    };

    private readonly ValidationOptions _options;
    private readonly HttpClient _http;
    private readonly RawModbusClient _modbus;
    private readonly List<ValidationCheck> _checks = new();

    public ValidationSuite(ValidationOptions options)
    {
        _options = options;
        _http = new HttpClient { BaseAddress = options.HttpBase, Timeout = TimeSpan.FromSeconds(8) };
        _modbus = new RawModbusClient(options.ModbusHost, options.ModbusPort, 1);
    }

    public async Task<ValidationReport> RunAsync()
    {
        var startedAt = DateTimeOffset.UtcNow;
        await _modbus.ConnectAsync();

        await CheckAsync("HTTP-01", new[] { "FR-004" }, "HTTP 健康、监控和非法参数", CheckHttpSurfaceAsync);
        await CheckAsync("MAP-01", new[] { "FR-001" }, "29 点 CSV/HTTP/state 一致性", CheckAddressMapAsync);
        await CheckAsync("MODBUS-01", new[] { "FR-002" }, "六个 Modbus 功能码成功路径", CheckModbusSuccessAsync);
        await CheckAsync("MODBUS-02", new[] { "FR-003" }, "Modbus 异常码、方向和多写原子性", CheckModbusErrorsAsync);
        await CheckAsync("FLOW-01", new[] { "FR-005" }, "心跳、区域配置和托盘锁握手", CheckHandshakeAsync);
        await CheckAsync("ACTION-01", new[] { "FR-006" }, "移动、重触发和 Retry_Cmd", CheckMoveAndRetryAsync);
        await CheckAsync("ACTION-02", new[] { "FR-006" }, "翻转和分拣正常流程", CheckFlipAndSortAsync);
        await CheckAsync("LOCK-01", new[] { "FR-007" }, "就绪、配置、软停和动作互斥", CheckInterlocksAsync);
        await CheckAsync("FAULT-01", new[] { "FR-008" }, "六种一次性动作故障", CheckActionFaultsAsync);
        await CheckAsync("FAULT-02", new[] { "FR-008" }, "急停、人工介入和心跳超时", CheckSafetyFaultsAsync);
        await CheckAsync("POLICY-01", new[] { "FR-009" }, "十一类流程决策", CheckFlowPolicyAsync);
        await CheckAsync("RESET-01", new[] { "FR-010" }, "复位清除故障、PC 值和动作状态", CheckResetAsync);
        await CheckAsync(
            "EVIDENCE-01",
            new[] { "FR-011", "FR-012", "FR-013", "FR-014" },
            "单命令、目录边界、报告判定和生产目标门禁",
            CheckEvidenceContractAsync);

        await _modbus.DisposeAsync();
        _http.Dispose();

        var finishedAt = DateTimeOffset.UtcNow;
        return new ValidationReport(
            startedAt,
            finishedAt,
            new ValidationEnvironment(
                Environment.OSVersion.ToString(),
                _options.SdkVersion,
                "net10.0",
                _options.ExecutionTarget,
                _options.Mode),
            _checks,
            new[]
            {
                new EnvironmentGate(
                    "Production .NET 10 build",
                    _options.ProductionGateStatus,
                    _options.ProductionGateReason)
            },
            Limitations,
            _checks.All(x => x.Status == "PASS") ? "PASS" : "FAIL");
    }

    private async Task CheckAsync(
        string id,
        string[] requirements,
        string name,
        Func<Task<string>> body)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var detail = await body();
            stopwatch.Stop();
            _checks.Add(new ValidationCheck(id, requirements, name, "PASS", stopwatch.ElapsedMilliseconds, detail));
            Console.WriteLine($"PASS {id} {name}");
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _checks.Add(new ValidationCheck(
                id,
                requirements,
                name,
                "FAIL",
                stopwatch.ElapsedMilliseconds,
                $"{exception.GetType().Name}: {exception.Message}"));
            Console.WriteLine($"FAIL {id} {name}: {exception.Message}");
        }
    }

    private async Task<string> CheckHttpSurfaceAsync()
    {
        using var health = JsonDocument.Parse(await _http.GetStringAsync("/health"));
        Equal("ok", health.RootElement.GetProperty("status").GetString(), "health.status");
        Equal("VirtualPlc", health.RootElement.GetProperty("service").GetString(), "health.service");

        var dashboard = await _http.GetStringAsync("/");
        True(dashboard.Contains("Virtual PLC 实时监控", StringComparison.Ordinal), "dashboard title missing");
        True((await _http.GetStringAsync("/app.js")).Contains("fetch", StringComparison.Ordinal), "app.js invalid");
        True((await _http.GetStringAsync("/styles.css")).Length > 1000, "styles.css invalid");

        using var badFault = await _http.PostAsync("/api/simulator/faults/NotARealFault", null);
        Equal(400, (int)badFault.StatusCode, "invalid fault HTTP status");
        using var badCategory = await _http.GetAsync(
            "/api/simulator/flow-decision?category=NotARealCategory&stepSucceeded=false");
        Equal(400, (int)badCategory.StatusCode, "invalid category HTTP status");
        return "health、dashboard、app.js、styles.css 可用；非法 fault/category 返回 400";
    }

    private async Task<string> CheckAddressMapAsync()
    {
        var csvPath = Path.Combine(_options.RepoRoot, "VirtualPlc", "docs", "plc-address-map.csv");
        var csv = File.ReadAllLines(csvPath)
            .Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .Select(columns => new ExpectedPoint(
                columns[0], columns[1], int.Parse(columns[2], CultureInfo.InvariantCulture), columns[3], columns[4]))
            .ToArray();
        Equal(29, csv.Length, "CSV point count");

        using var map = JsonDocument.Parse(await _http.GetStringAsync("/api/simulator/address-map"));
        using var state = JsonDocument.Parse(await _http.GetStringAsync("/api/simulator/state"));
        var mapPoints = map.RootElement.GetProperty("coils").EnumerateArray()
            .Select(x => ParseHttpPoint(x, "Coil", "documentAddress", false))
            .Concat(map.RootElement.GetProperty("holdingRegisters").EnumerateArray()
                .Select(x => ParseHttpPoint(x, "HoldingRegister", "documentAddress", false)))
            .ToDictionary(x => x.Address, StringComparer.Ordinal);
        var statePoints = state.RootElement.GetProperty("coils").EnumerateArray()
            .Select(x => ParseHttpPoint(x, "Coil", "address", true))
            .Concat(state.RootElement.GetProperty("holdingRegisters").EnumerateArray()
                .Select(x => ParseHttpPoint(x, "HoldingRegister", "address", true)))
            .ToDictionary(x => x.Address, StringComparer.Ordinal);
        Equal(29, mapPoints.Count, "HTTP map point count");
        Equal(29, statePoints.Count, "state point count");

        foreach (var expected in csv)
        {
            True(mapPoints.TryGetValue(expected.Address, out var mapped), $"map missing {expected.Address}");
            True(statePoints.TryGetValue(expected.Address, out var actual), $"state missing {expected.Address}");
            Equal(expected.Area, mapped!.Area, $"{expected.Address} map area");
            Equal(expected.Offset, mapped.Offset, $"{expected.Address} map offset");
            Equal(expected.Name, mapped.Name, $"{expected.Address} map name");
            Equal(expected.Direction, mapped.Direction, $"{expected.Address} map direction");
            Equal(mapped, actual, $"{expected.Address} map/state metadata");
        }

        await _modbus.WriteRegisterAsync(3, ushort.MaxValue);
        using var signedState = JsonDocument.Parse(await _http.GetStringAsync("/api/simulator/state"));
        var boundary = signedState.RootElement.GetProperty("holdingRegisters").EnumerateArray()
            .Single(x => x.GetProperty("address").GetString() == "4x0003");
        Equal(65535, boundary.GetProperty("rawValue").GetInt32(), "raw ushort boundary");
        Equal(-1, boundary.GetProperty("signedValue").GetInt32(), "signed ushort boundary");
        return "8 个线圈 + 21 个保持寄存器三方一致；65535 的 signedValue 为 -1";
    }

    private async Task<string> CheckModbusSuccessAsync()
    {
        await ResetAsync();
        var coils = await _modbus.ReadCoilsAsync(1, 8);
        True(coils[4], "FC01 did not read PLC_Mode_Auto=1");
        var registers = await _modbus.ReadRegistersAsync(1, 5);
        Equal(5, registers.Length, "FC03 register count");

        await _modbus.WriteCoilAsync(3, true);
        True((await _modbus.ReadCoilsAsync(3, 1))[0], "FC05 write/read mismatch");
        await _modbus.WriteRegisterAsync(3, ushort.MaxValue);
        Equal(ushort.MaxValue, (await _modbus.ReadRegistersAsync(3, 1))[0], "FC06 boundary mismatch");

        await _modbus.WriteCoilsAsync(2, new[] { true, true });
        var multiCoils = await _modbus.ReadCoilsAsync(2, 2);
        True(multiCoils[0] && multiCoils[1], "FC15 write/read mismatch");
        await _modbus.WriteRegistersAsync(3, new ushort[] { 11, 22, 33 });
        True(
            new ushort[] { 11, 22, 33 }.SequenceEqual(await _modbus.ReadRegistersAsync(3, 3)),
            "FC16 write/read mismatch");
        await ResetAsync();
        return "FC01、03、05、06、15、16 均完成真实 TCP 往返和回读";
    }

    private async Task<string> CheckModbusErrorsAsync()
    {
        await ResetAsync();
        await _modbus.ExpectExceptionAsync(0x04, AddressAndValue(0, 1), 0x01);
        await _modbus.ExpectExceptionAsync(0x03, AddressAndValue(127, 2), 0x02);
        await _modbus.ExpectExceptionAsync(0x03, AddressAndValue(0, 0), 0x03);
        await _modbus.ExpectExceptionAsync(0x03, AddressAndValue(0, 1), 0x0B, unitId: 2);
        await _modbus.ExpectExceptionAsync(0x05, AddressAndValue(0, 0xFF00), 0x02);
        await _modbus.ExpectExceptionAsync(0x06, AddressAndValue(1, 99), 0x02);
        await _modbus.ExpectExceptionAsync(0x05, AddressAndValue(2, 1), 0x03);

        await _modbus.WriteRegisterAsync(1, 0);
        var mixedOwnership = MultiRegisterPayload(0, new ushort[] { 5, 9 });
        await _modbus.ExpectExceptionAsync(0x10, mixedOwnership, 0x02);
        Equal((ushort)0, (await _modbus.ReadRegistersAsync(1, 1))[0], "multi-write was not atomic");

        var malformed = new byte[] { 0, 2, 0, 2, 2, 0, 7 };
        await _modbus.ExpectExceptionAsync(0x10, malformed, 0x03);
        return "异常码 01/02/03/0B、PLC 所有权和多写原子拒绝均符合合同";
    }

    private async Task<string> CheckHandshakeAsync()
    {
        await ResetAsync();
        var heartbeat = (await _modbus.ReadCoilsAsync(1, 1))[0];
        await Task.Delay(1200);
        True((await _modbus.ReadCoilsAsync(1, 1))[0] != heartbeat, "PLC heartbeat did not toggle");

        await WithHeartbeatAsync(async () =>
        {
            await ReadyAndConfigureAsync();
            await _modbus.WriteRegisterAsync(23, 1);
            await WaitRegisterAsync(24, 1, "pallet locked");
            await _modbus.WriteRegisterAsync(23, 0);
            await WaitRegisterAsync(24, 0, "pallet unlocked");
            Equal((ushort)4, (await _modbus.ReadRegistersAsync(25, 1))[0], "NG zone count");
            Equal((ushort)3, (await _modbus.ReadRegistersAsync(26, 1))[0], "Pending zone count");
        });
        return "PLC 心跳翻转；PC 应答、区域 Ack、托盘锁紧/解锁闭环通过";
    }

    private async Task<string> CheckMoveAndRetryAsync()
    {
        await WithHeartbeatAsync(async () =>
        {
            await ReadyAndConfigureAsync();
            for (ushort command = 1; command <= 5; command++)
            {
                var values = new ushort[] { (ushort)(100 + command), (ushort)(200 + command), (ushort)(300 + command) };
                await _modbus.WriteRegistersAsync(3, values);
                await PulseRegisterAsync(1, command);
                await WaitRegisterAsync(6, 1, $"move {command} running");
                await WaitRegisterAsync(2, 1, $"move {command} XY arrived");
                await WaitRegisterAsync(6, 2, $"move {command} Z arrived");
                Equal(values[0], (await _modbus.ReadRegistersAsync(7, 1))[0], $"move {command} X feedback");
                Equal(values[1], (await _modbus.ReadRegistersAsync(8, 1))[0], $"move {command} Y feedback");
            }

            await _modbus.WriteRegistersAsync(3, new ushort[] { 999, 888, 777 });
            await _modbus.WriteRegisterAsync(1, 5);
            await Task.Delay(500);
            Equal((ushort)105, (await _modbus.ReadRegistersAsync(7, 1))[0], "same command retriggered without zero");
            await PulseRegisterAsync(1, 5);
            await WaitRegisterAsync(6, 1, "re-armed move running");
            await WaitRegisterAsync(2, 1, "re-armed move");
            Equal((ushort)999, (await _modbus.ReadRegistersAsync(7, 1))[0], "re-armed X feedback");

            await PulseRegisterAsync(29, 1);
            await WaitRegisterAsync(2, 0, "retry reset XY");
            await WaitRegisterAsync(6, 0, "retry reset Z");
        });
        return "移动命令 1~5、0→非0 重触发、坐标反馈和 Retry_Cmd 状态复位通过";
    }

    private async Task<string> CheckFlipAndSortAsync()
    {
        await WithHeartbeatAsync(async () =>
        {
            await ReadyAndConfigureAsync();
            await PulseRegisterAsync(10, 1);
            await WaitRegisterAsync(11, 1, "flip 90 running");
            await WaitRegisterAsync(11, 2, "flip 90 complete");
            Equal((ushort)90, (await _modbus.ReadRegistersAsync(12, 1))[0], "flip 90 angle");

            await PulseRegisterAsync(10, 2);
            await WaitRegisterAsync(11, 1, "flip 180 running");
            await WaitRegisterAsync(11, 2, "flip 180 complete");
            Equal((ushort)180, (await _modbus.ReadRegistersAsync(12, 1))[0], "flip 180 angle");

            await _modbus.WriteRegisterAsync(20, 5);
            await PulseRegisterAsync(21, 1);
            await WaitRegisterAsync(22, 1, "sort running");
            await WaitRegisterAsync(22, 2, "sort complete");
            await PulseRegisterAsync(21, 2);
            await WaitRegisterAsync(22, 4, "full pallet command");
        });
        return "90°/180° 翻转、分拣成功和满盘命令状态通过";
    }

    private async Task<string> CheckInterlocksAsync()
    {
        await ResetAsync();
        await _modbus.WriteRegistersAsync(3, new ushort[] { 10, 20, 30 });
        await PulseRegisterAsync(1, 2);
        await WaitRegisterAsync(2, 2, "move rejected without ready");

        await WithHeartbeatAsync(async () =>
        {
            await ResetAsync();
            await _modbus.WriteCoilAsync(3, true);
            await PulseRegisterAsync(1, 2);
            await WaitRegisterAsync(2, 2, "move rejected without zone config");

            await ReadyAndConfigureAsync();
            await PulseRegisterAsync(1, 6);
            await WaitRegisterAsync(2, 2, "invalid move command");
            await PulseRegisterAsync(10, 3);
            await WaitRegisterAsync(11, 3, "invalid flip command");
            await PulseRegisterAsync(21, 3);
            await WaitRegisterAsync(22, 3, "invalid sort command");

            await _modbus.WriteRegistersAsync(3, new ushort[] { 50, 60, 70 });
            await PulseRegisterAsync(1, 2);
            await WaitRegisterAsync(6, 1, "move before soft stop");
            await _modbus.WriteCoilAsync(6, true);
            await WaitRegisterAsync(2, 2, "XY failed after soft stop");
            await WaitRegisterAsync(6, 3, "Z failed after soft stop");

            await ReadyAndConfigureAsync();
            await _modbus.WriteRegistersAsync(3, new ushort[] { 80, 90, 100 });
            await PulseRegisterAsync(1, 2);
            await WaitRegisterAsync(6, 1, "move before conflict");
            await PulseRegisterAsync(10, 1);
            await WaitRegisterAsync(11, 3, "conflicting flip rejected");
            await WaitRegisterAsync(2, 1, "original move completed after conflict");

            await _modbus.WriteRegisterAsync(20, 0);
            await PulseRegisterAsync(21, 1);
            await WaitRegisterAsync(22, 3, "zero slot sort rejected");
        });
        return "未就绪、未配置、非法命令、软停、动作互斥和零槽位均被拒绝";
    }

    private async Task<string> CheckActionFaultsAsync()
    {
        await WithHeartbeatAsync(async () =>
        {
            await ReadyAndConfigureAsync();
            await InjectFaultAsync("MoveTimeout");
            await _modbus.WriteRegistersAsync(3, new ushort[] { 1, 2, 3 });
            await PulseRegisterAsync(1, 2);
            await WaitRegisterAsync(2, 2, "MoveTimeout XY");
            await WaitRegisterAsync(6, 3, "MoveTimeout Z");
            await PulseRegisterAsync(1, 2);
            await WaitRegisterAsync(2, 1, "MoveTimeout one-shot recovery");

            await ReadyAndConfigureAsync();
            await InjectFaultAsync("FlipFailure");
            await PulseRegisterAsync(10, 1);
            await WaitRegisterAsync(11, 3, "FlipFailure");
            await PulseRegisterAsync(10, 1);
            await WaitRegisterAsync(11, 1, "FlipFailure recovery running");
            await WaitRegisterAsync(11, 2, "FlipFailure one-shot recovery");

            await ReadyAndConfigureAsync();
            await InjectFaultAsync("FlipAngleMismatch");
            await PulseRegisterAsync(10, 1);
            await WaitRegisterAsync(11, 2, "FlipAngleMismatch complete");
            Equal((ushort)180, (await _modbus.ReadRegistersAsync(12, 1))[0], "mismatched angle");
            await PulseRegisterAsync(10, 1);
            await WaitRegisterAsync(11, 1, "FlipAngleMismatch recovery running");
            await WaitRegisterAsync(11, 2, "FlipAngleMismatch recovery");
            Equal((ushort)90, (await _modbus.ReadRegistersAsync(12, 1))[0], "recovered angle");

            await ReadyAndConfigureAsync();
            await _modbus.WriteRegisterAsync(20, 7);
            await InjectFaultAsync("SortingFailure");
            await PulseRegisterAsync(21, 1);
            await WaitRegisterAsync(22, 3, "SortingFailure");
            await PulseRegisterAsync(21, 1);
            await WaitRegisterAsync(22, 2, "SortingFailure one-shot recovery");

            await ReadyAndConfigureAsync();
            await _modbus.WriteRegisterAsync(20, 7);
            await InjectFaultAsync("FullPallet");
            await PulseRegisterAsync(21, 1);
            await WaitRegisterAsync(22, 4, "FullPallet");
            await PulseRegisterAsync(21, 1);
            await WaitRegisterAsync(22, 2, "FullPallet one-shot recovery");

            await ReadyAndConfigureAsync();
            await InjectFaultAsync("PalletLockFailure");
            await _modbus.WriteRegisterAsync(23, 1);
            await WaitRegisterAsync(24, 2, "PalletLockFailure");
            await _modbus.WriteRegisterAsync(23, 0);
            await WaitRegisterAsync(24, 0, "pallet unlock after failure");
            await _modbus.WriteRegisterAsync(23, 1);
            await WaitRegisterAsync(24, 1, "PalletLockFailure one-shot recovery");
        });
        return "6 种动作故障均产生预期终态，且下一次动作恢复正常";
    }

    private async Task<string> CheckSafetyFaultsAsync()
    {
        await WithHeartbeatAsync(async () =>
        {
            await ReadyAndConfigureAsync();
            await _modbus.WriteRegistersAsync(3, new ushort[] { 10, 20, 30 });
            await PulseRegisterAsync(1, 2);
            await WaitRegisterAsync(6, 1, "move before emergency");
            await InjectFaultAsync("EmergencyAlarm");
            await WaitCoilAsync(4, true, "PLC fault after emergency");
            await WaitRegisterAsync(2, 2, "move failed after emergency");

            await ReadyAndConfigureAsync();
            await PulseRegisterAsync(1, 2);
            await WaitRegisterAsync(6, 1, "move before manual intervention");
            await InjectFaultAsync("ManualZoneOccupied");
            await WaitCoilAsync(10, true, "manual zone occupied");
            await WaitRegisterAsync(2, 2, "move failed after manual intervention");
            await _modbus.WriteCoilAsync(11, false);
            await _modbus.WriteCoilAsync(11, true);
            await WaitCoilAsync(10, false, "manual confirmation recovery");
        });

        await ResetAsync();
        await using (var echo = new HeartbeatEcho(_options.ModbusHost, _options.ModbusPort))
        {
            await echo.StartAsync();
            await _modbus.WriteCoilAsync(3, true);
            await InjectFaultAsync("PauseHeartbeat");
            var before = (await _modbus.ReadCoilsAsync(1, 1))[0];
            await Task.Delay(1200);
            Equal(before, (await _modbus.ReadCoilsAsync(1, 1))[0], "heartbeat changed while paused");
        }

        await WaitCoilAsync(4, true, "heartbeat response timeout fault", 5000);
        using var state = JsonDocument.Parse(await _http.GetStringAsync("/api/simulator/state"));
        True(state.RootElement.GetProperty("communicationTimedOut").GetBoolean(), "communicationTimedOut=false");
        True(state.RootElement.GetProperty("activeFaults").EnumerateArray()
            .Any(x => x.GetString() == "PauseHeartbeat"), "PauseHeartbeat missing from activeFaults");
        await ResetAsync();
        return "急停与人工介入切断动作；人工确认恢复；暂停心跳且停止应答后 3 秒超时";
    }

    private async Task<string> CheckFlowPolicyAsync()
    {
        var categories = new[]
        {
            "DeviceAction", "DeviceTimeout", "PlcSafety", "PlcCommunication",
            "Camera", "Barcode", "Algorithm", "Recipe", "Storage", "Mes", "Other"
        };
        var fallbackCategories = new HashSet<string>(
            new[] { "Camera", "Barcode", "Algorithm", "Recipe", "Storage", "Mes", "Other" },
            StringComparer.Ordinal);

        foreach (var category in categories)
        {
            using var success = JsonDocument.Parse(await _http.GetStringAsync(
                $"/api/simulator/flow-decision?category={category}&stepSucceeded=true"));
            AssertDecision(success.RootElement, true, false, false, decisionRequired: false, $"{category} success");

            using var failure = JsonDocument.Parse(await _http.GetStringAsync(
                $"/api/simulator/flow-decision?category={category}&stepSucceeded=false"));
            if (fallbackCategories.Contains(category))
            {
                AssertDecision(failure.RootElement, true, false, true, decisionRequired: true, $"{category} failure");
            }
            else if (category == "DeviceTimeout")
            {
                AssertDecision(failure.RootElement, true, true, true, decisionRequired: false, $"{category} failure");
            }
            else
            {
                AssertDecision(failure.RootElement, false, true, false, decisionRequired: false, $"{category} failure");
            }
        }

        return "11 类成功策略和失败策略全部通过；随机 decision 限定为 OK/NG/Pending";
    }

    private async Task<string> CheckResetAsync()
    {
        await ResetAsync();
        await _modbus.WriteCoilAsync(3, true);
        await _modbus.WriteRegisterAsync(3, 1234);
        await _modbus.WriteRegisterAsync(20, 8);
        await InjectFaultAsync("EmergencyAlarm");
        await ResetAsync();

        True(!(await _modbus.ReadCoilsAsync(3, 1))[0], "PC_System_Ready not reset");
        True(!(await _modbus.ReadCoilsAsync(4, 1))[0], "PLC_System_Fault not reset");
        True((await _modbus.ReadCoilsAsync(5, 1))[0], "PLC_Mode_Auto not restored");
        Equal((ushort)0, (await _modbus.ReadRegistersAsync(3, 1))[0], "Camera_Target_X not reset");
        Equal((ushort)0, (await _modbus.ReadRegistersAsync(20, 1))[0], "Sorting_Part_Index not reset");
        foreach (var address in new[] { 2, 6, 11, 22, 24, 28 })
        {
            Equal((ushort)0, (await _modbus.ReadRegistersAsync(address, 1))[0], $"status 4x{address:0000} not reset");
        }

        using var state = JsonDocument.Parse(await _http.GetStringAsync("/api/simulator/state"));
        Equal(0, state.RootElement.GetProperty("activeFaults").GetArrayLength(), "active faults not cleared");
        True(state.RootElement.GetProperty("activeAction").ValueKind == JsonValueKind.Null, "active action not cleared");
        True(!state.RootElement.GetProperty("communicationTimedOut").GetBoolean(), "timeout flag not cleared");
        return "reset 清除故障、活动动作、PC 可写量和状态，并恢复自动模式";
    }

    private Task<string> CheckEvidenceContractAsync()
    {
        var repositoryPrefix = Path.TrimEndingDirectorySeparator(_options.RepoRoot) + Path.DirectorySeparatorChar;
        True(_options.JsonOutput.StartsWith(repositoryPrefix, StringComparison.Ordinal), "JSON output is outside pj1");
        True(_options.MarkdownOutput.StartsWith(repositoryPrefix, StringComparison.Ordinal), "Markdown output is outside pj1");

        var script = Path.Combine(_options.RepoRoot, "VirtualPlc", "scripts", "validate.sh");
        var productionProject = Path.Combine(
            _options.RepoRoot, "VirtualPlc", "src", "VirtualPlc", "VirtualPlc.csproj");
        True(File.Exists(script), "single-command validation script is missing");
        var projectText = File.ReadAllText(productionProject);
        True(projectText.Contains("<TargetFramework>net10.0</TargetFramework>", StringComparison.Ordinal),
            "production target is no longer net10.0");
        True(_options.ProductionGateStatus is "PASS" or "FAIL" or "NOT RUN", "invalid gate status");
        True(Limitations.Length > 0, "known specification limitations are missing");
        return Task.FromResult(
            "validate.sh 为单入口；JSON/Markdown 位于 pj1；报告区分门禁与限制；生产目标仍为 net10.0");
    }

    private async Task WithHeartbeatAsync(Func<Task> action)
    {
        await using var echo = new HeartbeatEcho(_options.ModbusHost, _options.ModbusPort);
        await echo.StartAsync();
        await action();
    }

    private async Task ReadyAndConfigureAsync()
    {
        await ResetAsync();
        await _modbus.WriteCoilAsync(3, true);
        await _modbus.WriteRegisterAsync(25, 4);
        await _modbus.WriteRegisterAsync(26, 3);
        await PulseRegisterAsync(27, 1);
        await WaitRegisterAsync(28, 1, "Zone_Config_Ack");
    }

    private async Task ResetAsync()
    {
        using var response = await _http.PostAsync("/api/simulator/reset", null);
        response.EnsureSuccessStatusCode();
        await Task.Delay(30);
    }

    private async Task InjectFaultAsync(string fault)
    {
        using var response = await _http.PostAsync($"/api/simulator/faults/{fault}", null);
        response.EnsureSuccessStatusCode();
    }

    private async Task PulseRegisterAsync(int documentAddress, ushort value)
    {
        await _modbus.WriteRegisterAsync(documentAddress, 0);
        await Task.Delay(30);
        await _modbus.WriteRegisterAsync(documentAddress, value);
    }

    private async Task WaitRegisterAsync(int address, ushort expected, string name, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var actual = (await _modbus.ReadRegistersAsync(address, 1))[0];
            if (actual == expected)
            {
                return;
            }

            await Task.Delay(20);
        }

        var final = (await _modbus.ReadRegistersAsync(address, 1))[0];
        throw new InvalidOperationException($"{name}: expected {expected}, actual {final} at 4x{address:0000}.");
    }

    private async Task WaitCoilAsync(int address, bool expected, string name, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            var actual = (await _modbus.ReadCoilsAsync(address, 1))[0];
            if (actual == expected)
            {
                return;
            }

            await Task.Delay(20);
        }

        var final = (await _modbus.ReadCoilsAsync(address, 1))[0];
        throw new InvalidOperationException($"{name}: expected {expected}, actual {final} at 0x{address:0000}.");
    }

    private static void AssertDecision(
        JsonElement element,
        bool expectedContinue,
        bool expectedError,
        bool expectedFallback,
        bool decisionRequired,
        string context)
    {
        Equal(expectedContinue, element.GetProperty("continueFlow").GetBoolean(), $"{context} continueFlow");
        Equal(expectedError, element.GetProperty("shouldReportError").GetBoolean(), $"{context} shouldReportError");
        Equal(expectedFallback, element.GetProperty("isFallback").GetBoolean(), $"{context} isFallback");
        var decision = element.GetProperty("decision");
        if (decisionRequired)
        {
            var value = decision.GetString();
            True(value is "OK" or "NG" or "Pending", $"{context} invalid decision {value}");
        }
        else
        {
            True(decision.ValueKind == JsonValueKind.Null, $"{context} decision should be null");
        }
    }

    private static HttpPoint ParseHttpPoint(JsonElement element, string area, string addressProperty, bool stateDirection)
    {
        var directionElement = element.GetProperty("direction");
        string direction;
        if (directionElement.ValueKind == JsonValueKind.Number)
        {
            direction = directionElement.GetInt32() == 0 ? "PC->PLC" : "PLC->PC";
        }
        else
        {
            var raw = directionElement.GetString();
            direction = raw == "PcToPlc" ? "PC->PLC" : raw == "PlcToPc" ? "PLC->PC" : raw ?? "";
        }

        var address = element.GetProperty(addressProperty).GetString()!;
        var offset = stateDirection
            ? element.GetProperty("pduOffset").GetInt32()
            : element.GetProperty("documentNumber").GetInt32() - 1;
        return new HttpPoint(area, address, offset, element.GetProperty("name").GetString()!, direction);
    }

    private static byte[] AddressAndValue(int address, int value)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), checked((ushort)address));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), checked((ushort)value));
        return payload;
    }

    private static byte[] MultiRegisterPayload(int start, IReadOnlyList<ushort> values)
    {
        var payload = new byte[5 + values.Count * 2];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), checked((ushort)start));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), checked((ushort)values.Count));
        payload[4] = checked((byte)(values.Count * 2));
        for (var index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(5 + index * 2, 2), values[index]);
        }

        return payload;
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}.");
        }
    }

    private static void Equal<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, string name)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"{name}: expected [{string.Join(',', expected)}], actual [{string.Join(',', actual)}].");
        }
    }

    private sealed record ExpectedPoint(string Area, string Address, int Offset, string Name, string Direction);
    private sealed record HttpPoint(string Area, string Address, int Offset, string Name, string Direction);
}

internal sealed class HeartbeatEcho : IAsyncDisposable
{
    private readonly RawModbusClient _client;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _loop;

    public HeartbeatEcho(string host, int port) => _client = new RawModbusClient(host, port, 1);

    public async Task StartAsync()
    {
        await _client.ConnectAsync();
        _loop = RunAsync(_cancellation.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _client.DisposeAsync();
        _cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var value = (await _client.ReadCoilsAsync(1, 1, cancellationToken))[0];
            await _client.WriteCoilAsync(2, value, cancellationToken);
            await Task.Delay(80, cancellationToken);
        }
    }
}

internal sealed class RawModbusClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly byte _unitId;
    private readonly TcpClient _client = new();
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private NetworkStream? _stream;
    private ushort _transaction;

    public RawModbusClient(string host, int port, byte unitId)
    {
        _host = host;
        _port = port;
        _unitId = unitId;
    }

    public async Task ConnectAsync()
    {
        await _client.ConnectAsync(_host, _port);
        _client.NoDelay = true;
        _stream = _client.GetStream();
    }

    public async Task<bool[]> ReadCoilsAsync(
        int documentAddress,
        int count,
        CancellationToken cancellationToken = default)
    {
        var response = await SuccessAsync(0x01, AddressAndCount(documentAddress - 1, count), cancellationToken);
        Equal((count + 7) / 8, response[1], "FC01 byte count");
        var values = new bool[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = (response[2 + index / 8] & (1 << (index % 8))) != 0;
        }

        return values;
    }

    public async Task<ushort[]> ReadRegistersAsync(
        int documentAddress,
        int count,
        CancellationToken cancellationToken = default)
    {
        var response = await SuccessAsync(0x03, AddressAndCount(documentAddress - 1, count), cancellationToken);
        Equal(count * 2, response[1], "FC03 byte count");
        var values = new ushort[count];
        for (var index = 0; index < count; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(2 + index * 2, 2));
        }

        return values;
    }

    public async Task WriteCoilAsync(
        int documentAddress,
        bool value,
        CancellationToken cancellationToken = default)
    {
        var payload = AddressAndCount(documentAddress - 1, value ? 0xFF00 : 0x0000);
        var response = await SuccessAsync(0x05, payload, cancellationToken);
        True(response.AsSpan(1).SequenceEqual(payload), "FC05 echo mismatch");
    }

    public async Task WriteRegisterAsync(
        int documentAddress,
        ushort value,
        CancellationToken cancellationToken = default)
    {
        var payload = AddressAndCount(documentAddress - 1, value);
        var response = await SuccessAsync(0x06, payload, cancellationToken);
        True(response.AsSpan(1).SequenceEqual(payload), "FC06 echo mismatch");
    }

    public async Task WriteCoilsAsync(int documentAddress, IReadOnlyList<bool> values)
    {
        var byteCount = (values.Count + 7) / 8;
        var payload = new byte[5 + byteCount];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), checked((ushort)(documentAddress - 1)));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), checked((ushort)values.Count));
        payload[4] = checked((byte)byteCount);
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index])
            {
                payload[5 + index / 8] |= (byte)(1 << (index % 8));
            }
        }

        var response = await SuccessAsync(0x0F, payload, default);
        Equal((ushort)(documentAddress - 1), BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(1, 2)), "FC15 address");
        Equal((ushort)values.Count, BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(3, 2)), "FC15 count");
    }

    public async Task WriteRegistersAsync(int documentAddress, IReadOnlyList<ushort> values)
    {
        var payload = new byte[5 + values.Count * 2];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), checked((ushort)(documentAddress - 1)));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), checked((ushort)values.Count));
        payload[4] = checked((byte)(values.Count * 2));
        for (var index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(5 + index * 2, 2), values[index]);
        }

        var response = await SuccessAsync(0x10, payload, default);
        Equal((ushort)(documentAddress - 1), BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(1, 2)), "FC16 address");
        Equal((ushort)values.Count, BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(3, 2)), "FC16 count");
    }

    public async Task ExpectExceptionAsync(
        byte function,
        byte[] payload,
        byte expectedCode,
        byte? unitId = null)
    {
        var response = await RequestAsync(function, payload, unitId ?? _unitId, default);
        Equal((byte)(function | 0x80), response[0], $"function {function:X2} exception function");
        Equal(expectedCode, response[1], $"function {function:X2} exception code");
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        _client.Dispose();
        _requestLock.Dispose();
    }

    private async Task<byte[]> SuccessAsync(byte function, byte[] payload, CancellationToken cancellationToken)
    {
        var response = await RequestAsync(function, payload, _unitId, cancellationToken);
        if ((response[0] & 0x80) != 0)
        {
            throw new InvalidOperationException($"Modbus exception {response[1]} for function {function:X2}.");
        }

        Equal(function, response[0], "response function");
        return response;
    }

    private async Task<byte[]> RequestAsync(
        byte function,
        byte[] payload,
        byte unitId,
        CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Modbus client is not connected.");
        }

        await _requestLock.WaitAsync(cancellationToken);
        try
        {
            var transaction = unchecked(++_transaction);
            var request = new byte[8 + payload.Length];
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(0, 2), transaction);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4, 2), checked((ushort)(payload.Length + 2)));
            request[6] = unitId;
            request[7] = function;
            payload.CopyTo(request, 8);
            await _stream.WriteAsync(request, cancellationToken);

            var header = new byte[7];
            await ReadExactlyAsync(_stream, header, cancellationToken);
            Equal(transaction, BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(0, 2)), "transaction id");
            Equal((ushort)0, BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2)), "protocol id");
            Equal(unitId, header[6], "unit id echo");
            var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4, 2));
            True(length >= 2, "invalid response length");
            var response = new byte[length - 1];
            await ReadExactlyAsync(_stream, response, cancellationToken);
            return response;
        }
        finally
        {
            _requestLock.Release();
        }
    }

    private static byte[] AddressAndCount(int address, int value)
    {
        var payload = new byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(0, 2), checked((ushort)address));
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2, 2), checked((ushort)value));
        return payload;
    }

    private static async Task ReadExactlyAsync(NetworkStream stream, Memory<byte> buffer, CancellationToken token)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[total..], token);
            if (count == 0)
            {
                throw new EndOfStreamException();
            }

            total += count;
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}.");
        }
    }
}

internal static class MarkdownReport
{
    public static string Render(ValidationReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# VirtualPlc 全量测试验证报告");
        builder.AppendLine();
        builder.AppendLine($"- **结论**: {report.Verdict}");
        builder.AppendLine($"- **开始**: {report.StartedAt:O}");
        builder.AppendLine($"- **结束**: {report.FinishedAt:O}");
        builder.AppendLine($"- **耗时**: {(report.FinishedAt - report.StartedAt).TotalSeconds:F2} 秒");
        builder.AppendLine($"- **SDK**: {Escape(report.Environment.SdkVersion)}");
        builder.AppendLine($"- **运行模式**: {Escape(report.Environment.Mode)} ({Escape(report.Environment.ExecutionTarget)})");
        builder.AppendLine($"- **生产目标**: {Escape(report.Environment.ProductionTarget)}");
        builder.AppendLine();
        builder.AppendLine("## 自动检查");
        builder.AppendLine();
        builder.AppendLine("| ID | 需求 | 状态 | 耗时(ms) | 检查 | 证据/错误 |");
        builder.AppendLine("|---|---|---:|---:|---|---|");
        foreach (var check in report.Checks)
        {
            builder.AppendLine(
                $"| {check.Id} | {string.Join(", ", check.Requirements)} | {check.Status} | " +
                $"{check.DurationMs} | {Escape(check.Name)} | {Escape(check.Detail)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## 环境门禁");
        builder.AppendLine();
        builder.AppendLine("| 门禁 | 状态 | 原因 |");
        builder.AppendLine("|---|---:|---|");
        foreach (var gate in report.Gates)
        {
            builder.AppendLine($"| {Escape(gate.Name)} | {gate.Status} | {Escape(gate.Reason)} |");
        }

        builder.AppendLine();
        builder.AppendLine("## 已知规范限制");
        builder.AppendLine();
        foreach (var limitation in report.Limitations)
        {
            builder.AppendLine($"- {limitation}");
        }

        builder.AppendLine();
        builder.AppendLine("## 判定规则");
        builder.AppendLine();
        builder.AppendLine("自动检查任一 FAIL 时总评为 FAIL。环境门禁 NOT RUN 单独披露，不会被兼容主机冒充为 PASS。");
        return builder.ToString();
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);
}

internal sealed record ValidationReport(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    ValidationEnvironment Environment,
    IReadOnlyList<ValidationCheck> Checks,
    IReadOnlyList<EnvironmentGate> Gates,
    IReadOnlyList<string> Limitations,
    string Verdict);

internal sealed record ValidationEnvironment(
    string OperatingSystem,
    string SdkVersion,
    string ProductionTarget,
    string ExecutionTarget,
    string Mode);

internal sealed record ValidationCheck(
    string Id,
    IReadOnlyList<string> Requirements,
    string Name,
    string Status,
    long DurationMs,
    string Detail);

internal sealed record EnvironmentGate(string Name, string Status, string Reason);
