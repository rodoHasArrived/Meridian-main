#!/usr/bin/env python3
"""Fail-closed inventory of product surfaces that refuse because nothing implements them.

Meridian keeps routes and task types discoverable even when no implementation stands behind
them, and answers honestly - HTTP 501, or a "not executed" maintenance result - instead of a
404 or a fabricated success. That honesty is only half the job: an honest refusal that no
roadmap item owns is a gap that quietly becomes permanent.

This gate makes every such refusal traceable. A source file under ``src/`` that contains an
*unimplemented surface* must be listed as an ``evidence.path`` on some row of
``docs/roadmap/data/roadmap-items.yml``, or carry an entry in the baseline file with a reason.

An unimplemented surface is any of:

- a call to ``ApiProblemDetails.NotImplemented(`` (the canonical 501 body for a capability
  with no implementation);
- a call to ``BuildNotImplementedResult(`` (a scheduled maintenance task that cannot run);
- a ``Status501NotImplemented`` result (not a ``.Produces(...)`` declaration) whose nearby
  title or detail text says the capability is "not implemented".

A 501 returned only because an optional service is not registered in DI is a host
configuration refusal, not missing code, and is out of scope.

Baseline entries that no longer match a file with an unimplemented surface fail the gate, so
the baseline cannot outlive the gap it excuses.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_ROADMAP = REPO_ROOT / "docs" / "roadmap" / "data" / "roadmap-items.yml"
DEFAULT_BASELINE = REPO_ROOT / "build" / "scripts" / "ci" / "unimplemented-surface-baseline.json"
DEFAULT_SOURCE_DIR = REPO_ROOT / "src"

CANONICAL_HELPER_CALL = re.compile(r"\bApiProblemDetails\.NotImplemented\s*\(")
MAINTENANCE_HELPER_CALL = re.compile(r"\bBuildNotImplementedResult\s*\(")
HELPER_DEFINITION = re.compile(r"\b(?:static|private|public|internal)\b[^;{=]*\bBuildNotImplementedResult\s*\(")
STATUS_501 = re.compile(r"\bStatus501NotImplemented\b")
PRODUCES_DECLARATION = re.compile(r"\.Produces(?:ProblemDetails)?\s*\(")
NOT_IMPLEMENTED_TEXT = re.compile(r"not implemented", re.IGNORECASE)
# Lines either side of a 501 result searched for its title/detail text.
TEXT_WINDOW = 3
# Files that define the helpers rather than use them.
DEFINING_FILES = {
    "src/Meridian.Ui.Shared/Endpoints/ProblemDetails/ApiProblemDetails.cs",
    "src/Meridian.Ui.Shared/Endpoints/ProblemDetails/MeridianApiProblemDetailsServiceCollectionExtensions.cs",
}
EVIDENCE_PATH = re.compile(r"^\s*(?:-\s+)?path:\s*(?P<path>\S+)\s*$")


def find_surface_lines(text: str) -> list[int]:
    """Return 1-based line numbers of unimplemented surfaces in one C# source file."""
    lines = text.splitlines()
    hits: list[int] = []
    for index, line in enumerate(lines):
        stripped = line.strip()
        if stripped.startswith("//") or stripped.startswith("///"):
            continue
        if CANONICAL_HELPER_CALL.search(line):
            hits.append(index + 1)
            continue
        if MAINTENANCE_HELPER_CALL.search(line) and not HELPER_DEFINITION.search(line):
            hits.append(index + 1)
            continue
        if STATUS_501.search(line) and not PRODUCES_DECLARATION.search(line):
            window = lines[max(0, index - TEXT_WINDOW): index + TEXT_WINDOW + 1]
            if any(NOT_IMPLEMENTED_TEXT.search(candidate) for candidate in window):
                hits.append(index + 1)
    return hits


PRUNED_DIRECTORIES = frozenset({"bin", "obj", "node_modules"})


def _iter_cs_files(source_dir: Path):
    # Prune before descending: src/ also holds the dashboard's node_modules tree.
    for current, directories, files in source_dir.walk():
        directories[:] = [name for name in directories if name not in PRUNED_DIRECTORIES]
        yield from (current / name for name in files if name.endswith(".cs"))


def discover_surfaces(source_dir: Path, repo_root: Path) -> dict[str, list[int]]:
    surfaces: dict[str, list[int]] = {}
    for path in sorted(_iter_cs_files(source_dir)):
        relative = path.relative_to(repo_root).as_posix()
        if relative in DEFINING_FILES:
            continue
        try:
            text = path.read_text(encoding="utf-8-sig")
        except UnicodeDecodeError:
            text = path.read_text(encoding="latin-1")
        hits = find_surface_lines(text)
        if hits:
            surfaces[relative] = hits
    return surfaces


def load_roadmap_evidence(roadmap_path: Path) -> set[str]:
    """Collect every evidence path in the roadmap registry.

    A line scan keeps this gate free of a YAML dependency; evidence entries are always written
    as ``path: <repo-relative path>`` lines.
    """
    paths: set[str] = set()
    for line in roadmap_path.read_text(encoding="utf-8").splitlines():
        match = EVIDENCE_PATH.match(line)
        if match:
            paths.add(match.group("path").strip("'\""))
    return paths


def load_baseline(baseline_path: Path) -> list[dict]:
    if not baseline_path.exists():
        return []
    data = json.loads(baseline_path.read_text(encoding="utf-8"))
    entries = data.get("entries", [])
    if not isinstance(entries, list):
        raise ValueError(f"{baseline_path}: 'entries' must be a list")
    return entries


def evaluate(
    surfaces: dict[str, list[int]],
    evidence_paths: set[str],
    baseline: list[dict],
) -> tuple[list[str], dict[str, list[str]]]:
    errors: list[str] = []
    classification: dict[str, list[str]] = {"tracked": [], "baselined": [], "untracked": []}

    baseline_by_path: dict[str, dict] = {}
    for entry in baseline:
        path = str(entry.get("path", "")).strip()
        reason = str(entry.get("reason", "")).strip()
        if not path:
            errors.append("baseline entry is missing 'path'")
            continue
        if not reason:
            errors.append(f"baseline entry {path} is missing 'reason'")
        if path in baseline_by_path:
            errors.append(f"baseline entry {path} is duplicated")
        baseline_by_path[path] = entry

    for path, lines in sorted(surfaces.items()):
        if path in evidence_paths:
            classification["tracked"].append(path)
        elif path in baseline_by_path:
            classification["baselined"].append(path)
        else:
            classification["untracked"].append(path)
            line_list = ", ".join(str(line) for line in lines)
            errors.append(
                f"{path} (line {line_list}) has an unimplemented surface that no roadmap row lists as "
                "evidence; add the file to a roadmap row's evidence or record it in "
                "build/scripts/ci/unimplemented-surface-baseline.json with a reason"
            )

    for path in sorted(baseline_by_path):
        if path not in surfaces:
            errors.append(f"baseline entry {path} matches no unimplemented surface; remove it")
        elif path in evidence_paths:
            errors.append(f"baseline entry {path} is already tracked by a roadmap row; remove it")

    return errors, classification


def parse_arguments(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=REPO_ROOT, help="Repository root.")
    parser.add_argument("--roadmap", type=Path, help="Roadmap items registry (default: repo registry).")
    parser.add_argument("--baseline", type=Path, help="Baseline JSON (default: build/scripts/ci/...).")
    parser.add_argument("--summary", action="store_true", help="Print a compact summary.")
    parser.add_argument("--json-output", type=Path, help="Write the classification as JSON.")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_arguments(argv)
    root = args.root.resolve()
    roadmap = args.roadmap or root / DEFAULT_ROADMAP.relative_to(REPO_ROOT)
    baseline_path = args.baseline or root / DEFAULT_BASELINE.relative_to(REPO_ROOT)

    surfaces = discover_surfaces(root / "src", root)
    evidence = load_roadmap_evidence(roadmap)
    try:
        baseline = load_baseline(baseline_path)
    except (ValueError, json.JSONDecodeError) as exc:
        print(f"unimplemented surfaces: invalid baseline: {exc}", file=sys.stderr)
        return 1

    errors, classification = evaluate(surfaces, evidence, baseline)

    if args.json_output:
        args.json_output.parent.mkdir(parents=True, exist_ok=True)
        args.json_output.write_text(
            json.dumps({"surfaces": surfaces, "classification": classification, "errors": errors}, indent=2) + "\n",
            encoding="utf-8",
        )

    tracked = len(classification["tracked"])
    baselined = len(classification["baselined"])
    print(
        f"unimplemented surfaces: {len(surfaces)} file(s), {tracked} roadmap-tracked, "
        f"{baselined} baselined, {len(errors)} error(s)"
    )
    if not args.summary:
        for path in classification["tracked"]:
            print(f"  tracked:   {path}")
    for path in classification["baselined"]:
        print(f"  baselined: {path}")
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)

    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
