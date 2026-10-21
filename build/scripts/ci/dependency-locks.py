#!/usr/bin/env python3
"""Stage, verify, or adopt reviewed NuGet lock baselines for an explicit OS/RID.

Generation uses a disposable checkout and copies only the current restore inputs
over its pinned commit. No build output or candidate lock enters the caller's
checkout. Adoption checks the complete input fingerprint and candidate digests;
normal CI does not opt in until the supported profiles have been reviewed.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import tempfile


PROFILES = {
    "linux-portable": {"system": "Linux", "runtime": None},
    "windows-portable": {"system": "Windows", "runtime": None},
    "win-x64": {"system": "Windows", "runtime": "win-x64"},
    "win-arm64": {"system": "Windows", "runtime": "win-arm64"},
}
LOCK_ROOT = Path("build/nuget-locks")
INPUT_SUFFIXES = {".csproj", ".fsproj", ".props", ".targets", ".sln", ".slnf", ".slnx"}
INPUT_NAMES = {"global.json", "nuget.config", "nuget.config.json", "packages.config"}


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def run(command: list[str], root: Path, log: Path | None = None) -> str:
    result = subprocess.run(command, cwd=root, capture_output=True, text=True,
                            encoding="utf-8", errors="replace", timeout=1800,
                            env={**os.environ, "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
                                 "MSBUILDDISABLENODEREUSE": "1"})
    if log:
        log.write_text(result.stdout + result.stderr, encoding="utf-8")
    if result.returncode:
        raise ValueError(f"{command[0]} failed ({result.returncode}); see {log or 'command output'}: "
                         + (result.stderr or result.stdout)[-1500:])
    return result.stdout.strip()


def restore_inputs(root: Path) -> dict[str, str]:
    def is_input(path: str) -> bool:
        return (path and not path.startswith("build/nuget-locks/")
                and (Path(path).suffix.lower() in INPUT_SUFFIXES
                     or Path(path).name.lower() in INPUT_NAMES))

    untracked = run(["git", "ls-files", "--others", "--exclude-standard", "-z"], root).split("\0")
    if any(is_input(path) for path in untracked):
        raise ValueError("Untracked restore inputs must be added to Git before generating or adopting locks")
    tracked = run(["git", "ls-files", "-z"], root).split("\0")
    paths = sorted(path for path in tracked if is_input(path))
    result = {}
    for name in paths:
        path = root / name
        if not path.is_file() or path.is_symlink():
            raise ValueError(f"Restore input must be a regular tracked file: {name}")
        result[name] = digest(path.read_bytes())
    if "global.json" not in result or "Directory.Build.props" not in result or "Meridian.sln" not in result:
        raise ValueError("The global SDK, shared properties, and solution must be tracked restore inputs")
    names = [Path(path).stem for path in result
             if path.startswith(("src/", "tests/", "benchmarks/", "build/dotnet/"))
             and Path(path).suffix in {".csproj", ".fsproj"}]
    if len(names) != len(set(names)):
        raise ValueError("Project names must be unique before using named lock baselines")
    return result


def restore_command(dotnet: str, profile: str, *, generate: bool) -> list[str]:
    command = [dotnet, "restore", "Meridian.sln", "--force", "--verbosity", "minimal",
               "-p:EnableWindowsTargeting=true", f"-p:MeridianNuGetLockProfile={profile}",
               "-p:MeridianGenerateNuGetLocks=" + ("true" if generate else "false"),
               "-p:RestoreLockedMode=" + ("false" if generate else "true"),
               "-nodeReuse:false"]
    if generate:
        command.append("--force-evaluate")
    else:
        command.append("--locked-mode")
    if PROFILES[profile]["runtime"]:
        command += ["--runtime", PROFILES[profile]["runtime"]]
    return command


def solution_locks(root: Path) -> set[str]:
    projects = re.findall(r'^Project\([^\n]+? = "[^"\n]+", "([^"\n]+\.(?:csproj|fsproj))"',
                          (root / "Meridian.sln").read_text(encoding="utf-8"), re.MULTILINE)
    expected = {Path(name.replace("\\", "/")).stem + ".packages.lock.json" for name in projects}
    if not expected or len(expected) != len(projects):
        raise ValueError("Solution lock inventory must contain uniquely named projects")
    return expected


def lock_inventory(root: Path, profile: str) -> dict[str, str]:
    directory = root / LOCK_ROOT / profile
    result = {}
    for path in sorted(directory.glob("*.packages.lock.json")):
        if path.is_symlink() or not path.is_file():
            raise ValueError("Lockfiles must be regular files")
        data = path.read_bytes()
        payload = json.loads(data)
        if payload.get("version") not in (1, 2) or not isinstance(payload.get("dependencies"), dict):
            raise ValueError(f"Invalid NuGet lockfile: {path.name}")
        result[path.name] = digest(data)
    if not result:
        raise ValueError(f"No lockfiles generated for {profile}")
    missing = solution_locks(root) - set(result)
    if missing:
        raise ValueError("Solution projects are missing lockfiles: " + ", ".join(sorted(missing)))
    return result


@contextmanager
def checkout(root: Path, commit: str, inputs: dict[str, str]):
    with tempfile.TemporaryDirectory(prefix="meridian-locks-") as directory:
        work = Path(directory) / "repo"
        run(["git", "clone", "--quiet", "--shared", "--no-checkout", str(root), str(work)], root)
        run(["git", "-c", "core.hooksPath=/dev/null", "checkout", "--quiet", "--detach", commit], work)
        for name in inputs:
            target = work / name
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(root / name, target)
        # Catch edits during snapshotting rather than certify a mixed restore input set.
        if restore_inputs(root) != inputs:
            raise ValueError("Restore inputs changed while preparing the disposable checkout")
        yield work


def stage(root: Path, profile: str, output: Path, dotnet: str, *, generate: bool = True) -> dict:
    if platform.system() != PROFILES[profile]["system"]:
        raise ValueError(f"{profile} requires native {PROFILES[profile]['system']} restore")
    inputs = restore_inputs(root)
    commit = run(["git", "rev-parse", "HEAD"], root)
    output.mkdir(parents=True, exist_ok=False)
    result = dict(schemaVersion=1, profile=profile, sourceCommit=commit,
                  context=PROFILES[profile], inputs=inputs, status="failed")
    try:
        with checkout(root, commit, inputs) as work:
            result["sdk"] = run([dotnet, "--version"], work)
            baseline = work / LOCK_ROOT / profile
            if generate:
                if baseline.exists():
                    shutil.rmtree(baseline)
                baseline.mkdir(parents=True)
                run(restore_command(dotnet, profile, generate=True), work, output / "generate.log")
            else:
                baseline.mkdir(parents=True, exist_ok=True)
                for path in (root / LOCK_ROOT / profile).glob("*.packages.lock.json"):
                    shutil.copyfile(path, baseline / path.name)
            before = lock_inventory(work, profile)
            run(restore_command(dotnet, profile, generate=False), work, output / "verify.log")
            after = lock_inventory(work, profile)
            if before != after:
                raise ValueError("Locked restore modified the candidate lock inventory")
            if restore_inputs(root) != inputs:
                raise ValueError("Restore inputs changed while verifying the candidate")
            locks = output / "locks"
            locks.mkdir()
            for name in before:
                shutil.copyfile(baseline / name, locks / name)
            result.update(status="verified", locks=before,
                          invocation=restore_command(dotnet, profile, generate=False))
    except (ValueError, OSError, subprocess.TimeoutExpired) as exc:
        result["error"] = str(exc)
    (output / "manifest.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return result


def adopt(root: Path, candidate: Path) -> dict:
    manifest = json.loads((candidate / "manifest.json").read_text(encoding="utf-8"))
    profile = manifest.get("profile")
    if (manifest.get("schemaVersion") != 1 or manifest.get("status") != "verified"
            or profile not in PROFILES or manifest.get("context") != PROFILES[profile]):
        raise ValueError("Candidate is not a verified known OS/RID profile")
    if manifest.get("inputs") != restore_inputs(root):
        raise ValueError("Candidate restore inputs differ from this checkout; regenerate")
    if not re.fullmatch(r"[0-9a-f]{40}", manifest.get("sourceCommit", "")):
        raise ValueError("Candidate is missing its source commit")
    locks = manifest.get("locks")
    project_names = {Path(name).stem for name in manifest["inputs"]
                     if name.startswith(("src/", "tests/", "benchmarks/", "build/dotnet/"))
                     and Path(name).suffix in {".csproj", ".fsproj"}}
    if not isinstance(locks, dict) or not locks:
        raise ValueError("Candidate lock inventory is empty")
    if solution_locks(root) - set(locks):
        raise ValueError("Candidate is missing solution project lockfiles")
    expected_files = set(locks)
    actual_files = {path.name for path in (candidate / "locks").iterdir()}
    if expected_files != actual_files:
        raise ValueError("Candidate inventory has missing or unexpected files")
    for name, expected in locks.items():
        if name not in {project + ".packages.lock.json" for project in project_names}:
            raise ValueError(f"Unknown project lockfile: {name}")
        path = candidate / "locks" / name
        if path.is_symlink() or not path.is_file() or digest(path.read_bytes()) != expected:
            raise ValueError(f"Candidate lockfile digest differs: {name}")
        data = json.loads(path.read_bytes())
        if data.get("version") not in (1, 2) or not isinstance(data.get("dependencies"), dict):
            raise ValueError(f"Invalid lockfile: {name}")
    destination = root / LOCK_ROOT / profile
    destination.mkdir(parents=True, exist_ok=True)
    # Never partially replace a previous profile with a different project inventory.
    previous = {p.name for p in destination.glob("*.packages.lock.json")}
    if previous and previous != expected_files:
        raise ValueError("Existing profile inventory differs; review removals before replacement")
    for name in locks:
        shutil.copyfile(candidate / "locks" / name, destination / name)
    (destination / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return {"status": "adopted", "profile": profile, "locks": len(locks),
            "nextAction": f"Run dependency-locks.py verify --profile {profile} on native "
                          f"{PROFILES[profile]['system']} before enabling this profile in CI"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("generate", "verify", "adopt"))
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--profile", choices=PROFILES)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--candidate", type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    try:
        if args.mode == "adopt":
            if not args.candidate:
                parser.error("adopt requires --candidate")
            result = adopt(args.repo.resolve(), args.candidate.resolve())
        else:
            if not args.profile or not args.output:
                parser.error("generate/verify require --profile and a new --output directory")
            result = stage(args.repo.resolve(), args.profile, args.output.resolve(), args.dotnet,
                           generate=args.mode == "generate")
        print(json.dumps(result, indent=2))
        return 0 if result["status"] in {"verified", "adopted"} else 1
    except (ValueError, OSError, subprocess.TimeoutExpired) as exc:
        print(f"Dependency lock operation refused: {exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
