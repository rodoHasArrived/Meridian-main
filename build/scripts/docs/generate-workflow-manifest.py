#!/usr/bin/env python3
"""Generate workflow command references and drift/validation artifacts from a canonical manifest."""

from __future__ import annotations

import argparse
import json
import re
from datetime import datetime, timedelta, timezone
from dataclasses import dataclass
from pathlib import Path
from typing import Any

HELP_BLOCK_START = "<!-- BEGIN AUTO-GENERATED: WORKFLOW-MANIFEST-HELP -->"
HELP_BLOCK_END = "<!-- END AUTO-GENERATED: WORKFLOW-MANIFEST-HELP -->"
DEV_BLOCK_START = "<!-- BEGIN AUTO-GENERATED: WORKFLOW-MANIFEST-DEV -->"
DEV_BLOCK_END = "<!-- END AUTO-GENERATED: WORKFLOW-MANIFEST-DEV -->"


@dataclass(frozen=True)
class WorkflowRef:
    workflow_id: str
    command: str


def write_text_lf(path: Path, content: str) -> None:
    """Write UTF-8 text with LF endings so generated docs match the repo on every platform."""
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(content)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Generate workflow docs/validation artifacts from manifest.")
    parser.add_argument(
        "--manifest",
        default="docs/status/workflow-manifest.json",
        help="Path to workflow manifest JSON.",
    )
    parser.add_argument(
        "--help-doc",
        default="docs/HELP.md",
        help="Path to docs HELP page for snippet injection.",
    )
    parser.add_argument(
        "--dev-doc",
        default="docs/development/documentation-automation.md",
        help="Path to development docs page for snippet injection.",
    )
    parser.add_argument(
        "--summary-output",
        default="docs/status/workflow-validation-summary.json",
        help="CI-consumable summary artifact path.",
    )
    parser.add_argument(
        "--drift-output",
        default="docs/status/workflow-drift-report.md",
        help="Markdown drift report output path.",
    )
    parser.add_argument(
        "--commands-output",
        default="docs/generated/workflow-command-reference.md",
        help="Generated markdown command reference output path.",
    )
    return parser.parse_args()


def load_manifest(path: Path) -> dict[str, Any]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    workflows = payload.get("workflows")
    if not isinstance(workflows, list) or not workflows:
        raise ValueError("Manifest must contain a non-empty 'workflows' array.")

    required_keys = {
        "id",
        "owners",
        "requiredPrerequisites",
        "executionCommands",
        "expectedArtifacts",
        "validationChecks",
        "artifactPolicy",
    }

    seen_ids: set[str] = set()
    for index, workflow in enumerate(workflows):
        if not isinstance(workflow, dict):
            raise ValueError(f"workflows[{index}] must be an object.")

        missing = [key for key in required_keys if key not in workflow]
        if missing:
            raise ValueError(f"workflow '{workflow.get('id', index)}' missing required keys: {', '.join(missing)}")

        workflow_id = workflow["id"]
        if not isinstance(workflow_id, str) or not workflow_id.strip():
            raise ValueError(f"workflows[{index}].id must be a non-empty string.")
        if workflow_id in seen_ids:
            raise ValueError(f"Duplicate workflow id '{workflow_id}'.")
        seen_ids.add(workflow_id)

        if not isinstance(workflow["executionCommands"], list) or not workflow["executionCommands"]:
            raise ValueError(f"workflow '{workflow_id}' must declare at least one execution command.")

        artifact_policy = workflow["artifactPolicy"]
        if not isinstance(artifact_policy, dict):
            raise ValueError(f"workflow '{workflow_id}'.artifactPolicy must be an object.")

        policy_required_keys = {"ownerLane", "refreshTrigger", "retention", "canonicalOutputRoots", "driftChecks"}
        policy_missing = [key for key in policy_required_keys if key not in artifact_policy]
        if policy_missing:
            raise ValueError(
                f"workflow '{workflow_id}'.artifactPolicy missing required keys: {', '.join(policy_missing)}"
            )

        retention = artifact_policy["retention"]
        if not isinstance(retention, dict):
            raise ValueError(f"workflow '{workflow_id}'.artifactPolicy.retention must be an object.")
        for retention_key in ("policy", "maxAgeDays", "retainLatest"):
            if retention_key not in retention:
                raise ValueError(
                    f"workflow '{workflow_id}'.artifactPolicy.retention missing required key: {retention_key}"
                )

        canonical_roots = artifact_policy["canonicalOutputRoots"]
        if not isinstance(canonical_roots, list) or not canonical_roots:
            raise ValueError(f"workflow '{workflow_id}'.artifactPolicy.canonicalOutputRoots must be a non-empty list.")

        drift_checks = artifact_policy["driftChecks"]
        if not isinstance(drift_checks, dict):
            raise ValueError(f"workflow '{workflow_id}'.artifactPolicy.driftChecks must be an object.")
        for drift_key in ("enforceMissing", "enforceFreshness", "enforceCanonicalPaths"):
            if drift_key not in drift_checks:
                raise ValueError(
                    f"workflow '{workflow_id}'.artifactPolicy.driftChecks missing required key: {drift_key}"
                )

    return payload


def replace_block(text: str, start_marker: str, end_marker: str, replacement: str) -> str:
    pattern = re.compile(rf"{re.escape(start_marker)}.*?{re.escape(end_marker)}", re.DOTALL)
    block = f"{start_marker}\n{replacement.rstrip()}\n{end_marker}"
    if pattern.search(text):
        return pattern.sub(block, text)
    return text.rstrip() + "\n\n" + block + "\n"


def collect_declared_commands(workflows: list[dict[str, Any]]) -> list[WorkflowRef]:
    refs: list[WorkflowRef] = []
    for workflow in workflows:
        workflow_id = str(workflow["id"])
        for command_entry in workflow["executionCommands"]:
            if isinstance(command_entry, dict):
                command = str(command_entry.get("command", "")).strip()
            else:
                command = str(command_entry).strip()
            if command:
                refs.append(WorkflowRef(workflow_id=workflow_id, command=command))
    return refs


def parse_make_targets(repo_root: Path) -> set[str]:
    makefiles = sorted((repo_root / "make").glob("*.mk"))
    makefiles.append(repo_root / "Makefile")

    targets: set[str] = set()
    target_pattern = re.compile(r"^([A-Za-z0-9_.-]+):")
    for makefile in makefiles:
        if not makefile.exists():
            continue
        for line in makefile.read_text(encoding="utf-8", errors="replace").splitlines():
            match = target_pattern.match(line)
            if not match:
                continue
            target = match.group(1)
            if target in {".PHONY", ".DEFAULT_GOAL"}:
                continue
            targets.add(target)
    return targets


def parse_script_paths(repo_root: Path) -> set[str]:
    tracked: set[str] = set()
    for base in (repo_root / "scripts", repo_root / "build" / "scripts"):
        if not base.exists():
            continue
        for path in base.rglob("*"):
            if path.suffix.lower() in {".py", ".ps1", ".sh"}:
                tracked.add(path.relative_to(repo_root).as_posix())
    return tracked


def command_make_target(command: str) -> str | None:
    tokenized = command.strip().split()
    if len(tokenized) >= 2 and tokenized[0] == "make":
        target = tokenized[1]
        if target and not target.startswith("-"):
            return target
    return None


def command_script_path(command: str) -> str | None:
    normalized = command.replace("\\", "/")
    match = re.search(r"((?:\./)?(?:scripts|build/scripts)/[^\s'\"]+\.(?:py|ps1|sh))", normalized)
    if not match:
        return None
    path = match.group(1)
    if path.startswith("./"):
        path = path[2:]
    return path


def contains_template_token(path: str) -> bool:
    return "<" in path and ">" in path


def _is_within_roots(relative_path: str, roots: list[str]) -> bool:
    normalized_path = relative_path.strip("/").replace("\\", "/")
    for root in roots:
        normalized_root = root.strip("/").replace("\\", "/")
        if normalized_path == normalized_root or normalized_path.startswith(normalized_root + "/"):
            return True
    return False


def evaluate_artifact_governance(
    repo_root: Path, workflows: list[dict[str, Any]], generated_outputs: list[str]
) -> dict[str, list[str]]:
    missing_artifacts: list[str] = []
    stale_artifacts: list[str] = []
    outside_canonical_paths: list[str] = []
    orphan_generated_outputs: list[str] = []

    expected_artifacts: set[str] = set()
    docs_automation_roots: list[str] = []
    stale_now = datetime.now(timezone.utc)

    for workflow in workflows:
        workflow_id = str(workflow["id"])
        policy = workflow["artifactPolicy"]
        roots = [str(root) for root in policy["canonicalOutputRoots"]]
        checks = policy["driftChecks"]
        max_age_days = int(policy["retention"]["maxAgeDays"])
        freshness_cutoff = stale_now - timedelta(days=max_age_days)

        for artifact in workflow["expectedArtifacts"]:
            artifact_path = str(artifact)
            expected_artifacts.add(artifact_path)
            if contains_template_token(artifact_path):
                continue

            if checks.get("enforceCanonicalPaths", False) and not _is_within_roots(artifact_path, roots):
                outside_canonical_paths.append(f"{workflow_id}:{artifact_path}")

            resolved_artifact = repo_root / artifact_path
            if checks.get("enforceMissing", False) and not resolved_artifact.exists():
                missing_artifacts.append(f"{workflow_id}:{artifact_path}")

            if (
                checks.get("enforceFreshness", False)
                and max_age_days > 0
                and resolved_artifact.exists()
            ):
                modified_at = datetime.fromtimestamp(resolved_artifact.stat().st_mtime, tz=timezone.utc)
                if modified_at < freshness_cutoff:
                    stale_artifacts.append(
                        f"{workflow_id}:{artifact_path} (last modified {modified_at.date().isoformat()})"
                    )

        if workflow_id == "docs-automation-core":
            docs_automation_roots = roots

    for generated_output in generated_outputs:
        if generated_output not in expected_artifacts:
            orphan_generated_outputs.append(generated_output)
        elif docs_automation_roots and not _is_within_roots(generated_output, docs_automation_roots):
            outside_canonical_paths.append(f"docs-automation-core:{generated_output}")

    return {
        "missingArtifacts": missing_artifacts,
        "staleArtifacts": stale_artifacts,
        "outsideCanonicalPaths": outside_canonical_paths,
        "orphanGeneratedOutputs": orphan_generated_outputs,
    }


def build_help_snippet(workflows: list[dict[str, Any]]) -> str:
    lines = [
        "### Canonical Workflow Manifest (Generated)",
        "",
        "The commands below are generated from `docs/status/workflow-manifest.json`.",
        "",
    ]
    for workflow in workflows:
        lines.append(f"#### `{workflow['id']}`")
        lines.append("")
        lines.append(f"- Owners: {', '.join(workflow['owners'])}")
        lines.append("- Commands:")
        for command_entry in workflow["executionCommands"]:
            command = command_entry["command"] if isinstance(command_entry, dict) else str(command_entry)
            lines.append(f"  - `{command}`")
        lines.append("")
    lines.append("_Generated by `python3 build/scripts/docs/generate-workflow-manifest.py`._")
    return "\n".join(lines)


def build_dev_snippet(workflows: list[dict[str, Any]]) -> str:
    lines = [
        "### Workflow Manifest Snapshot (Generated)",
        "",
        "Use this generated snapshot when validating docs automation and workflow drift.",
        "",
        "| Workflow ID | Prerequisites | Validation checks |",
        "| --- | --- | --- |",
    ]
    for workflow in workflows:
        prereq = "; ".join(workflow["requiredPrerequisites"]) or "None"
        checks = "; ".join(check.get("name", "check") for check in workflow["validationChecks"])
        lines.append(f"| `{workflow['id']}` | {prereq} | {checks} |")
    lines.append("")
    lines.append("_Generated by `python3 build/scripts/docs/generate-workflow-manifest.py`._")
    return "\n".join(lines)


def write_commands_reference(path: Path, workflows: list[dict[str, Any]]) -> None:
    lines = [
        "# Workflow Command Reference",
        "",
        "> Auto-generated by `build/scripts/docs/generate-workflow-manifest.py`.",
        "",
    ]
    for workflow in workflows:
        lines.append(f"## {workflow['id']}")
        lines.append("")
        lines.append(f"- Owners: {', '.join(workflow['owners'])}")
        lines.append(f"- Expected artifacts: {', '.join(workflow['expectedArtifacts'])}")
        lines.append(f"- Owner lane: {workflow['artifactPolicy']['ownerLane']}")
        lines.append(f"- Refresh trigger: {workflow['artifactPolicy']['refreshTrigger']}")
        lines.append(f"- Canonical output roots: {', '.join(workflow['artifactPolicy']['canonicalOutputRoots'])}")
        retention = workflow["artifactPolicy"]["retention"]
        lines.append(
            f"- Retention: {retention['policy']} (maxAgeDays={retention['maxAgeDays']}, retainLatest={retention['retainLatest']})"
        )
        lines.append("- Commands:")
        for command_entry in workflow["executionCommands"]:
            command = command_entry["command"] if isinstance(command_entry, dict) else str(command_entry)
            lines.append(f"  - `{command}`")
        lines.append("")
    path.parent.mkdir(parents=True, exist_ok=True)
    write_text_lf(path, "\n".join(lines).rstrip() + "\n")


def main() -> int:
    args = parse_args()
    repo_root = Path(__file__).resolve().parents[3]

    manifest_path = repo_root / args.manifest
    help_doc_path = repo_root / args.help_doc
    dev_doc_path = repo_root / args.dev_doc

    payload = load_manifest(manifest_path)
    workflows = payload["workflows"]

    help_doc = help_doc_path.read_text(encoding="utf-8")
    help_doc = replace_block(help_doc, HELP_BLOCK_START, HELP_BLOCK_END, build_help_snippet(workflows))
    write_text_lf(help_doc_path, help_doc)

    dev_doc = dev_doc_path.read_text(encoding="utf-8")
    dev_doc = replace_block(dev_doc, DEV_BLOCK_START, DEV_BLOCK_END, build_dev_snippet(workflows))
    write_text_lf(dev_doc_path, dev_doc)

    write_commands_reference(repo_root / args.commands_output, workflows)

    declared = collect_declared_commands(workflows)
    declared_make = sorted({target for ref in declared if (target := command_make_target(ref.command))})
    declared_scripts = sorted({script for ref in declared if (script := command_script_path(ref.command))})

    existing_make = parse_make_targets(repo_root)
    existing_scripts = parse_script_paths(repo_root)

    missing_make = [target for target in declared_make if target not in existing_make]
    missing_scripts = [script for script in declared_scripts if script not in existing_scripts]

    referenced_make = set(declared_make)
    referenced_scripts = set(declared_scripts)

    undeclared_make_candidates = sorted(target for target in existing_make if target not in referenced_make)
    undeclared_script_candidates = sorted(path for path in existing_scripts if path not in referenced_scripts)

    generated_outputs = [args.summary_output, args.drift_output, args.commands_output, args.help_doc, args.dev_doc]
    governance_violations = evaluate_artifact_governance(repo_root, workflows, generated_outputs)
    has_governance_violations = any(governance_violations.values())

    drift_status = "clean" if not missing_make and not missing_scripts and not has_governance_violations else "drift-detected"

    summary = {
        "manifest": args.manifest,
        "workflowCount": len(workflows),
        "declared": {
            "makeTargets": declared_make,
            "scriptPaths": declared_scripts,
        },
        "missing": {
            "makeTargets": missing_make,
            "scriptPaths": missing_scripts,
        },
        "governance": governance_violations,
        "status": drift_status,
    }

    summary_path = repo_root / args.summary_output
    summary_path.parent.mkdir(parents=True, exist_ok=True)
    write_text_lf(summary_path, json.dumps(summary, indent=2) + "\n")

    drift_lines = [
        "# Workflow Drift Report",
        "",
        "> Auto-generated by `build/scripts/docs/generate-workflow-manifest.py`.",
        "",
        f"- Manifest path: `{args.manifest}`",
        f"- Workflow count: `{len(workflows)}`",
        f"- Status: `{drift_status}`",
        "",
        "## Missing declared commands",
        "",
        f"- Missing Make targets ({len(missing_make)}): " + (", ".join(f"`{t}`" for t in missing_make) if missing_make else "None"),
        f"- Missing scripts ({len(missing_scripts)}): " + (", ".join(f"`{s}`" for s in missing_scripts) if missing_scripts else "None"),
        "",
        "## Artifact governance violations",
        "",
        f"- Missing expected artifacts ({len(governance_violations['missingArtifacts'])}): "
        + (", ".join(f"`{entry}`" for entry in governance_violations["missingArtifacts"]) if governance_violations["missingArtifacts"] else "None"),
        f"- Stale artifacts ({len(governance_violations['staleArtifacts'])}): "
        + (", ".join(f"`{entry}`" for entry in governance_violations["staleArtifacts"]) if governance_violations["staleArtifacts"] else "None"),
        f"- Outside canonical paths ({len(governance_violations['outsideCanonicalPaths'])}): "
        + (
            ", ".join(f"`{entry}`" for entry in governance_violations["outsideCanonicalPaths"])
            if governance_violations["outsideCanonicalPaths"]
            else "None"
        ),
        f"- Orphan generated outputs ({len(governance_violations['orphanGeneratedOutputs'])}): "
        + (
            ", ".join(f"`{entry}`" for entry in governance_violations["orphanGeneratedOutputs"])
            if governance_violations["orphanGeneratedOutputs"]
            else "None"
        ),
        "",
        "## Undeclared actual inventory (top 25)",
        "",
        "### Make targets",
    ]

    preview_make = undeclared_make_candidates[:25]
    if preview_make:
        drift_lines.extend([f"- `{target}`" for target in preview_make])
    else:
        drift_lines.append("- None")

    drift_lines.extend(["", "### Scripts"])
    preview_scripts = undeclared_script_candidates[:25]
    if preview_scripts:
        drift_lines.extend([f"- `{path}`" for path in preview_scripts])
    else:
        drift_lines.append("- None")

    drift_path = repo_root / args.drift_output
    drift_path.parent.mkdir(parents=True, exist_ok=True)
    write_text_lf(drift_path, "\n".join(drift_lines).rstrip() + "\n")

    print(f"Generated workflow artifacts for {len(workflows)} workflows.")
    print(f"Status: {drift_status}")
    return 1 if drift_status != "clean" else 0


if __name__ == "__main__":
    raise SystemExit(main())
