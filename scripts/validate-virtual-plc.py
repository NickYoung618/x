#!/usr/bin/env python3
"""Build .NET 10, run legacy checks and the real Host against isolated PLC processes."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import socket
import subprocess
import sys
import time
import urllib.request
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SIM = ROOT / "simulator"
CONTRACT = "legacy-v6-u16-snapshot-20260917"


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def test_projects():
    solution = ET.parse(ROOT / "Gaode.slnx")
    projects = {
        Path(item.attrib["Path"]).stem
        for item in solution.findall(".//Project")
        if "/tests/" in "/" + item.attrib["Path"].replace("\\", "/")
        and item.attrib["Path"].endswith(".Tests.csproj")
    }
    require(projects, "No executable test projects found in Gaode.slnx")
    return projects


def verify_test_reports(result_dir, expected_projects):
    reports = list(result_dir.glob("*.trx"))
    require(len(reports) == len(expected_projects),
            f"Expected {len(expected_projects)} TRX reports, found {len(reports)}")
    results = {}
    for trx in reports:
        document = ET.parse(trx)
        counters = document.find(".//{*}Counters")
        require(counters is not None, f"Missing test counters: {trx.name}")
        count = int(counters.get("total", "0"))
        passed = int(counters.get("passed", "0"))
        require(count > 0 and passed == count, f"Tests failed or were skipped: {trx.name}")
        sources = {
            os.path.basename(method.attrib["codeBase"].replace("\\", "/"))
            for method in document.findall(".//{*}TestMethod")
            if method.attrib.get("codeBase")
        }
        require(len(sources) == 1, f"Expected one assembly identity in {trx.name}: {sorted(sources)}")
        assembly = next(iter(sources))
        require(assembly.endswith(".dll"), f"Unexpected test assembly in {trx.name}: {assembly}")
        project = assembly[:-4]
        require(project in expected_projects, f"Unexpected test project in {trx.name}: {project}")
        require(project not in results, f"Duplicate test project report: {project}")
        results[project] = {"passed": passed, "total": count, "report": trx.name}
    require(set(results) == expected_projects,
            f"Missing test projects: {sorted(expected_projects - set(results))}")
    return results


def candidate_identity(path):
    dll = Path(path).expanduser().resolve(strict=True)
    require(dll.is_file() and dll.name == "VirtualPlc.dll", "Candidate must be a published VirtualPlc.dll")
    runtime_path = dll.with_name("VirtualPlc.runtimeconfig.json")
    require(runtime_path.is_file(), "Candidate runtimeconfig.json is missing")
    runtime = json.loads(runtime_path.read_text(encoding="utf-8"))
    tfm = runtime.get("runtimeOptions", {}).get("tfm")
    require(tfm == "net10.0", f"Candidate must target net10.0; found {tfm!r}")
    package_hash = hashlib.sha256()
    entries = list(dll.parent.rglob("*"))
    require(not any(item.is_symlink() for item in entries), "Candidate package cannot contain symlinks")
    files = sorted(item for item in entries if item.is_file())
    require(files, "Candidate package is empty")
    for item in files:
        package_hash.update(item.relative_to(dll.parent).as_posix().encode("utf-8") + b"\0")
        package_hash.update(hashlib.sha256(item.read_bytes()).digest())
    return dll, {"dll": str(dll), "sha256": hashlib.sha256(dll.read_bytes()).hexdigest(),
                 "packageSha256": package_hash.hexdigest(), "packageFileCount": len(files),
                 "targetFramework": tfm, "sourceBuild": "NOT VERIFIED - provide candidate build evidence separately"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--candidate-dll", help="Published local VirtualPlc.dll to test instead of the repository simulator")
    parser.add_argument("--contract", help="Required with --candidate-dll; only the pinned legacy contract is supported")
    args = parser.parse_args()
    run_id = datetime.datetime.now(datetime.timezone.utc).strftime("%Y%m%dT%H%M%SZ") + "-" + uuid.uuid4().hex[:8]
    output = SIM / "artifacts" / run_id
    output.mkdir(parents=True)
    dotnet = shutil.which(args.dotnet)
    summary = {"runId": run_id, "platform": platform.platform(), "contract": CONTRACT,
               "build": "NOT RUN", "coreTests": "NOT RUN", "legacy": "NOT RUN", "integration": [],
               "v13FullContract": "NOT RUN - interface decisions deferred to device owner",
               "fullTray": "NOT RUN - framework increment only", "verdict": "FAIL"}
    processes = []
    logs = []

    def run(name, command, expected=0, timeout=180):
        with (output / (name + ".log")).open("w", encoding="utf-8") as log:
            log.write(json.dumps(command, ensure_ascii=False) + "\n")
            log.flush()
            result = subprocess.run(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT, timeout=timeout)
        require(result.returncode == expected, f"{name}: exit {result.returncode}, expected {expected}; see {name}.log")

    def http(url, method="GET"):
        with urllib.request.urlopen(urllib.request.Request(url, method=method), timeout=2) as response:
            return json.load(response)

    def start_sim(name, plc_dll, motion_ms=350):
        # Reserve both choices together, so HTTP and Modbus can never pick the same port.
        with socket.socket() as one, socket.socket() as two:
            one.bind(("127.0.0.1", 0)); two.bind(("127.0.0.1", 0))
            web_port, plc_port = one.getsockname()[1], two.getsockname()[1]
        log = (output / f"{name}-simulator.log").open("w", encoding="utf-8")
        logs.append(log)
        env = os.environ.copy()
        env.update({"Dashboard__OpenBrowserOnStart": "false", "Modbus__ListenAddress": "127.0.0.1",
                    "Modbus__Port": str(plc_port), "Modbus__UnitId": "1", "Simulation__MotionDurationMs": str(motion_ms),
                    "Simulation__ScanPeriodMs": "10"})
        proc = subprocess.Popen([dotnet, str(plc_dll), "--urls", f"http://127.0.0.1:{web_port}"],
                                cwd=plc_dll.parent, env=env, stdout=log, stderr=subprocess.STDOUT)
        processes.append(proc)
        base = f"http://127.0.0.1:{web_port}"
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            require(proc.poll() is None, f"{name}: simulator exited before ready")
            try:
                if http(base + "/health").get("service") == "VirtualPlc":
                    with socket.create_connection(("127.0.0.1", plc_port), timeout=.5):
                        return proc, base, plc_port
            except (OSError, ValueError):
                pass
            time.sleep(.1)
        raise RuntimeError(f"{name}: simulator readiness timed out")

    def stop(proc):
        if proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill(); proc.wait(timeout=5)

    def probe(name, base, port, outcome, completed, timeout_ms=3000):
        before = http(base + "/api/simulator/state")
        report = output / (name + ".json")
        run(name, [dotnet, str(output / "host" / "Inspection.Host.dll"), "--plc-probe",
                   "--Plc:Contract", CONTRACT, "--Plc:Host", "127.0.0.1", "--Plc:Port", str(port),
                   "--Plc:Report", str(report), "--Plc:ActionTimeoutMs", str(timeout_ms)],
            expected=0 if outcome == "Completed" else 2, timeout=40)
        data = json.loads(report.read_text())
        after = http(base + "/api/simulator/state")
        (output / (name + "-device.json")).write_text(json.dumps({"before": before, "after": after}, indent=2), encoding="utf-8")
        require(data["outcome"] == outcome, f"{name}: unexpected outcome {data['outcome']}")
        require(len(data["moves"]) == completed, f"{name}: wrong completed count")
        require(all(m["observedBusy"] for m in data["moves"]), f"{name}: missing fresh busy")
        pdus = [bytes.fromhex(e["request"])[7:] for e in data["exchanges"] if e["request"]]
        submitted = sum(p == bytes.fromhex("0600000001") for p in pdus)
        if outcome == "RecoveryRequired":
            require(all(p[0] in (1, 3) for p in pdus), f"{name}: recovery wrote to PLC")
        else:
            require(submitted == (2 if outcome == "Completed" else 1), f"{name}: unexpected retries/submissions")
        require(not any(p[:3] == bytes.fromhex("06001C") for p in pdus), f"{name}: used Retry_Cmd")
        if outcome == "Completed":
            registers = {r["name"]: r["rawValue"] for r in after["holdingRegisters"]}
            require(registers["Machine_Current_Pos_X"] == 321 and registers["Machine_Current_Pos_Y"] == 654,
                    "Independent device coordinates mismatch")
            require(registers["Pallet_Lock_Status"] == 1, "Tray was not locked")
        summary["integration"].append({"name": name, "status": "PASS", "outcome": outcome,
                                       "completedMoves": completed, "submittedMoves": submitted})

    try:
        require(bool(args.candidate_dll) == bool(args.contract),
                "--candidate-dll and explicit --contract must be supplied together")
        require(args.contract in (None, CONTRACT),
                "Only the pinned legacy contract is runnable; V1.3 contract is NOT RUN")
        candidate_dll = None
        if args.candidate_dll:
            candidate_dll, summary["candidate"] = candidate_identity(args.candidate_dll)
            summary["simulatorUnderTest"] = "external candidate DLL"
            summary["buildScope"] = "repository solution only; candidate source build NOT VERIFIED"
        else:
            summary["simulatorUnderTest"] = "repository snapshot"
            summary["buildScope"] = "repository solution including simulator under test"
        require(dotnet is not None, "dotnet not found; install exact SDK from global.json or pass --dotnet")
        version = subprocess.check_output([dotnet, "--version"], cwd=ROOT, text=True).strip()
        summary["sdk"] = version
        summary["commit"] = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        summary["workingTreeDirty"] = bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip())
        require(version == json.loads((ROOT / "global.json").read_text())["sdk"]["version"], "SDK version mismatch")
        run("restore", [dotnet, "restore", "Gaode.slnx", "--locked-mode"])
        run("build", [dotnet, "build", "Gaode.slnx", "-c", "Release", "--no-restore"])
        summary["build"] = "PASS"
        run("core-tests", [dotnet, "test", "Gaode.slnx", "-c", "Release", "--no-build", "--logger", "trx",
                           "--results-directory", str(output / "test-results")])
        assemblies = verify_test_reports(output / "test-results", test_projects())
        summary["coreTests"] = {"status": "PASS", "passed": sum(item["passed"] for item in assemblies.values()),
                                "assemblies": len(assemblies), "byAssembly": assemblies}
        run("publish-plc", [dotnet, "publish", "simulator/VirtualPlc/src/VirtualPlc", "-c", "Release", "--no-build", "-o", str(output / "plc")])
        run("publish-host", [dotnet, "publish", "backend/src/Inspection.Host", "-c", "Release", "--no-build", "-o", str(output / "host")])
        run("publish-driver", [dotnet, "publish", "simulator/VirtualPlc/tests/VirtualPlc.SystemValidation/VirtualPlc.SystemValidation.csproj", "-c", "Release", "--no-build", "-o", str(output / "driver")])
        plc_dll = candidate_dll or output / "plc" / "VirtualPlc.dll"
        proc, base, port = start_sim("legacy", plc_dll)
        production_gate = "NOT RUN" if candidate_dll else "PASS"
        gate_reason = ("External candidate source build is not verified by this entrypoint."
                       if candidate_dll else "This run built and published the net10.0 production project successfully.")
        run("legacy-checks", [dotnet, str(output / "driver" / "VirtualPlc.SystemValidation.dll"),
            "--http", base, "--modbus-port", str(port), "--repo-root", str(SIM),
            "--json-output", str(output / "legacy.json"), "--markdown-output", str(output / "legacy.md"),
            "--sdk-version", version, "--execution-target", "net10.0", "--mode", "candidate-legacy" if candidate_dll else "production",
            "--production-gate-status", production_gate, "--production-gate-reason", gate_reason], timeout=90)
        legacy = json.loads((output / "legacy.json").read_text())
        require(len(legacy["checks"]) == 13 and all(c["status"] == "PASS" for c in legacy["checks"]), "Legacy checks missing or failed")
        require(legacy["verdict"] == "PASS", "Legacy verdict failed")
        require(len(legacy["gates"]) == 1 and legacy["gates"][0]["status"] == production_gate,
                "Legacy report mislabeled the production build gate")
        summary["legacy"] = "PASS 13/13 (old protocol and old policy only)"
        stop(proc)

        proc, base, port = start_sim("normal", plc_dll)
        probe("normal", base, port, "Completed", 2)
        probe("reconnect-stale", base, port, "RecoveryRequired", 0)
        stop(proc)
        require((output / "normal-simulator.log").read_text().count("Virtual PLC action started: Move") == 2,
                "Device independently recorded an unexpected number of normal moves")

        proc, base, port = start_sim("failure", plc_dll)
        require(http(base + "/api/simulator/faults/MoveTimeout", "POST")["success"], "Fault injection rejected")
        probe("device-failure", base, port, "Failed", 0)
        stop(proc)
        require((output / "failure-simulator.log").read_text().count("Virtual PLC action started: Move") == 1,
                "Failure scenario executed an extra move")

        proc, base, port = start_sim("timeout", plc_dll, motion_ms=5000)
        probe("action-timeout", base, port, "Unknown", 0, timeout_ms=700)
        probe("reconnect-unknown", base, port, "RecoveryRequired", 0)
        stop(proc)
        require((output / "timeout-simulator.log").read_text().count("Virtual PLC action started: Move") == 1,
                "Timeout/reconnect executed an extra move")
        summary["verdict"] = "PASS - framework/legacy integration only"
    except Exception as exc:
        summary["error"] = f"{type(exc).__name__}: {exc}"
        print(summary["error"], file=sys.stderr)
    finally:
        for proc in processes:
            stop(proc)
        for log in logs:
            log.close()
        summary["processesStopped"] = all(p.poll() is not None for p in processes)
        (output / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(f"Evidence: {output}")
        print(summary["verdict"])
    return 0 if summary["verdict"].startswith("PASS") else 1


if __name__ == "__main__":
    sys.exit(main())
