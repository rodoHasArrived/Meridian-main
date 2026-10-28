#!/usr/bin/env python3
"""Regenerate the maintained browser/docs outputs to a bounded fixed point.

The explicit catalog below owns generation for CI, Documentation Automation, and
isolated repair. The adjacent output policy only authorizes writes. This command
does not stage, commit, or publish files.
"""

from __future__ import annotations

import argparse
import json
import re
import shlex
import shutil
import stat
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

import yaml


@dataclass(frozen=True)
class Step:
    name: str
    command: tuple[str, ...]
    optional: bool = False


POLICY = "build/scripts/maintained-generation-outputs.json"
REPORT = "artifacts/maintained-generation/report.json"
# Docker Hub's official PlantUML manifest: registry-1.docker.io/v2/plantuml/plantuml/manifests/latest
# Verified Docker-Content-Digest against SHA-256(response body) on 2026-10-20.
PLANTUML_IMAGE = "plantuml/plantuml@sha256:9b5071ab56a4192aac458ab4b0d42261c22c8109695894ae5be6013c3be6cac1"

BROWSER_STEPS = (
    Step("Generate UI API routes", ("python3", "build/scripts/generate-ui-api-routes-ts.py")),
    Step("Generate workspace catalog", ("python3", "build/scripts/generate-workspace-catalog-ts.py")),
    Step("Build browser workstation", ("npm", "--prefix", "src/Meridian.Ui/dashboard", "run", "build")),
)

# Add producers here, not as extra workflow commands. All callers then share the
# same required steps, ordering, bounded convergence, and write protection.
# The core profile includes generate-workflow-manifest; do not run it twice.
DOCS_STEPS = (
    Step("Validate roadmap registry", ("python3", "build/scripts/docs/validate-roadmap-registry.py", "--summary")),
    Step("Validate source READMEs", ("python3", "build/scripts/docs/validate-source-readmes.py", "--summary")),
    Step("Scan source TODOs", ("python3", "build/scripts/docs/scan-source-todos.py", "--summary")),
    Step("Render adapter readiness", ("python3", "build/scripts/docs/render-adapter-readiness.py")),
    Step("Render roadmap docs", ("python3", "build/scripts/docs/render-roadmap-docs.py", "--summary")),
    Step("Render source docs", ("python3", "build/scripts/docs/render-source-docs.py", "--summary")),
    Step("Render roadmap diagrams", ("python3", "build/scripts/docs/render-roadmap-diagrams.py", "--summary")),
    Step("Render source diagrams", ("python3", "build/scripts/docs/render-source-diagrams.py", "--summary")),
    Step("Render WPF UI diagrams", ("npm", "run", "generate-diagrams")),
    Step("Render UML diagrams", ("bash", "-e", "-c",
        f'docker run --rm --user "$(id -u):$(id -g)" -v "$PWD/docs/diagrams/uml:/data" {PLANTUML_IMAGE} -tpng /data/*.puml\n'
        f'docker run --rm --user "$(id -u):$(id -g)" -v "$PWD/docs/diagrams/uml:/data" {PLANTUML_IMAGE} -tsvg /data/*.puml'), True),
    Step("Refresh workflow overview", (
        "python3", "build/scripts/docs/generate-structure-docs.py",
        "--workflows-only", "--output", "docs/generated/workflows-overview.md")),
    Step("Run docs automation profile", (
        "python3", "build/scripts/docs/run-docs-automation.py", "--profile", "core",
        "--summary-output", "docs/status/docs-automation-summary.md",
        "--json-output", "docs/status/docs-automation-summary.json")),
)


def discover_steps(root: Path, *, docs_only: bool = False) -> list[Step]:
    """Return the reviewed catalog without interpreting workflow shell commands.

    Keep this API for isolated repair and existing local callers. The root is
    intentionally not used for executable discovery: a workflow is a consumer of
    the catalog, and cannot recursively add itself or an unrelated shell command.
    """
    return list(DOCS_STEPS if docs_only else BROWSER_STEPS + DOCS_STEPS)


def load_policy(root: Path) -> dict:
    policy = json.loads((root / POLICY).read_text(encoding="utf-8"))
    modules = yaml.safe_load((root / "docs/source/data/source-modules.yml").read_text(encoding="utf-8"))
    for module in modules["modules"]:
        policy["hybrid_files"][module["readme"]] = [
            [f"<!-- {block}:begin module={module['id']} -->", f"<!-- {block}:end -->"]
            for block in ("source-roadmap-traceability", "source-todos")
        ]
    return policy


def visible_paths(root: Path) -> list[str]:
    names = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=root
    ).decode("utf-8").split("\0")
    return sorted(set(names) - {""})


def snapshot(root: Path) -> dict[str, bytes]:
    """Capture content, including nonignored new files and materialized deletions.

    Git status alone is insufficient: already-dirty files can change again.
    Ignored dependency caches and artifacts are deliberately outside this view.
    """
    names = visible_paths(root)
    for name in names:
        if (root / name).is_symlink():
            raise ValueError(f"Symlinks are not supported in generation snapshots: {name}")
    result = {}
    for name in names:
        path = root / name
        if path.is_file():
            result[name] = path.read_bytes()
    return result


def changes(before: dict[str, bytes], after: dict[str, bytes]) -> dict[str, list[str]]:
    return {
        "added": sorted(after.keys() - before.keys()),
        "modified": sorted(path for path in before.keys() & after.keys() if before[path] != after[path]),
        "deleted": sorted(before.keys() - after.keys()),
    }


def protected_content(path: str, content: bytes, policy: dict) -> bytes | None:
    """Return immutable bytes, or None for a whole generated output.

    Retain markers in the immutable view. Duplicate, missing, nested or reversed
    boundaries cannot expand a generator's write authority.
    """
    if path in policy.get("protected_suffixes", {}):
        marker = policy["protected_suffixes"][path].encode()
        return content[content.index(marker):] if marker in content else b""
    if path in policy["whole_files"] or any(
        path.startswith(tree.rstrip("/") + "/") for tree in policy["whole_trees"]
    ):
        return None
    spans = []
    for start, end in policy["hybrid_files"].get(path, []):
        start_bytes, end_bytes = start.encode(), end.encode()
        if content.count(start_bytes) != 1 or content.count(end_bytes) != 1:
            raise ValueError(f"Missing or duplicate generated markers: {path}")
        left, right = content.index(start_bytes) + len(start_bytes), content.index(end_bytes)
        if right < left:
            raise ValueError(f"Reversed generated markers: {path}")
        spans.append((left, right))
    immutable, cursor = [], 0
    for left, right in sorted(spans):
        if left < cursor:
            raise ValueError(f"Overlapping generated markers: {path}")
        immutable.append(content[cursor:left])
        cursor = right
    immutable.append(content[cursor:])
    return b"".join(immutable)


def validate_changes(before: dict[str, bytes], after: dict[str, bytes], policy: dict) -> list[str]:
    violations = []
    for paths in changes(before, after).values():
        for path in paths:
            try:
                old = protected_content(path, before.get(path, b""), policy)
                new = protected_content(path, after.get(path, b""), policy)
                presence_changed = (path not in before or path not in after) and path not in policy.get("protected_suffixes", {})
                if old is not None and (presence_changed or old != new):
                    violations.append(path)
            except ValueError:
                violations.append(path)
    return sorted(violations)


def log_path(root: Path, step: Step) -> Path:
    slug = re.sub(r"[^a-z0-9]+", "-", step.name.lower()).strip("-")
    return root / "artifacts/maintained-generation/logs" / f"{slug}.log"


def execute_step(root: Path, step: Step) -> int:
    command = list(step.command)
    if command[0] in {"python", "python3"}:
        command[0] = sys.executable
    elif command[0] == "npm":
        command[0] = shutil.which("npm") or "npm"
    log = log_path(root, step)
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open("w", encoding="utf-8") as output:
        try:
            return subprocess.run(command, cwd=root, stdout=output, stderr=subprocess.STDOUT, check=False).returncode
        except OSError as exc:
            output.write(f"{exc}\n")
            return 127


def restore_paths(root: Path, before: dict[str, bytes], paths: list[str]) -> None:
    for path in paths:
        target = root / path
        if target.is_symlink():
            target.unlink()
        originals = [name for name in before if name == path or name.startswith(path + "/")]
        if originals:
            for name in originals:
                target = root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(before[name])
        elif target.exists():
            target.unlink()


def run_generation(root: Path, steps: list[Step], policy: dict, max_passes: int = 3,
                   runner=execute_step) -> dict:
    if max_passes < 1:
        raise ValueError("max_passes must be positive")
    before = snapshot(root)
    before_modes = {path: stat.S_IMODE((root / path).stat().st_mode) for path in before}
    # Check hybrid boundaries before any subprocess can use an append fallback.
    for path in policy["hybrid_files"]:
        protected_content(path, before.get(path, b""), policy)
    report = {"successful": False, "converged": False, "passes": [], "failed_steps": [],
              "protection_violations": [], "changed_outputs": changes(before, before)}
    previous = before
    optional_done = set()
    for number in range(1, max_passes + 1):
        iteration = {"number": number, "steps": [], "changed_outputs": {}}
        report["passes"].append(iteration)
        for step in steps:
            # Freshness-excluded UML binaries are advisory and need only one attempt.
            if step.optional and step.name in optional_done:
                continue
            print(f"Pass {number}: {step.name}", flush=True)
            try:
                code = runner(root, step)
            except OSError as exc:
                print(str(exc), file=sys.stderr)
                code = 127
            result = {"name": step.name, "command": list(step.command), "return_code": code,
                      "optional": step.optional, "status": "failed" if code else "success",
                      "log": log_path(root, step).relative_to(root).as_posix()}
            iteration["steps"].append(result)
            if step.optional:
                optional_done.add(step.name)
            if code:
                report["failed_steps"].append({**result, "pass": number})
                print(f"  {'Advisory' if step.optional else 'Required'} step failed ({code}); see {result['log']}")
            links = [path for path in visible_paths(root) if (root / path).is_symlink()]
            if links:
                restore_paths(root, before, links)
            after = snapshot(root)
            violations = validate_changes(before, after, policy)
            mode_changes = [
                path for path in after
                if (stat.S_IMODE((root / path).stat().st_mode) & 0o111)
                != (before_modes.get(path, 0) & 0o111)
            ]
            violations = sorted(set(violations + mode_changes))
            if violations or links:
                # Roll back only unauthorized changes, to invocation-start bytes,
                # preserving user edits that existed before this command.
                restore_paths(root, before, violations)
                for path in set(violations + links) & before_modes.keys():
                    (root / path).chmod(before_modes[path])
                report["protection_violations"] = sorted(set(violations + links))
                break
        current = snapshot(root)
        iteration["changed_outputs"] = changes(previous, current)
        report["changed_outputs"] = changes(before, current)
        if report["protection_violations"] or any(not step["optional"] for step in report["failed_steps"]):
            break
        if not any(iteration["changed_outputs"].values()):
            report["converged"] = True
            report["successful"] = True
            break
        previous = current
    if not report["converged"]:
        report["error"] = ("Generation failed or changed protected content" if any(not step["optional"] for step in report["failed_steps"])
                           or report["protection_violations"] else f"No fixed point after {max_passes} passes")
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument("--max-passes", type=int, default=3, choices=range(1, 11), metavar="1..10")
    parser.add_argument("--dry-run", action="store_true", help="Show the maintained catalog without writing anything")
    parser.add_argument("--docs-only", action="store_true", help="Run source/roadmap and workflow documentation generators; browser validation remains in its own lane")
    parser.add_argument("--report", type=Path, default=Path(REPORT), help="JSON report under ignored artifacts/")
    args = parser.parse_args()
    root = args.repo.resolve()
    try:
        steps = discover_steps(root, docs_only=args.docs_only)
        if args.dry_run:
            for step in steps:
                print(f"{'Advisory' if step.optional else 'Required'}: {step.name}: {shlex.join(step.command)}")
            return 0
        report_path = (root / args.report).resolve()
        if not report_path.is_relative_to(root / "artifacts"):
            raise ValueError("Reports must be under ignored artifacts/ to avoid generator input churn")
        report = run_generation(root, steps, load_policy(root), args.max_passes)
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        for kind, paths in report["changed_outputs"].items():
            for path in paths:
                print(f"{kind}: {path}")
        print(f"Converged: {report['converged']}; failed steps: {len(report['failed_steps'])}")
        if report.get("error"):
            print(report["error"], file=sys.stderr)
        if report["protection_violations"]:
            print("Protected changes restored: " + ", ".join(report["protection_violations"]), file=sys.stderr)
        print(f"Report: {report_path.relative_to(root)}")
        return 0 if report["successful"] else 1
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as exc:
        print(f"Generation precondition failed: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
