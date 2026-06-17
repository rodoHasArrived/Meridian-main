#!/usr/bin/env python3
"""Map Meridian shared contract changes to likely consumers, tests, docs, and validation."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[4]
SCAN_SURFACES = {
    "services": ("src/Meridian.Application", "src/Meridian.Ui.Services", "src/Meridian.Ui.Shared"),
    "browser": ("src/Meridian.Ui/dashboard",),
    "wpf": ("src/Meridian.Wpf",),
    "tests": ("tests",),
    "docs": ("docs",),
}
TEXT_SUFFIXES = {".cs", ".fs", ".ts", ".tsx", ".md", ".json", ".yml", ".yaml"}
SKIP_PARTS = {"bin", "obj", "node_modules", ".git", ".vs", "TestResults", "coverage"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--path", action="append", required=True, help="Contract path to inspect. Repeatable.")
    parser.add_argument("--summary", action="store_true", help="Emit a compact human-readable summary.")
    parser.add_argument("--json", action="store_true", help="Emit machine-readable JSON.")
    parser.add_argument("--fail-on-missing-test", action="store_true", help="Return nonzero when no tests reference the contract terms.")
    parser.add_argument("--fail-on-missing-doc", action="store_true", help="Return nonzero when no docs reference the contract terms.")
    return parser.parse_args()


def repo_relative(path: Path) -> str:
    try:
        return path.resolve().relative_to(REPO_ROOT.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def resolve_path(value: str) -> Path:
    path = Path(value)
    if not path.is_absolute():
        path = REPO_ROOT / path
    return path.resolve()


def extract_terms(path: Path) -> list[str]:
    terms = {path.stem}
    simplified = re.sub(r"(Dtos|Dto|Models|Model|Contracts|Contract)$", "", path.stem)
    if simplified and simplified != path.stem:
        terms.add(simplified)

    if path.is_file() and path.suffix in {".cs", ".fs"}:
        text = path.read_text(encoding="utf-8", errors="replace")
        for match in re.finditer(r"\b(?:record|class|interface|enum|struct)\s+([A-Z][A-Za-z0-9_]*)", text):
            terms.add(match.group(1))

    return sorted(term for term in terms if len(term) >= 4)


def iter_surface_files(roots: tuple[str, ...]) -> list[Path]:
    files: list[Path] = []
    for root_name in roots:
        root = REPO_ROOT / root_name
        if not root.exists():
            continue
        for path in root.rglob("*"):
            if not path.is_file() or path.suffix not in TEXT_SUFFIXES:
                continue
            if any(part in SKIP_PARTS for part in path.parts):
                continue
            files.append(path)
    return files


def scan_surface(terms: list[str], roots: tuple[str, ...]) -> dict[str, object]:
    rg = shutil.which("rg")
    if rg and terms:
        command = [
            rg,
            "--files-with-matches",
            "--fixed-strings",
            "--glob",
            "!**/bin/**",
            "--glob",
            "!**/obj/**",
            "--glob",
            "!**/node_modules/**",
            "--glob",
            "!**/TestResults/**",
        ]
        for term in terms:
            command.extend(["-e", term])
        command.extend(roots)
        completed = subprocess.run(command, cwd=REPO_ROOT, capture_output=True, text=True, encoding="utf-8", check=False)
        if completed.returncode in {0, 1}:
            files = [line.strip() for line in completed.stdout.splitlines() if line.strip()]
            return {"file_count": len(files), "sample_files": files[:20]}

    matches: list[str] = []
    hit_count = 0
    for path in iter_surface_files(roots):
        text = path.read_text(encoding="utf-8", errors="replace")
        if any(term in text for term in terms):
            hit_count += 1
            if len(matches) < 20:
                matches.append(repo_relative(path))
    return {"file_count": hit_count, "sample_files": matches}


def classify_change(path: Path) -> str:
    text = path.read_text(encoding="utf-8", errors="replace") if path.is_file() else ""
    if "[Obsolete" in text or "ObsoleteAttribute" in text:
        return "compatibility-preserving"
    if path.name.endswith(("Dtos.cs", "Models.cs")):
        return "shared-dto"
    if "I" == path.stem[:1] and path.suffix == ".cs":
        return "interface-contract"
    return "shared-contract"


def validation_commands(path: Path) -> list[str]:
    rel = repo_relative(path)
    commands = ["dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter \"FullyQualifiedName~Contract\""]
    if "src/Meridian.Ui/dashboard" in rel:
        commands.append("npm --prefix src/Meridian.Ui/dashboard run test")
    if "src/Meridian.Contracts" in rel or "src/Meridian.Ui.Shared" in rel:
        commands.append("npm --prefix src/Meridian.Ui/dashboard run build")
    if "src/Meridian.Wpf" in rel:
        commands.append(
            "dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter \"FullyQualifiedName~Workstation\" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true"
        )
    return commands


def inspect_path(path: Path) -> dict[str, object]:
    terms = extract_terms(path)
    surfaces = {name: scan_surface(terms, roots) for name, roots in SCAN_SURFACES.items()}
    return {
        "path": repo_relative(path),
        "exists": path.exists(),
        "classification": classify_change(path) if path.exists() else "missing",
        "terms": terms,
        "surfaces": surfaces,
        "validation_commands": validation_commands(path),
    }


def build_payload(paths: list[Path]) -> dict[str, object]:
    results = [inspect_path(path) for path in paths]
    missing_tests = [result["path"] for result in results if result["surfaces"]["tests"]["file_count"] == 0]
    missing_docs = [result["path"] for result in results if result["surfaces"]["docs"]["file_count"] == 0]
    return {
        "status": "pass",
        "summary": {
            "target_count": len(results),
            "missing_test_reference_count": len(missing_tests),
            "missing_doc_reference_count": len(missing_docs),
        },
        "missing_tests": missing_tests,
        "missing_docs": missing_docs,
        "results": results,
    }


def print_summary(payload: dict[str, object]) -> None:
    summary = payload["summary"]
    assert isinstance(summary, dict)
    print(
        "contract_impact "
        f"status={payload['status']} "
        f"targets={summary['target_count']} "
        f"missing_tests={summary['missing_test_reference_count']} "
        f"missing_docs={summary['missing_doc_reference_count']}"
    )
    for result in payload["results"]:
        assert isinstance(result, dict)
        surface_counts = ", ".join(
            f"{name}={details['file_count']}" for name, details in result["surfaces"].items()
        )
        print(f"- {result['path']}: {result['classification']} terms={len(result['terms'])} {surface_counts}")


def main() -> int:
    args = parse_args()
    payload = build_payload([resolve_path(value) for value in args.path])

    if args.json:
        print(json.dumps(payload, indent=2))
    else:
        print_summary(payload)
        if args.summary:
            for result in payload["results"]:
                print(f"validation for {result['path']}:")
                for command in result["validation_commands"]:
                    print(f"  - {command}")

    if args.fail_on_missing_test and payload["missing_tests"]:
        return 1
    if args.fail_on_missing_doc and payload["missing_docs"]:
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
