#!/usr/bin/env python3
"""Dispatch Meridian's curated Targeted Test workflow safely."""

from __future__ import annotations

import argparse
import json
import re
import shlex
import subprocess
import sys
import time
from dataclasses import dataclass
from typing import Sequence


WORKFLOW_FILE = "targeted-test.yml"
WINDOWS_MODES = {"wpf-dev-loop", "wpf-route", "desktop-smoke"}
UBUNTU_MODES = {"browser-workstation", "docs-source"}
ALL_MODES = {"dotnet-filtered", *WINDOWS_MODES, *UBUNTU_MODES}
BROAD_DOTNET_FILTERS = {
    "*",
    "Category!=Integration",
    "Category!=Performance",
    "Category!=Integration&Category!=Performance",
}
FILTER_ALLOWED_RE = re.compile(r"^[A-Za-z0-9_ .,:&|!<>=~()'\"+\-\[\]\\/]+$")
DOTNET_PROJECT_RE = re.compile(r"^tests/[A-Za-z0-9._/-]+\.(csproj|fsproj)$")


@dataclass(frozen=True)
class DispatchResult:
    command: list[str]
    workflow_url: str | None
    conclusion: str | None


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Dispatch the Meridian Targeted Test workflow.")
    parser.add_argument("--ref", required=True, help="Branch, tag, or SHA to dispatch.")
    parser.add_argument("--mode", required=True, choices=sorted(ALL_MODES), help="Curated hosted validation mode.")
    parser.add_argument("--runner", default=None, choices=("ubuntu-latest", "windows-latest"))
    parser.add_argument("--configuration", default="Release")
    parser.add_argument("--dotnet-project", default="tests/Meridian.Tests/Meridian.Tests.csproj")
    parser.add_argument("--dotnet-filter", default="")
    parser.add_argument("--enable-windows-targeting", default="true", choices=("true", "false"))
    parser.add_argument("--enable-full-wpf-build", default="false", choices=("true", "false"))
    parser.add_argument("--wpf-route", default="position-blotter", choices=("position-blotter", "operator-inbox"))
    parser.add_argument("--wait", action="store_true", help="Wait for the dispatched run and return its conclusion.")
    parser.add_argument("--dry-run", action="store_true", help="Print the gh command without dispatching.")
    return parser.parse_args(argv)


def default_runner_for_mode(mode: str) -> str:
    if mode in WINDOWS_MODES:
        return "windows-latest"
    return "ubuntu-latest"


def normalize_bool(value: str) -> str:
    return "true" if value.lower() == "true" else "false"


def validate_args(args: argparse.Namespace) -> list[str]:
    errors: list[str] = []
    runner = args.runner or default_runner_for_mode(args.mode)

    if args.mode in WINDOWS_MODES and runner != "windows-latest":
        errors.append(f"Mode '{args.mode}' requires runner=windows-latest.")
    if args.mode in UBUNTU_MODES and runner != "ubuntu-latest":
        errors.append(f"Mode '{args.mode}' requires runner=ubuntu-latest.")

    if not re.fullmatch(r"[A-Za-z0-9._/\-]+", args.ref):
        errors.append("--ref must be a branch, tag, or SHA without shell metacharacters.")
    if not re.fullmatch(r"[A-Za-z0-9._-]+", args.configuration):
        errors.append("--configuration must contain only letters, numbers, dot, underscore, or dash.")

    project = args.dotnet_project.replace("\\", "/").strip()
    if args.mode == "dotnet-filtered":
        filter_text = args.dotnet_filter.strip()
        if not project or not DOTNET_PROJECT_RE.fullmatch(project) or ".." in project:
            errors.append("--dotnet-project must be a repo-relative .NET test project under tests/.")
        if not filter_text:
            errors.append("--dotnet-filter is required for mode=dotnet-filtered.")
        elif not FILTER_ALLOWED_RE.fullmatch(filter_text):
            errors.append("--dotnet-filter contains unsupported characters.")
        else:
            normalized_filter = re.sub(r"\s+", "", filter_text)
            if normalized_filter in BROAD_DOTNET_FILTERS:
                errors.append("--dotnet-filter is too broad for Targeted Test.")
            if not re.search(r"(?<![!<>])(?:=|~)", normalized_filter):
                errors.append("--dotnet-filter must include a positive class, method, trait, or fully qualified name selector.")

    return errors


def build_workflow_command(args: argparse.Namespace) -> list[str]:
    runner = args.runner or default_runner_for_mode(args.mode)
    fields = {
        "mode": args.mode,
        "runner": runner,
        "configuration": args.configuration,
        "enable_windows_targeting": normalize_bool(args.enable_windows_targeting),
        "enable_full_wpf_build": normalize_bool(args.enable_full_wpf_build),
        "wpf_route": args.wpf_route,
    }
    if args.mode == "dotnet-filtered":
        fields["dotnet_project"] = args.dotnet_project.replace("\\", "/").strip()
        fields["dotnet_filter"] = args.dotnet_filter.strip()
    elif args.mode == "wpf-dev-loop" and args.dotnet_filter.strip():
        fields["dotnet_filter"] = args.dotnet_filter.strip()

    command = ["gh", "workflow", "run", WORKFLOW_FILE, "--ref", args.ref]
    for key, value in fields.items():
        command.extend(["-f", f"{key}={value}"])
    return command


def shell_join(command: Sequence[str]) -> str:
    return " ".join(shlex.quote(part) for part in command)


def run_command(command: Sequence[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(command, check=True, text=True, capture_output=True)


def latest_run_for_ref(ref: str) -> dict[str, object] | None:
    list_command = [
        "gh",
        "run",
        "list",
        "--workflow",
        WORKFLOW_FILE,
        "--branch",
        ref,
        "--limit",
        "1",
        "--json",
        "databaseId,url,status,conclusion,name,displayTitle",
    ]
    payload = run_command(list_command).stdout
    runs = json.loads(payload) if payload.strip() else []
    return runs[0] if runs else None


def wait_for_run(ref: str) -> tuple[str | None, str | None]:
    run = None
    for _ in range(12):
        run = latest_run_for_ref(ref)
        if run is not None:
            break
        time.sleep(5)
    if run is None:
        return None, None

    run_id = str(run["databaseId"])
    run_command(["gh", "run", "watch", run_id, "--exit-status"])
    refreshed = run_command(["gh", "run", "view", run_id, "--json", "url,conclusion"]).stdout
    payload = json.loads(refreshed)
    return str(payload.get("url") or run.get("url") or ""), str(payload.get("conclusion") or "")


def dispatch(args: argparse.Namespace) -> DispatchResult:
    command = build_workflow_command(args)
    if args.dry_run:
        return DispatchResult(command=command, workflow_url=None, conclusion=None)

    run_command(command)
    if not args.wait:
        return DispatchResult(command=command, workflow_url=None, conclusion=None)

    workflow_url, conclusion = wait_for_run(args.ref)
    return DispatchResult(command=command, workflow_url=workflow_url, conclusion=conclusion)


def main(argv: Sequence[str] | None = None) -> int:
    args = parse_args(argv)
    errors = validate_args(args)
    if errors:
        for error in errors:
            print(f"error: {error}", file=sys.stderr)
        return 2

    result = dispatch(args)
    print("Targeted Test dispatch")
    print(f"- Command: {shell_join(result.command)}")
    print(f"- Mode: {args.mode}")
    print(f"- Runner: {args.runner or default_runner_for_mode(args.mode)}")
    if result.workflow_url:
        print(f"- Workflow URL: {result.workflow_url}")
    if result.conclusion:
        print(f"- Conclusion: {result.conclusion}")
    return 0 if result.conclusion in (None, "", "success") else 1


if __name__ == "__main__":
    raise SystemExit(main())
