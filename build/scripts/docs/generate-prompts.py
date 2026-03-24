#!/usr/bin/env python3
"""Generate AI prompts from GitHub Actions workflow run failures.

This utility is intentionally dependency-free so it can run in CI runners.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable


@dataclass(frozen=True)
class PromptSpec:
    filename: str
    name: str
    description: str
    system_intro: str


FAILURE_CATEGORIES: dict[str, PromptSpec] = {
    "build": PromptSpec(
        filename="fix-build-errors.prompt.yml",
        name="{name}",
        description="Fix build errors from CI analysis",
        system_intro="You are a senior .NET build engineer fixing CI build failures.",
    ),
    "test": PromptSpec(
        filename="fix-test-failures.prompt.yml",
        name="{name}",
        description="Fix test failures from CI analysis",
        system_intro="You are a senior .NET test engineer diagnosing and fixing failing tests.",
    ),
    "code-quality": PromptSpec(
        filename="fix-code-quality.prompt.yml",
        name="Address Code Quality Issues From Ci Analysis",
        description="Address code quality issues from CI analysis",
        system_intro="You are a senior .NET developer addressing code quality issues reported by CI analyzers.",
    ),
}


def run_command(args: list[str]) -> str:
    completed = subprocess.run(args, check=False, capture_output=True, text=True)
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or completed.stdout.strip() or f"command failed: {' '.join(args)}")
    return completed.stdout


def fetch_run_log(repo: str, run_id: str) -> str:
    if run_id in {"", "0"}:
        return ""
    try:
        return run_command(["gh", "run", "view", run_id, "--repo", repo, "--log"])
    except Exception:
        return ""


def detect_categories(workflow: str, log_text: str) -> set[str]:
    lower_workflow = workflow.lower()
    categories: set[str] = set()

    if "test" in lower_workflow:
        categories.add("test")
    if "quality" in lower_workflow or "lint" in lower_workflow or "analysis" in lower_workflow:
        categories.add("code-quality")
    if "build" in lower_workflow or "compile" in lower_workflow:
        categories.add("build")

    lower_log = log_text.lower()
    if any(token in lower_log for token in ["error cs", "netsdk", "build failed", "msbuild"]):
        categories.add("build")
    if any(token in lower_log for token in ["failed!", "test run", "assert", "xunit", "nunit"]):
        categories.add("test")
    if any(token in lower_log for token in ["warning ca", "warning sa", "warning ide", "analyzer", "stylecop"]):
        categories.add("code-quality")

    return categories


def extract_findings(log_text: str, limit: int = 8) -> list[str]:
    lines = []
    for raw in log_text.splitlines():
        line = raw.strip()
        if not line:
            continue
        if re.search(r"\b(error|warning|failed|exception)\b", line, flags=re.IGNORECASE):
            lines.append(line)

    # preserve order, de-duplicate
    unique: list[str] = []
    seen: set[str] = set()
    for line in lines:
        if line not in seen:
            seen.add(line)
            unique.append(line)
        if len(unique) >= limit:
            break
    return unique


def workflow_stub_name(workflow: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "-", workflow.lower()).strip("-")
    return slug or "workflow"


def yaml_list(lines: Iterable[str]) -> str:
    rendered = []
    for line in lines:
        escaped = line.replace("`", "'")
        rendered.append(f"      - `{escaped}`")
    return "\n".join(rendered)


def build_workflow_prompt(workflow: str, run_url: str, findings: list[str], now: str, conclusion: str) -> str:
    stub = workflow_stub_name(workflow)
    findings_block = yaml_list(findings) if findings else "      No specific patterns detected."
    return f"""name: Workflow Results - {stub}
description: Address issues found in the {stub} workflow run
# Auto-generated prompt from workflow run analysis
# Generated: {now}
# Source: {run_url}
# Run conclusion: {conclusion}
# Model-agnostic prompt - works with any capable LLM
messages:
  - role: system
    content: |
      You are a senior .NET developer analyzing CI/CD workflow results for the Meridian project.
      The `{stub}` workflow has completed with status: **{conclusion}**.

      ## Findings from Workflow Run
{findings_block}

      ## Resolution Guidelines
      1. Prioritize failures by severity (errors > warnings)
      2. Fix root causes, not symptoms
      3. Follow project conventions and coding standards
      4. Run verification commands before considering resolved

  - role: user
    content: |
      The `{stub}` CI workflow needs attention. Analyze the results and help fix the issues.

      Workflow run: {run_url}

      Additional context:
      {{{{additional_context}}}}

      Please provide:
      1. Summary of all issues found
      2. Prioritized fix plan
      3. Code changes needed (with file paths)
      4. Verification steps
"""


def build_category_prompt(spec: PromptSpec, workflow: str, run_url: str, findings: list[str], now: str) -> str:
    findings_block = yaml_list(findings) if findings else "      - `No specific error patterns extracted from logs.`"
    return f"""name: {spec.name}
description: {spec.description}
# Auto-generated prompt from workflow results - {workflow}
# Generated: {now}
# Source: {run_url}
# Model-agnostic prompt - works with any capable LLM
messages:
  - role: system
    content: |
      {spec.system_intro}

      ## Failure Patterns
{findings_block}

      ## Resolution Approach
      1. Identify and fix the root cause
      2. Keep changes scoped and production-safe
      3. Add or update tests when needed
      4. Verify the fix in CI-equivalent commands

  - role: user
    content: |
      The CI workflow `{workflow}` has reported failures that need attention.

      Error details:
      ```
      {{{{error_details}}}}
      ```

      Affected files:
      {{{{affected_files}}}}

      Please provide:
      1. Root cause analysis of each failure
      2. Specific code fixes with file paths and line numbers
      3. Verification steps to confirm the fix
      4. Preventive measures to avoid recurrence
"""


def write_file(path: Path, content: str, dry_run: bool) -> bool:
    existing = path.read_text(encoding="utf-8") if path.exists() else None
    changed = existing != content
    if changed and not dry_run:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
    return changed


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate prompts from workflow run results")
    parser.add_argument("--workflow", required=True)
    parser.add_argument("--run-id", default="0")
    parser.add_argument("--output", required=True)
    parser.add_argument("--json-output", required=True)
    parser.add_argument("--summary", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    repo = os.environ.get("GITHUB_REPOSITORY", Path.cwd().name)
    run_url = f"https://github.com/{repo}/actions/runs/{args.run_id}" if args.run_id != "0" else "https://github.com"

    log_text = fetch_run_log(repo, args.run_id)
    findings = extract_findings(log_text)
    categories = detect_categories(args.workflow, log_text)
    now = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    workflow_slug = workflow_stub_name(args.workflow.removesuffix(".yml"))

    output_dir = Path(args.output)
    changed_files: list[str] = []

    workflow_prompt_name = f"workflow-results-{workflow_slug}.prompt.yml"
    workflow_prompt_path = output_dir / workflow_prompt_name
    workflow_prompt = build_workflow_prompt(args.workflow, run_url, findings, now, "failure")
    if write_file(workflow_prompt_path, workflow_prompt, args.dry_run):
        changed_files.append(str(workflow_prompt_path))

    for category in sorted(categories):
        spec = FAILURE_CATEGORIES.get(category)
        if not spec:
            continue
        path = output_dir / spec.filename
        content = build_category_prompt(spec, args.workflow, run_url, findings, now)
        if write_file(path, content, args.dry_run):
            changed_files.append(str(path))

    results = {
        "workflow": args.workflow,
        "run_id": args.run_id,
        "total_generated": len(changed_files),
        "categories": sorted(categories),
        "findings_count": len(findings),
        "changed_files": changed_files,
        "dry_run": args.dry_run,
    }

    json_path = Path(args.json_output)
    json_path.write_text(json.dumps(results, indent=2), encoding="utf-8")

    if args.summary:
        print("Prompt generation summary")
        print(f"- workflow: {args.workflow}")
        print(f"- run id: {args.run_id}")
        print(f"- categories: {', '.join(sorted(categories)) if categories else 'none'}")
        print(f"- findings extracted: {len(findings)}")
        print(f"- prompts generated: {len(changed_files)}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
