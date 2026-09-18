#!/usr/bin/env python3
"""Plan required test evidence from a change; check a report without inventing PASS.

This is a scope and evidence gate, not a test runner. CI and independent device
traces remain the source of actual test results.
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
from pathlib import Path


SUITES = {
    "core_ci": "Linux/Windows locked restore, Release build, .NET tests, UI tests and published Host smoke",
    "contract_review": "Versioned specification, protocol and expected-result review",
    "workflow": "State, identity, sequencing, timeout and adjacent-stage regression",
    "host": "Real Host endpoint, rejection, duplicate request and published-process checks",
    "plc_wire": "Independent Modbus client/server raw-frame and point-map checks",
    "plc_legacy": "Separate old V6 regression, never counted as V1.3 acceptance",
    "plc_v13": "V1.3 independent simulator action/fault/reconnect checks",
    "acquisition": "Independent camera/3D process, frame identity and bad-frame checks",
    "algorithm": "Worker input/output, stale result, failure and deadline checks",
    "persistence": "Database/media intent, failure, restart and cross-record checks",
    "ui": "Vue contract/browser checks and affected Windows desktop paths",
    "recovery": "Interrupt at the changed boundary; reconcile without duplicate physical action",
    "test_gate": "Test runner negative controls: failure, zero tests and missing evidence reject",
    "manual_classification": "Unmapped changed path requires an explicit impact decision",
    "station_matrix": "All applicable normal, refusal, timeout, late, duplicate and recovery rows for this station",
    "station_boundary": "Previous and next stage handoff with independent expected identity and action counts",
    "full_tray": "All published virtual recipes plus applicable fault families and cross-layer reconciliation",
    "windows_desktop": "Same candidate package on interactive Windows desktop with independent devices",
}


def classify(path: str) -> set[str]:
    """Fail closed for unfamiliar paths; a caller may add reviewed suites."""
    p = path.replace("\\", "/")
    if p.startswith("backend/src/Inspection.Infrastructure/Plc/"):
        return {"plc_wire", "plc_v13", "recovery"}
    if p.startswith("simulator/VirtualPlc/"):
        return {"plc_wire", "plc_legacy", "plc_v13", "recovery"}
    if p.startswith("backend/src/Inspection.Application/"):
        return {"workflow", "recovery"}
    if p.startswith("backend/src/Inspection.Domain/"):
        return {"workflow"}
    if p.startswith("backend/src/Inspection.Host/"):
        return {"host", "workflow"}
    if p.startswith("backend/src/Inspection.Infrastructure/"):
        return {"persistence", "recovery", "manual_classification"}
    if p.startswith("frontend/") or p.startswith("desktop/"):
        return {"ui", "host"}
    if p.startswith("worker/") or p.startswith("algorithm/"):
        return {"algorithm", "recovery"}
    if p.startswith("backend/tests/") or p.startswith("scripts/") or p.startswith(".github/workflows/"):
        return {"test_gate"}
    if p == ".github/pull_request_template.md":
        return {"contract_review", "test_gate"}
    if p.startswith("docs/") or p.startswith("specs/") or p in {"README.md", "Gaode.slnx", "global.json"}:
        return {"contract_review"}
    return {"manual_classification"}


def make_plan(paths: list[str], station: str | None, head: str, extra: list[str] | None = None) -> dict:
    if not paths:
        raise ValueError("No changed files: check base/head or provide --changed-file")
    unknown = [p for p in paths if "manual_classification" in classify(p)]
    suites = {"core_ci"}
    for path in paths:
        suites.update(classify(path))
    suites.update(extra or [])
    if station and any(s in suites for s in ("workflow", "host", "plc_v13", "acquisition", "algorithm", "persistence", "ui")):
        suites.add("station_boundary")
    station_required = ["station_matrix", "station_boundary"] if station else []
    if station == "S11":
        station_required += ["full_tray", "windows_desktop"]
    return {
        "schema": "gaode-test-impact-v1",
        "head": head,
        "station": station,
        "changedFiles": sorted(set(paths)),
        "unclassifiedFiles": sorted(unknown),
        "prRequired": sorted(suites),
        "stationAcceptanceRequired": station_required,
        "suiteDefinitions": {name: SUITES[name] for name in sorted(suites | set(station_required))},
        "note": "This is a required-scope plan, not a PASS result. Run and record each test separately.",
    }


def check_report(plan: dict, report: dict, gate: str) -> list[str]:
    errors = []
    if report.get("head") != plan.get("head"):
        errors.append("report head does not match plan head")
    results = report.get("results", [])
    if not isinstance(results, list):
        return errors + ["results must be a list"]
    by_id = {}
    for item in results:
        if not isinstance(item, dict) or not isinstance(item.get("id"), str):
            errors.append("every result needs a string id")
            continue
        if item["id"] in by_id:
            errors.append(f"duplicate result: {item['id']}")
        by_id[item["id"]] = item
    required = set(plan.get("prRequired", []))
    if gate == "station":
        required.update(plan.get("stationAcceptanceRequired", []))
    for suite in sorted(required):
        item = by_id.get(suite)
        if item is None:
            errors.append(f"{suite}: missing result")
        elif item.get("status") != "PASS":
            errors.append(f"{suite}: {item.get('status', 'missing status')} is not PASS")
        elif not isinstance(item.get("evidence"), str) or not item["evidence"].strip():
            errors.append(f"{suite}: PASS needs an evidence path or run URL")
    return errors


def git_output(*args: str) -> str:
    return subprocess.check_output(["git", *args], text=True).strip()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    plan_cmd = sub.add_parser("plan", help="Generate scope; do not claim tests passed")
    plan_cmd.add_argument("--base", help="Git base revision, using merge-base diff")
    plan_cmd.add_argument("--head", default="HEAD", help="Git head revision")
    plan_cmd.add_argument("--station", choices=[f"S{i:02d}" for i in range(1, 12)])
    plan_cmd.add_argument("--changed-file", action="append", default=[])
    plan_cmd.add_argument("--add-suite", action="append", choices=sorted(SUITES), default=[])
    plan_cmd.add_argument("--output", type=Path)
    check_cmd = sub.add_parser("check", help="Reject missing, skipped or unevidenced required results")
    check_cmd.add_argument("--plan", type=Path, required=True)
    check_cmd.add_argument("--report", type=Path, required=True)
    check_cmd.add_argument("--gate", choices=["pr", "station"], default="pr")
    args = parser.parse_args()
    try:
        if args.command == "plan":
            if not args.base and not args.changed_file:
                parser.error("plan requires --base or --changed-file")
            paths = list(args.changed_file)
            if args.base:
                paths += git_output("diff", "--name-only", "--diff-filter=ACMRT", f"{args.base}...{args.head}").splitlines()
            head = git_output("rev-parse", args.head)
            result = make_plan(paths, args.station, head, args.add_suite)
            output = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
            if args.output:
                args.output.parent.mkdir(parents=True, exist_ok=True)
                args.output.write_text(output, encoding="utf-8")
            else:
                print(output, end="")
            return 0
        plan = json.loads(args.plan.read_text(encoding="utf-8"))
        report = json.loads(args.report.read_text(encoding="utf-8"))
        errors = check_report(plan, report, args.gate)
        for error in errors:
            print(error, file=sys.stderr)
        if errors:
            return 1
        print(f"PASS: {args.gate} evidence manifest has every required suite")
        return 0
    except (subprocess.CalledProcessError, OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
