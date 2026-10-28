"""Read-only focused validation recommendations; required integration checks remain mandatory.

Mappings are conservative suggestions grounded in the task-to-proof matrix, existing
package scripts, exact Python test names and a transitive ProjectReference graph.
MSBuild is not evaluated: dynamic references produce explicit coverage warnings.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path, PurePosixPath
import shlex
import subprocess
import xml.etree.ElementTree as ET

MATRIX = "docs/ai/codex/quickstart.md"
REGISTRY = "docs/source/data/source-modules.yml"
BROWSER = "src/Meridian.Ui/dashboard"
SHARED = ("src/Meridian.Contracts", "src/Meridian.Ui.Shared", "src/Meridian.Ui.Services")
IGNORED_PARTS = {"obj", "bin", "node_modules", ".git", "artifacts"}


def normalize_path(root: Path, value: str) -> str:
    text = value.replace("\\", "/")
    path = PurePosixPath(text)
    if not text or path.is_absolute() or ".." in path.parts or ":" in text or text.startswith("-"):
        raise ValueError(f"expected a repository-relative path without traversal: {value}")
    result = path.as_posix()
    if result == "." or not (root / result).resolve().is_relative_to(root.resolve()):
        raise ValueError(f"path escapes repository: {value}")
    return result


def collect_changed_paths(root: Path) -> list[str]:
    paths = set()
    for args in (("diff", "--no-renames", "--name-only", "-z"), ("diff", "--cached", "--no-renames", "--name-only", "-z"),
                 ("ls-files", "--others", "--exclude-standard", "-z")):
        proc = subprocess.run(["git", *args], cwd=root, capture_output=True)
        if proc.returncode:
            raise ValueError(f"git {' '.join(args)} failed: {proc.stderr.decode(errors='replace').strip()}")
        paths.update(normalize_path(root, p.decode("utf-8")) for p in proc.stdout.split(b"\0") if p)
    return sorted(paths)


def recommend_validation(root: Path, paths: list[str]) -> dict:
    root = root.resolve()
    if not root.is_dir():
        raise ValueError("repository root must be an existing directory")
    paths = sorted({normalize_path(root, path) for path in paths})
    checks: dict[tuple[str, ...], dict] = {}
    warnings: set[str] = set()
    consumers: set[str] = set()
    sources: dict[str, str] = {}

    def track(path: str) -> bool:
        safe = normalize_path(root, path)
        candidate = root / safe
        if not candidate.is_file():
            return False
        sources[safe] = hashlib.sha256(candidate.read_bytes()).hexdigest()
        return True

    def add(identifier: str, argv: list[str], reason: str, affected: list[str]) -> None:
        key = tuple(argv)
        if key not in checks:
            checks[key] = {"id": identifier, "argv": argv, "reason": reason, "paths": []}
        checks[key]["paths"] = sorted(set(checks[key]["paths"]) | set(affected))

    def python_check(path: str, affected: list[str], reason: str) -> None:
        if track(path):
            add(Path(path).stem, ["python", path, "--summary"], reason, affected)
        else:
            warnings.add(f"Expected validator is unavailable: {path}; select proof manually.")

    for metadata in (MATRIX, REGISTRY, "Directory.Build.props", "tests/Directory.Build.props"):
        if not track(metadata):
            warnings.add(f"Validation metadata unavailable: {metadata}.")
    for metadata in ("Directory.Packages.props", "Directory.Build.targets", "global.json", "NuGet.Config",
                     "build/python/cli/buildctl.py"):
        track(metadata)
    # Changes to the recommendation implementation invalidate packets built from its mapping.
    module = Path(__file__).resolve()
    if module.is_relative_to(root):
        track(module.relative_to(root).as_posix())
    for path in paths:
        if not (root / path).exists():
            warnings.add(f"Changed path is absent (deleted or planned): {path}; using path/dependency coverage.")
    if paths:
        add("diff-check", ["git", "diff", "--check", "--", *paths], "Unstaged whitespace check", paths)
        add("diff-check-staged", ["git", "diff", "--cached", "--check", "--", *paths], "Staged whitespace check", paths)

    projects: dict[str, set[str]] = {}
    test_projects: set[str] = set()
    for tree in ("src", "tests"):
        for project in sorted((root / tree).rglob("*.*proj")):
            if project.suffix not in {".csproj", ".fsproj"} or IGNORED_PARTS & set(project.relative_to(root).parts):
                continue
            relative = normalize_path(root, project.relative_to(root).as_posix())
            track(relative)
            projects[relative] = set()
            try:
                xml = ET.fromstring(project.read_text(encoding="utf-8"))
            except (ET.ParseError, UnicodeError) as exc:
                warnings.add(f"Cannot parse {relative}: {exc}; dependency coverage is unknown.")
                continue
            test_flags = [element for element in xml.iter() if element.tag.split("}")[-1] == "IsTestProject"]
            flag_values = [(flag.text or "").strip().lower() for flag in test_flags]
            has_test_sdk = any(element.tag.split("}")[-1] == "PackageReference" and
                               element.get("Include", "").lower() in {"microsoft.net.test.sdk", "xunit", "xunit.v3",
                                                                       "nunit", "mstest.testframework", "expecto"}
                               for element in xml.iter())
            # A literal false excludes helper executables; conditional/dynamic metadata remains uncertain.
            unconditional_false = any(value == "false" and not flag.get("Condition")
                                      for flag, value in zip(test_flags, flag_values))
            uncertain_flags = any(value not in {"true", "false"} or flag.get("Condition")
                                  for flag, value in zip(test_flags, flag_values))
            if uncertain_flags:
                warnings.add(f"Conditional or dynamic IsTestProject in {relative}; runtime test classification is unknown.")
            if not unconditional_false and ("true" in flag_values or has_test_sdk):
                test_projects.add(relative)
            for element in xml.iter():
                if element.tag.split("}")[-1] != "ProjectReference":
                    continue
                reference = element.get("Include", "").replace("\\", "/")
                if not reference or any(token in reference for token in ("$(", "@(", "%(", "*", "?", ";")):
                    warnings.add(f"Unresolved ProjectReference in {relative}: {reference!r}; dependency coverage is unknown.")
                    continue
                destination = (project.parent / reference).resolve()
                if not destination.is_relative_to(root):
                    warnings.add(f"ProjectReference escapes repository in {relative}: {reference!r}.")
                    continue
                target = destination.relative_to(root).as_posix()
                projects[relative].add(target)
                if not destination.is_file():
                    warnings.add(f"Missing referenced project {target} from {relative}; dependency coverage is incomplete.")

    def add_dotnet(project: str, affected: list[str], reason: str) -> None:
        if not track(project):
            warnings.add(f"Expected test project is unavailable: {project}; select proof manually.")
            return
        argv = ["python", "build/python/cli/buildctl.py", "test", "--project", project, "--queue"]
        if "Wpf" in project:
            argv.append("--full-wpf-build")
        add("dotnet-" + Path(project).stem, argv, reason, affected)
        if "Wpf" in project:
            consumers.add("wpf")
            warnings.add("WPF tests require Windows with full WPF compilation; a non-Windows stub is not consumer proof.")

    reverse: dict[str, set[str]] = {}
    for project, dependencies in projects.items():
        for dependency in dependencies:
            reverse.setdefault(dependency, set()).add(project)
    metadata_paths = [p for p in paths if p.endswith((".props", ".targets")) or Path(p).name == "global.json"]
    if metadata_paths:
        for project in sorted(test_projects):
            add_dotnet(project, metadata_paths, "MSBuild/SDK metadata change requires broad existing project coverage")
        warnings.add("MSBuild/SDK metadata may alter the evaluated dependency graph; broad existing test projects selected, "
                     "but evaluated coverage is unknown and requires review.")
    browser_paths = []
    shared_paths = [path for path in paths if any(path == prefix or path.startswith(prefix + "/") for prefix in SHARED)]
    for path in paths:
        if path in metadata_paths:
            continue
        if path.startswith(BROWSER + "/"):
            browser_paths.append(path)
            continue
        owning = [project for project in projects if path == project or path.startswith(str(Path(project).parent) + "/")]
        max_parent = max((len(str(Path(p).parent)) for p in owning), default=0)
        owners = [p for p in owning if len(str(Path(p).parent)) == max_parent]
        if path.endswith((".csproj", ".fsproj")):
            owners = [path]
        if owners:
            affected = set()
            pending = owners[:]
            while pending:
                project = pending.pop()
                if project in affected:
                    continue
                affected.add(project)
                pending.extend(reverse.get(project, set()))
            for project in sorted(affected & test_projects):
                add_dotnet(project, [path], "Changed project or transitive ProjectReference consumer")
            if not affected & test_projects:
                warnings.add(f"No existing test project mapped to {path}; select focused proof manually.")
            continue
        if path.endswith(".py"):
            name = Path(path).stem.replace("-", "_")
            test_name = name if name.startswith("test_") else "test_" + name
            matches = []
            if name.startswith("test_") and (root / path).is_file():
                matches.append(path)
            for folder in ("tests/scripts", "build/scripts/ai/tests", "build/scripts/docs/tests", "build/python/tests"):
                candidate = f"{folder}/{test_name}.py"
                if (root / candidate).is_file():
                    matches.append(candidate)
            for candidate in sorted(set(matches)):
                track(candidate)
                add("python-" + candidate.removesuffix(".py").replace("/", "-"), ["python", "-m", "unittest", "discover", "-s",
                    str(Path(candidate).parent), "-p", Path(candidate).name], "Exact existing Python test filename", [path])
            if not matches:
                warnings.add(f"No exact Python test filename found for {path}; select focused proof manually.")
        elif not (path.startswith(("docs/", ".codex/", ".claude/", ".agents/")) or path.endswith(".md")):
            warnings.add(f"No focused mapping for {path}; select proof manually.")

    if shared_paths:
        consumers.update(("browser", "wpf"))
        browser_paths.extend(shared_paths)
        for project in ("tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj",
                        "tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj",
                        "tests/Meridian.Tests/Meridian.Tests.csproj"):
            add_dotnet(project, shared_paths, "Shared contract/service seam requires UI consumer coverage")
    if browser_paths:
        consumers.add("browser")
        manifest = BROWSER + "/package.json"
        scripts = {}
        if track(manifest):
            try:
                scripts = json.loads((root / manifest).read_text(encoding="utf-8")).get("scripts", {})
                if not isinstance(scripts, dict):
                    raise ValueError("scripts must be an object")
            except (ValueError, AttributeError):
                scripts = {}
                warnings.add("Browser package scripts cannot be parsed; select consumer proof manually.")
        for script in ("lint", "typecheck:strict", "build"):
            if script in scripts:
                add("browser-" + script, ["npm", "--prefix", BROWSER, "run", script],
                    "Browser workstation package validation", browser_paths)
            else:
                warnings.add(f"Browser package script unavailable: {script}.")
        targets = set()
        broad = bool(shared_paths)
        for path in browser_paths:
            relative = path.removeprefix(BROWSER + "/")
            candidates = [path] if ".test." in path else [str(Path(path).with_suffix(suffix))
                          for suffix in (".test.ts", ".test.tsx", ".test.js", ".test.jsx")]
            matches = [p for p in candidates if (root / p).is_file() and ".test." in p]
            targets.update(p.removeprefix(BROWSER + "/") for p in matches)
            broad |= not bool(matches)
        if "test" in scripts:
            argv = ["npm", "--prefix", BROWSER, "run", "test"]
            if targets and not broad:
                argv += ["--", *sorted(targets)]
                for target in targets:
                    track(BROWSER + "/" + target)
            add("browser-test", argv, "Shared/general browser coverage" if broad else "Exact browser test files", browser_paths)
        else:
            warnings.add("Browser test script is unavailable; select consumer proof manually.")

    ai_paths = [p for p in paths if p.startswith(("docs/ai/", ".codex/", ".claude/", ".agents/", "build/scripts/ai/"))]
    if ai_paths:
        for script in ("check-ai-inventory.py", "check-codex-skills.py"):
            python_check("build/scripts/docs/" + script, ai_paths, "Task-to-proof matrix AI tooling/guidance check")
    if any(p.startswith(".codex/memory/") for p in paths):
        python_check("build/scripts/docs/check-codex-memory.py", paths, "Codex memory routing changed")
    if not paths:
        warnings.add("No changed paths supplied or discovered; no focused checks selected.")
    return {"schemaVersion": 1, "changedPaths": paths, "checks": sorted(checks.values(), key=lambda c: (c["id"], c["argv"])),
            "affectedConsumers": sorted(consumers), "coverageWarnings": sorted(warnings),
            "sources": [{"path": path, "sha256": sha} for path, sha in sorted(sources.items())],
            "requiredFinalChecks": [
                {"id": "local-ci", "argv": ["bash", "scripts/ci.sh"], "reason": "Required before completion"},
                {"id": "quality-gate", "kind": "github", "name": "Meridian CI / quality-gate"},
                {"id": "integration-gate", "kind": "github", "name": "Meridian CI / integration-gate"}],
            "policy": "Recommendations only; no commands executed. Focused checks do not replace required final checks."}


def run_cli(args) -> int:
    try:
        root = args.root.resolve()
        paths = list(args.changed_file or [])
        if args.from_git or not paths:
            paths.extend(collect_changed_paths(root))
        data = recommend_validation(root, paths)
        if args.json:
            content = json.dumps(data, indent=2) + "\n"
        else:
            lines = [data["policy"], ""]
            lines += [shlex.join(check["argv"]) + "  # " + check["reason"] for check in data["checks"]]
            lines += ["Coverage warning: " + warning for warning in data["coverageWarnings"]]
            lines += ["Required final checks: bash scripts/ci.sh; Meridian CI / quality-gate; Meridian CI / integration-gate"]
            content = "\n".join(lines) + "\n"
        if args.output:
            output = args.output.resolve()
            protected = {(root / source["path"]).resolve() for source in data["sources"]} | {
                (root / path).resolve() for path in data["changedPaths"]}
            if output in protected:
                raise ValueError("output may not overwrite a source or changed path")
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(content, encoding="utf-8")
        else:
            print(content, end="")
        return 0
    except (OSError, ValueError, UnicodeError) as exc:
        import sys
        print(f"recommend-validation: {exc}", file=sys.stderr)
        return 1
