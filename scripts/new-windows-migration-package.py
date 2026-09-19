#!/usr/bin/env python3
"""Build the one-time Windows development migration package.

The package preserves Git refs and verified authority documents. It is not a
FullSim or production deployment artifact.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import tarfile
import tempfile
from datetime import datetime, timezone
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


ROOT = Path(__file__).resolve().parents[1]
AUTHORITY_FILES = [
    "docs/authoritative-sources.md",
    "docs/architecture/architecture-v1.3.md",
    "docs/architecture/source.json",
    "docs/contracts/source/PLC与上位机通信接口协议-20260911.docx",
]
AUTHORITY_DIRS = ["docs/process-diagrams/upper-lower-v13"]
OPERATION_FILES = [
    "docs/windows-primary-migration.md",
    "docs/windows-acceptance.md",
    "docs/team-development-and-release.md",
    "docs/station-delivery-order.md",
    "docs/adaptive-test-workflow.md",
    "docs/testing.md",
    "docs/fullsim-coverage-plan.md",
]
SCRIPT_FILES = [
    "scripts/Restore-GaodeWorkspace.ps1",
    "scripts/Test-WindowsDevelopmentEnvironment.ps1",
]


def run(*args: str, cwd: Path = ROOT, text: bool = True) -> str | bytes:
    result = subprocess.check_output(args, cwd=cwd, text=text)
    return result.strip() if text else result


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def files_at_ref(ref: str, relative: str) -> list[str]:
    result = run("git", "-c", "core.quotePath=false", "ls-tree", "-r", "--name-only", ref, "--", relative)
    return result.splitlines() if result else []


def copy_from_ref(ref: str, relative: str, destination: Path) -> None:
    data = run("git", "show", f"{ref}:{relative}", text=False)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(data)


def add_repo_files(stage: Path, ref: str) -> None:
    for relative in AUTHORITY_FILES:
        name = Path(relative).name
        category = "plc-protocol" if relative.startswith("docs/contracts/") else \
            "architecture" if relative.startswith("docs/architecture/") else "index"
        copy_from_ref(ref, relative, stage / "authoritative" / category / name)
    for directory in AUTHORITY_DIRS:
        for relative in files_at_ref(ref, directory):
            copy_from_ref(ref, relative, stage / "authoritative" / "process-diagrams" / Path(relative).name)
    for relative in OPERATION_FILES:
        copy_from_ref(ref, relative, stage / "operations" / Path(relative).name)
    for relative in SCRIPT_FILES:
        copy_from_ref(ref, relative, stage / "scripts" / Path(relative).name)


def make_git_bundle(stage: Path, main_commit: str) -> list[dict[str, str]]:
    # Refresh remote branch tips, then convert them into normal heads in an
    # isolated bare repo. This makes an offline clone restore every open branch.
    subprocess.run(["git", "fetch", "origin", "--prune", "+refs/heads/*:refs/remotes/origin/*"],
                   cwd=ROOT, check=True)
    refs_text = run("git", "for-each-ref", "--format=%(refname) %(objectname)", "refs/remotes/origin")
    refs = []
    with tempfile.TemporaryDirectory(prefix="gaode-mirror-") as temporary:
        mirror = Path(temporary) / "mirror.git"
        subprocess.run(["git", "init", "--bare", str(mirror)], check=True, capture_output=True)
        subprocess.run(["git", "-C", str(mirror), "fetch", str(ROOT),
                        "+refs/remotes/origin/*:refs/heads/*"], check=True, capture_output=True)
        subprocess.run(["git", "-C", str(mirror), "update-ref", "refs/heads/main", main_commit], check=True)
        for line in refs_text.splitlines():
            ref, oid = line.split()
            name = ref.removeprefix("refs/remotes/origin/")
            if name == "HEAD":
                continue
            refs.append({"branch": name, "commit": oid})
        subprocess.run(["git", "-C", str(mirror), "symbolic-ref", "HEAD", "refs/heads/main"], check=True)
        bundle = stage / "repository" / "gaode-repository.bundle"
        bundle.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(["git", "-C", str(mirror), "bundle", "create", str(bundle), "--all"], check=True)
        subprocess.run(["git", "bundle", "verify", str(bundle)], cwd=ROOT, check=True,
                       stdout=subprocess.DEVNULL)
        with tempfile.TemporaryDirectory(prefix="gaode-restore-check-") as clone_dir:
            restored = Path(clone_dir) / "repo"
            subprocess.run(["git", "clone", "--quiet", str(bundle), str(restored)], check=True)
            restored_head = run("git", "rev-parse", "HEAD", cwd=restored)
            if restored_head != main_commit:
                raise RuntimeError(f"bundle restored {restored_head}, expected main {main_commit}")
    return sorted(refs, key=lambda item: item["branch"])


def copy_file(source: Path | None, destination: Path, expected: str | None = None) -> None:
    if source is None:
        return
    if not source.is_file():
        raise FileNotFoundError(source)
    if expected and sha256(source) != expected:
        raise ValueError(f"SHA-256 mismatch: {source}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)


def archive_directory(source: Path | None, destination: Path, exclude_office_temp: bool = False) -> None:
    if source is None:
        return
    if not source.is_dir():
        raise NotADirectoryError(source)
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tarfile.open(destination, "w:gz") as archive:
        for path in sorted(source.rglob("*")):
            if path.is_symlink() or not path.is_file():
                continue
            if exclude_office_temp and path.name.startswith("~$"):
                continue
            archive.add(path, arcname=path.relative_to(source))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--main-ref", default="origin/main")
    parser.add_argument("--virtual-plc-snapshot", type=Path)
    parser.add_argument("--pj-backup", type=Path)
    parser.add_argument("--pj-repository", type=Path)
    parser.add_argument("--pj-reference-dir", type=Path)
    args = parser.parse_args()

    main_commit = run("git", "rev-parse", args.main_ref)
    protocol = ROOT / "docs/contracts/source/PLC与上位机通信接口协议-20260911.docx"
    if sha256(protocol) != "a896faf063b5042bd43e0406ed011cb4f233fca37e64ebe1d5cdea11016b6d16":
        raise ValueError("Authoritative 2026-09-11 PLC protocol hash mismatch")
    architecture = ROOT / "docs/architecture/architecture-v1.3.md"
    if sha256(architecture) != "37479578dffac8f1832ff40883a1e452aff54f7e1f42b5ae1d8e26a104bfca2e":
        raise ValueError("Authoritative V1.3 architecture hash mismatch")

    args.output_dir.mkdir(parents=True, exist_ok=True)
    date = datetime.now(timezone.utc).strftime("%Y%m%d")
    package_name = f"Gaode-Windows-Development-Migration-{date}-{main_commit[:8]}"
    with tempfile.TemporaryDirectory(prefix="gaode-windows-migration-") as temporary:
        stage = Path(temporary) / package_name
        add_repo_files(stage, args.main_ref)
        refs = make_git_bundle(stage, main_commit)
        copy_file(args.virtual_plc_snapshot, stage / "continuity" / "VirtualPlc-20260917-source.tar.gz",
                  "0780781b7c09f44b6cbf547181094ed445dc06cc87cebdb82e23e9f5e5814010"
                  if args.virtual_plc_snapshot else None)
        archive_directory(args.pj_backup, stage / "continuity" / "008-before-v13-backup.tar.gz")
        if args.pj_repository:
            stash = subprocess.run(["git", "stash", "show", "-p", "--binary", "stash@{0}"],
                                   cwd=args.pj_repository, check=True, capture_output=True).stdout
            (stage / "continuity").mkdir(parents=True, exist_ok=True)
            (stage / "continuity" / "008-stash.patch").write_bytes(stash)
        archive_directory(args.pj_reference_dir,
                          stage / "archive-not-authoritative" / "pj-reference-documents.tar.gz", True)

        manifest = {
            "schema": "gaode-windows-development-migration-v1",
            "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
            "mainCommit": main_commit,
            "remote": "https://github.com/NickYoung618/x.git",
            "artifactKind": "development-migration-not-fullsim",
            "authority": [
                {"role": "process-order", "path": "authoritative/process-diagrams", "source": "11 diagrams"},
                {"role": "plc-fields", "path": "authoritative/plc-protocol/PLC与上位机通信接口协议-20260911.docx",
                 "sha256": "a896faf063b5042bd43e0406ed011cb4f233fca37e64ebe1d5cdea11016b6d16"},
                {"role": "architecture", "path": "authoritative/architecture/architecture-v1.3.md",
                 "sha256": "37479578dffac8f1832ff40883a1e452aff54f7e1f42b5ae1d8e26a104bfca2e"},
            ],
            "branches": refs,
            "knownNotRun": ["V1.3 motion two-process", "Host/PLC/3D S01 three-process",
                             "WPF FullSim", "real Windows devices", "complete tray"],
        }
        (stage / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        checksums = []
        for path in sorted(stage.rglob("*")):
            if path.is_file() and path.name != "checksums.sha256":
                checksums.append(f"{sha256(path)}  {path.relative_to(stage).as_posix()}")
        (stage / "checksums.sha256").write_text("\n".join(checksums) + "\n", encoding="utf-8")

        zip_path = args.output_dir / f"{package_name}.zip"
        with ZipFile(zip_path, "w", compression=ZIP_DEFLATED, compresslevel=9) as output:
            for path in sorted(stage.rglob("*")):
                if path.is_file():
                    output.write(path, arcname=(Path(package_name) / path.relative_to(stage)).as_posix())
        outer_hash = sha256(zip_path)
        zip_path.with_suffix(".zip.sha256").write_text(f"{outer_hash}  {zip_path.name}\n", encoding="ascii")
        print(json.dumps({"package": str(zip_path), "sha256": outer_hash,
                          "mainCommit": main_commit, "branches": len(refs)}, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
