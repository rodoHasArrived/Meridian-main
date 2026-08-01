#!/usr/bin/env python3
"""Fail-closed validation of the deployed monitoring artifacts.

`validate-observability-contract.py` checks that alert expressions name series the exporter
emits and that runbook links resolve. That is a static, repository-internal check. It cannot
tell whether a rule *fires* on the condition it claims, nor whether the compose files a
deployment actually runs are well formed. Every monitoring regression this repository has hit
lived in exactly that gap:

- a `rate()` window over a counter advanced once at startup, so a P1 integrity alert cleared
  itself minutes after recovery while the corruption was still unreconciled;
- an equality test against a circuit-breaker state that went silent during half-open recovery;
- a `for:` paired with a mismatched range so the alert fired at roughly fifteen minutes rather
  than the ten its SLO declared;
- thresholds on gauges with no writer, which fire permanently on a healthy instance;
- a `${VAR:?}` in the base compose file, which broke `docker compose up` for every profile
  because Compose interpolates before it filters by profile.

This gate closes it by running the real tools:

1. ``promtool check rules``  — the rule files parse and every expression is valid PromQL.
2. ``promtool test rules``   — the unit tests in ``alert-rules.test.yml`` pass, so each rule
   fires on its stated condition and stays silent otherwise.
3. ``docker compose config`` — the base stack renders with no credentials in the environment,
   and the monitoring overlay *fails* without them, so a default admin password cannot return.

Both tools are required. A missing tool is a failure, not a skip: "we could not check" reports
the same green as "we checked and it is fine", which is the class of defect this gate exists to
remove. Pass ``--allow-missing-tools`` for a local run that only wants the checks it can do.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]
MONITORING_DIR = REPO_ROOT / "deploy" / "monitoring"
COMPOSE_DIR = REPO_ROOT / "deploy" / "docker"
RULES_FILE = MONITORING_DIR / "alert-rules.yml"
RULES_TEST_FILE = MONITORING_DIR / "alert-rules.test.yml"
BASE_COMPOSE = COMPOSE_DIR / "docker-compose.yml"
MONITORING_COMPOSE = COMPOSE_DIR / "docker-compose.monitoring.yml"

# Credentials the monitoring overlay must demand rather than default.
REQUIRED_MONITORING_VARS = ("GF_SECURITY_ADMIN_USER", "GF_SECURITY_ADMIN_PASSWORD")


class Finding(Exception):
    """A validation failure worth failing the build over."""


def run(command: list[str], cwd: Path, env: dict[str, str] | None = None) -> subprocess.CompletedProcess:
    return subprocess.run(
        command,
        cwd=cwd,
        env=env,
        capture_output=True,
        text=True,
        check=False,
    )


def resolve_promtool() -> str | None:
    override = os.environ.get("PROMTOOL")
    if override and Path(override).is_file():
        return override
    return shutil.which("promtool")


def resolve_compose() -> list[str] | None:
    if shutil.which("docker"):
        probe = run(["docker", "compose", "version"], cwd=REPO_ROOT)
        if probe.returncode == 0:
            return ["docker", "compose"]
    if shutil.which("docker-compose"):
        return ["docker-compose"]
    return None


def check_rules(promtool: str, findings: list[str]) -> None:
    if not RULES_FILE.is_file():
        findings.append(f"alert rules not found: {RULES_FILE.relative_to(REPO_ROOT)}")
        return

    result = run([promtool, "check", "rules", RULES_FILE.name], cwd=MONITORING_DIR)
    if result.returncode != 0:
        findings.append(
            "promtool check rules failed:\n"
            + (result.stdout or "").strip()
            + (result.stderr or "").strip()
        )


def test_rules(promtool: str, findings: list[str]) -> None:
    if not RULES_TEST_FILE.is_file():
        # A rule file with no unit tests is the state this gate exists to prevent: it parses,
        # and nothing establishes that it fires.
        findings.append(
            f"alert rule unit tests not found: {RULES_TEST_FILE.relative_to(REPO_ROOT)}. "
            "Parsing is not firing; every rule needs a promtool test."
        )
        return

    result = run([promtool, "test", "rules", RULES_TEST_FILE.name], cwd=MONITORING_DIR)
    if result.returncode != 0:
        findings.append(
            "promtool test rules failed:\n"
            + (result.stdout or "").strip()
            + (result.stderr or "").strip()
        )


def check_compose(compose: list[str], findings: list[str]) -> None:
    if not BASE_COMPOSE.is_file():
        findings.append(f"compose file not found: {BASE_COMPOSE.relative_to(REPO_ROOT)}")
        return

    stripped = {k: v for k, v in os.environ.items() if k not in REQUIRED_MONITORING_VARS}

    # The base stack must render with no monitoring credentials present. A required-value
    # expansion here would break `docker compose up` for every profile, because Compose
    # interpolates the merged file before it filters services by profile.
    base = run([*compose, "-f", BASE_COMPOSE.name, "config"], cwd=COMPOSE_DIR, env=stripped)
    if base.returncode != 0:
        findings.append(
            "the base compose file does not render without monitoring credentials, so "
            "`docker compose up` is broken for every profile:\n" + (base.stderr or "").strip()
        )

    if not MONITORING_COMPOSE.is_file():
        findings.append(f"compose file not found: {MONITORING_COMPOSE.relative_to(REPO_ROOT)}")
        return

    overlay_args = [*compose, "-f", BASE_COMPOSE.name, "-f", MONITORING_COMPOSE.name, "config"]

    # The overlay must refuse to render without credentials. If it renders, something supplies
    # a default, and a default Grafana admin password is a published credential.
    without = run(overlay_args, cwd=COMPOSE_DIR, env=stripped)
    if without.returncode == 0:
        findings.append(
            "the monitoring overlay renders without "
            + " or ".join(REQUIRED_MONITORING_VARS)
            + " set, which means a default admin credential exists. Require the value "
            "(${VAR:?message}) rather than defaulting it (${VAR:-admin})."
        )

    supplied = dict(stripped)
    supplied.update({name: "validation-only" for name in REQUIRED_MONITORING_VARS})
    with_creds = run(overlay_args, cwd=COMPOSE_DIR, env=supplied)
    if with_creds.returncode != 0:
        findings.append(
            "the monitoring overlay does not render even with credentials supplied:\n"
            + (with_creds.stderr or "").strip()
        )


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument(
        "--allow-missing-tools",
        action="store_true",
        help="Skip a check whose tool is unavailable instead of failing. For local runs only; "
        "CI must not pass this, because an unrun check reports the same green as a passed one.",
    )
    parser.add_argument("--summary", action="store_true", help="Print a one-line result")
    args = parser.parse_args()

    findings: list[str] = []
    ran: list[str] = []
    skipped: list[str] = []

    promtool = resolve_promtool()
    if promtool:
        check_rules(promtool, findings)
        test_rules(promtool, findings)
        ran.extend(["promtool check rules", "promtool test rules"])
    elif args.allow_missing_tools:
        skipped.append("promtool (not installed)")
    else:
        findings.append(
            "promtool not found. Install it from a Prometheus release or set PROMTOOL to its "
            "path. Alert rules that nothing validates are the defect this gate exists to catch."
        )

    compose = resolve_compose()
    if compose:
        check_compose(compose, findings)
        ran.append("docker compose config")
    elif args.allow_missing_tools:
        skipped.append("docker compose (not installed)")
    else:
        findings.append(
            "docker compose not found. `docker compose config` needs the CLI but not a running "
            "daemon, so it is available anywhere the CLI is installed."
        )

    for finding in findings:
        print(f"::error::{finding}", file=sys.stderr)

    if skipped:
        print(f"monitoring-deployment: SKIPPED {', '.join(skipped)}", file=sys.stderr)

    if findings:
        print(f"monitoring-deployment: {len(findings)} problem(s)")
        return 1

    if args.summary:
        print(f"monitoring-deployment: {len(ran)} check(s) passed ({', '.join(ran)})")
        if not ran:
            print("No checks ran. This is not a pass; install promtool and the docker CLI.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
