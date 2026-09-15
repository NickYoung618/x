param([Parameter(Mandatory = $true)][string]$HostDirectory)
$ErrorActionPreference = 'Stop'
$directory = (Resolve-Path $HostDirectory).Path
$assembly = Join-Path $directory 'Inspection.Host.dll'
if (-not (Test-Path $assembly)) { throw "Host artifact missing: $assembly" }
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$baseUrl = "http://127.0.0.1:$port"
$info = [System.Diagnostics.ProcessStartInfo]::new()
$info.FileName = (Get-Command dotnet -ErrorAction Stop).Source
$info.WorkingDirectory = $directory
$info.UseShellExecute = $false
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true
$info.ArgumentList.Add($assembly)
$info.ArgumentList.Add('--urls')
$info.ArgumentList.Add($baseUrl)
$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $info
$started = $false
try {
    $started = $process.Start()
    if (-not $started) { throw 'Host process did not start.' }
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    $live = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($process.HasExited) { throw "Host exited with code $($process.ExitCode)." }
        try { $live = Invoke-RestMethod "$baseUrl/health/live" -TimeoutSec 2 } catch { $live = $null }
        if ($live.status -eq 'alive') { break }
        Start-Sleep -Milliseconds 200
    }
    if ($live.status -ne 'alive') { throw 'Host did not become live within 30 seconds.' }
    $status = Invoke-RestMethod "$baseUrl/api/system/status" -TimeoutSec 5
    $plan = Invoke-RestMethod "$baseUrl/api/engineering/demo-plan" -TimeoutSec 5
    if ($status.productionReady -ne $false -or $status.architectureVersion -ne '1.3') { throw 'Unexpected foundation status.' }
    if ($plan.isTestFixture -ne $true -or $plan.executionEnabled -ne $false) { throw 'Plan must remain a nonexecuting fixture.' }
    $count = ($plan.plan.faces | ForEach-Object { $_.captures.Count } | Measure-Object -Sum).Sum
    if ($count -ne 8) { throw "Default published sample expected 8 captures, got $count." }
    Write-Output "PASS: published Host process, architecture 1.3, productionReady=false, 8 planned captures."
} finally {
    if ($started -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit(5000) | Out-Null }
    if ($null -ne $stdout) { $stdout.GetAwaiter().GetResult() | Set-Content (Join-Path $directory 'smoke-stdout.log') }
    if ($null -ne $stderr) { $stderr.GetAwaiter().GetResult() | Set-Content (Join-Path $directory 'smoke-stderr.log') }
    $process.Dispose()
}
