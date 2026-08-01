#!/usr/bin/env python3
"""Fail-closed gate binding alerts, dashboards, and SLO definitions to real signals.

Meridian's operator observability surface is split across four artifacts that nothing
mechanically reconciled:

- the Prometheus exporter in `src/**/*.cs` (the only place metric names actually exist),
- `deploy/monitoring/alert-rules.yml`,
- the provisioned Grafana dashboards under `deploy/monitoring/grafana/`,
- the runtime SLO registry in `src/Meridian.Platform/Monitoring/Core/SloDefinitionRegistry.cs`.

An alert whose `expr` names a metric the exporter never emits is not a quiet alert: it is a
permanently absent series, so the alert can never fire and the subsystem it claims to cover
is unmonitored. The same is true of an SLO whose `MetricName` does not resolve, and of a
`runbook_url` whose anchor does not exist in the target document — the operator following
the link during an incident lands on nothing.

This gate makes each of those a build failure:

- every `mdc_*` metric referenced by an alert, dashboard, or SLO definition must be emitted
  by the exporter (histogram/summary `_bucket`/`_sum`/`_count` suffixes resolve to their
  base series; non-`mdc_` identifiers such as Prometheus' own `up` are out of scope);
- every runbook and SLO-document link must target an active document — not an
  archive-migration stub — and its `#anchor` must resolve to a heading in that document;
- every SLO must name an alert rule that exists, and every alert must carry a runbook link;
- provisioned monitoring stacks must not ship hardcoded administrator credentials.

Run with `--summary` for a compact one-line-per-check report.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[3]

# The exporter names every Prometheus series with this prefix. Restricting both the emitted
# and the referenced sets to it keeps Prometheus built-ins (`up`, `scrape_duration_seconds`)
# and OpenTelemetry instrument names (dotted, e.g. `meridian.pipeline.published`) out of scope.
METRIC_PREFIX = "mdc_"
METRIC_TOKEN = re.compile(r"\b(mdc_[a-z0-9_]+)\b")

# Matches `Metrics.CreateCounter("mdc_x", ...)`, `Prometheus.Metrics.CreateGauge("mdc_x", ...)`,
# and the generic OpenTelemetry form so the latter can be recognised and skipped.
METRIC_FACTORY = re.compile(
    r"Create(?P<kind>Counter|Gauge|Histogram|Summary)\s*(?:<[^>]*>\s*)?\(\s*\"(?P<name>[a-zA-Z_][a-zA-Z0-9_.]*)\"",
)

SLO_FIELD = re.compile(r"(?P<field>MetricName|AlertRuleName|RunbookSection|SloDocSection)\s*=\s*\"(?P<value>[^\"]*)\"")
SLO_ID = re.compile(r"\bId\s*=\s*\"(?P<value>SLO-[A-Z]+-\d+)\"")

RUNBOOK_ENTRY_NAME = re.compile(r"\bAlertName\s*=\s*\"(?P<value>[^\"]*)\"")
RUNBOOK_ENTRY_FIELD = re.compile(r"(?P<field>RunbookUrl|SloId)\s*=\s*\"(?P<value>[^\"]*)\"")

ALERT_NAME = re.compile(r"^\s*-\s*alert:\s*(?P<name>\S+)\s*$")
ALERT_EXPR = re.compile(r"^\s*expr:\s*(?P<expr>.+?)\s*$")
ALERT_RUNBOOK = re.compile(r"^\s*runbook_url:\s*\"?(?P<link>[^\"]+?)\"?\s*$")

MARKDOWN_HEADING = re.compile(r"^#{1,6}\s+(?P<text>.+?)\s*#*\s*$")
STUB_MARKER = re.compile(r"^\*\*Status:\*\*\s*archive-migration-stub\s*$", re.MULTILINE)

# Grafana admin credentials must come from the deploying environment. A literal value here
# ships a known password with the repository.
COMPOSE_SECRET_KEYS = ("GF_SECURITY_ADMIN_PASSWORD", "GF_SECURITY_ADMIN_USER")
COMPOSE_SECRET_ASSIGNMENT = re.compile(
    r"(?P<key>" + "|".join(COMPOSE_SECRET_KEYS) + r")\s*[=:]\s*(?P<value>\S*)",
)

# Suffixes Prometheus derives from a base series rather than registering separately.
DERIVED_SUFFIXES = {
    "Histogram": ("_bucket", "_sum", "_count"),
    "Summary": ("_sum", "_count"),
}


class Finding:
    """A single gate violation, rendered as one actionable line."""

    def __init__(self, check: str, location: str, message: str) -> None:
        self.check = check
        self.location = location
        self.message = message

    def render(self) -> str:
        return f"[{self.check}] {self.location}: {self.message}"


def slugify_heading(text: str) -> str:
    """Return the GitHub-style anchor slug for a Markdown heading."""
    lowered = text.strip().lower()
    # Drop inline code fences and link syntax before slugging, matching GitHub behaviour.
    lowered = re.sub(r"`([^`]*)`", r"\1", lowered)
    lowered = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", lowered)
    lowered = re.sub(r"[^\w\s-]", "", lowered)
    # GitHub replaces whitespace runs with a single hyphen and keeps underscores, so a
    # heading naming a metric (`mdc_drop_rate_percent`) anchors with its underscores intact.
    return re.sub(r"\s+", "-", lowered).strip("-")


def iter_source_files(root: Path) -> list[Path]:
    return sorted(p for p in (root / "src").rglob("*.cs") if "/obj/" not in p.as_posix() and "/bin/" not in p.as_posix())


def collect_emitted_metrics(root: Path) -> dict[str, str]:
    """Return every Prometheus metric the exporter registers, mapped to its instrument kind."""
    emitted: dict[str, str] = {}
    for path in iter_source_files(root):
        try:
            text = path.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            continue
        if "Create" not in text:
            continue
        for match in METRIC_FACTORY.finditer(text):
            name = match.group("name")
            if not name.startswith(METRIC_PREFIX):
                continue
            emitted[name] = match.group("kind")
    return emitted


def resolve_metric(name: str, emitted: dict[str, str]) -> bool:
    """Return True when *name* is emitted directly or derived from an emitted series."""
    if name in emitted:
        return True
    for kind, suffixes in DERIVED_SUFFIXES.items():
        for suffix in suffixes:
            if name.endswith(suffix) and emitted.get(name[: -len(suffix)]) == kind:
                return True
    return False


def metric_tokens(expression: str) -> list[str]:
    return sorted(set(METRIC_TOKEN.findall(expression)))


class AlertRule:
    def __init__(self, name: str, line: int) -> None:
        self.name = name
        self.line = line
        self.expressions: list[tuple[int, str]] = []
        self.runbook_links: list[tuple[int, str]] = []


def parse_alert_rules(path: Path) -> list[AlertRule]:
    """Parse alert names, expressions, and runbook links without requiring a YAML dependency."""
    rules: list[AlertRule] = []
    current: AlertRule | None = None
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        stripped = line.strip()
        if stripped.startswith("#"):
            continue
        name_match = ALERT_NAME.match(line)
        if name_match:
            current = AlertRule(name_match.group("name"), number)
            rules.append(current)
            continue
        if current is None:
            continue
        expr_match = ALERT_EXPR.match(line)
        if expr_match:
            current.expressions.append((number, expr_match.group("expr")))
            continue
        runbook_match = ALERT_RUNBOOK.match(line)
        if runbook_match:
            current.runbook_links.append((number, runbook_match.group("link")))
    return rules


def parse_dashboard_expressions(path: Path) -> list[str]:
    """Return every PromQL expression in a Grafana dashboard definition."""
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return []

    expressions: list[str] = []

    def walk(node: object) -> None:
        if isinstance(node, dict):
            for key, value in node.items():
                if key == "expr" and isinstance(value, str):
                    expressions.append(value)
                else:
                    walk(value)
        elif isinstance(node, list):
            for item in node:
                walk(item)

    walk(payload)
    return expressions


class SloDefinition:
    def __init__(self, slo_id: str, line: int) -> None:
        self.id = slo_id
        self.line = line
        self.fields: dict[str, tuple[int, str]] = {}


def parse_slo_registry(path: Path) -> list[SloDefinition]:
    """Parse the SLO registry's object initialisers into id/field records."""
    definitions: list[SloDefinition] = []
    current: SloDefinition | None = None
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        id_match = SLO_ID.search(line)
        if id_match:
            current = SloDefinition(id_match.group("value"), number)
            definitions.append(current)
            continue
        if current is None:
            continue
        field_match = SLO_FIELD.search(line)
        if field_match:
            current.fields[field_match.group("field")] = (number, field_match.group("value"))
    return definitions


class DocumentIndex:
    """Lazily-loaded map of document path to its heading anchors and stub status."""

    def __init__(self, root: Path) -> None:
        self._root = root
        self._cache: dict[str, tuple[bool, bool, set[str]]] = {}

    def lookup(self, relative_path: str) -> tuple[bool, bool, set[str]]:
        """Return (exists, is_stub, anchors) for a repo-relative document path."""
        if relative_path in self._cache:
            return self._cache[relative_path]

        path = self._root / relative_path
        if not path.is_file():
            result = (False, False, set())
        else:
            text = path.read_text(encoding="utf-8")
            anchors = {slugify_heading(m.group("text")) for m in (MARKDOWN_HEADING.match(line) for line in text.splitlines()) if m}
            result = (True, bool(STUB_MARKER.search(text)), anchors)

        self._cache[relative_path] = result
        return result


def check_document_link(
    findings: list[Finding],
    check: str,
    location: str,
    link: str,
    documents: DocumentIndex,
) -> None:
    """Record a finding unless *link* resolves to an anchor in an active document."""
    target, _, anchor = link.partition("#")
    target = target.strip()
    if not target:
        findings.append(Finding(check, location, f"link '{link}' names no document"))
        return

    exists, is_stub, anchors = documents.lookup(target)
    if not exists:
        findings.append(Finding(check, location, f"link target '{target}' does not exist"))
        return
    if is_stub:
        findings.append(
            Finding(
                check,
                location,
                f"link target '{target}' is an archive-migration stub; point at the active operator document",
            )
        )
        return
    if not anchor:
        findings.append(Finding(check, location, f"link '{link}' has no '#anchor'; operators need the exact section"))
        return
    if anchor not in anchors:
        findings.append(Finding(check, location, f"anchor '#{anchor}' does not resolve to a heading in '{target}'"))


def check_alerts(
    root: Path,
    emitted: dict[str, str],
    documents: DocumentIndex,
) -> tuple[list[Finding], set[str]]:
    findings: list[Finding] = []
    path = root / "deploy" / "monitoring" / "alert-rules.yml"
    if not path.is_file():
        return [Finding("alerts", path.as_posix(), "alert rule file is missing")], set()

    rules = parse_alert_rules(path)
    if not rules:
        return [Finding("alerts", path.as_posix(), "no alert rules found")], set()

    relative = path.relative_to(root).as_posix()
    for rule in rules:
        if not rule.expressions:
            findings.append(Finding("alerts", f"{relative}:{rule.line}", f"alert '{rule.name}' has no expr"))
        for line_number, expression in rule.expressions:
            for metric in metric_tokens(expression):
                if not resolve_metric(metric, emitted):
                    findings.append(
                        Finding(
                            "alerts",
                            f"{relative}:{line_number}",
                            f"alert '{rule.name}' uses metric '{metric}', which the exporter never emits",
                        )
                    )
        if not rule.runbook_links:
            findings.append(
                Finding("alerts", f"{relative}:{rule.line}", f"alert '{rule.name}' has no runbook_url annotation")
            )
        for line_number, link in rule.runbook_links:
            check_document_link(findings, "alerts", f"{relative}:{line_number}", link, documents)

    return findings, {rule.name for rule in rules}


def check_dashboards(root: Path, emitted: dict[str, str]) -> list[Finding]:
    findings: list[Finding] = []
    dashboard_root = root / "deploy" / "monitoring" / "grafana"
    if not dashboard_root.is_dir():
        return findings

    for path in sorted(dashboard_root.rglob("*.json")):
        relative = path.relative_to(root).as_posix()
        for expression in parse_dashboard_expressions(path):
            for metric in metric_tokens(expression):
                if not resolve_metric(metric, emitted):
                    findings.append(
                        Finding(
                            "dashboards",
                            relative,
                            f"panel expression '{expression}' uses metric '{metric}', which the exporter never emits",
                        )
                    )
    return findings


def check_slo_registry(
    root: Path,
    emitted: dict[str, str],
    documents: DocumentIndex,
    alert_names: set[str],
) -> tuple[list[Finding], set[str]]:
    findings: list[Finding] = []
    path = root / "src" / "Meridian.Platform" / "Monitoring" / "Core" / "SloDefinitionRegistry.cs"
    if not path.is_file():
        return [Finding("slo", path.as_posix(), "SLO registry source is missing")], set()

    definitions = parse_slo_registry(path)
    if not definitions:
        return [Finding("slo", path.relative_to(root).as_posix(), "no SLO definitions found")], set()

    relative = path.relative_to(root).as_posix()
    for definition in definitions:
        metric_entry = definition.fields.get("MetricName")
        if metric_entry is None:
            findings.append(Finding("slo", f"{relative}:{definition.line}", f"{definition.id} has no MetricName"))
        else:
            line_number, metric = metric_entry
            # SLOs may legitimately measure a Prometheus built-in such as `up`.
            if metric.startswith(METRIC_PREFIX) and not resolve_metric(metric, emitted):
                findings.append(
                    Finding(
                        "slo",
                        f"{relative}:{line_number}",
                        f"{definition.id} measures metric '{metric}', which the exporter never emits",
                    )
                )

        alert_entry = definition.fields.get("AlertRuleName")
        if alert_entry is None or not alert_entry[1]:
            findings.append(Finding("slo", f"{relative}:{definition.line}", f"{definition.id} names no alert rule"))
        elif alert_entry[1] not in alert_names:
            findings.append(
                Finding(
                    "slo",
                    f"{relative}:{alert_entry[0]}",
                    f"{definition.id} names alert '{alert_entry[1]}', which alert-rules.yml does not define",
                )
            )

        for field in ("RunbookSection", "SloDocSection"):
            entry = definition.fields.get(field)
            if entry is None or not entry[1]:
                findings.append(Finding("slo", f"{relative}:{definition.line}", f"{definition.id} has no {field}"))
                continue
            check_document_link(findings, "slo", f"{relative}:{entry[0]}", entry[1], documents)

    return findings, {definition.id for definition in definitions}


class RunbookEntry:
    def __init__(self, alert_name: str, line: int) -> None:
        self.alert_name = alert_name
        self.line = line
        self.fields: dict[str, tuple[int, str]] = {}


def parse_runbook_registry(path: Path) -> list[RunbookEntry]:
    """Parse the alert-runbook registry's object initialisers into alert/field records."""
    entries: list[RunbookEntry] = []
    current: RunbookEntry | None = None
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        name_match = RUNBOOK_ENTRY_NAME.search(line)
        if name_match:
            current = RunbookEntry(name_match.group("value"), number)
            entries.append(current)
            continue
        if current is None:
            continue
        field_match = RUNBOOK_ENTRY_FIELD.search(line)
        if field_match:
            current.fields[field_match.group("field")] = (number, field_match.group("value"))
    return entries


def check_runbook_registry(
    root: Path,
    documents: DocumentIndex,
    alert_names: set[str],
    slo_ids: set[str],
) -> list[Finding]:
    """Bind the runtime alert-runbook registry to the deployed alerts and the SLO registry."""
    findings: list[Finding] = []
    path = root / "src" / "Meridian.Platform" / "Monitoring" / "Core" / "AlertRunbookRegistry.cs"
    if not path.is_file():
        return [Finding("runbook", path.as_posix(), "alert-runbook registry source is missing")]

    entries = parse_runbook_registry(path)
    if not entries:
        return [Finding("runbook", path.relative_to(root).as_posix(), "no alert-runbook entries found")]

    relative = path.relative_to(root).as_posix()
    for entry in entries:
        if entry.alert_name not in alert_names:
            findings.append(
                Finding(
                    "runbook",
                    f"{relative}:{entry.line}",
                    f"registry entry '{entry.alert_name}' has no matching rule in alert-rules.yml",
                )
            )

        url_entry = entry.fields.get("RunbookUrl")
        if url_entry is None or not url_entry[1]:
            findings.append(Finding("runbook", f"{relative}:{entry.line}", f"'{entry.alert_name}' has no RunbookUrl"))
        else:
            check_document_link(findings, "runbook", f"{relative}:{url_entry[0]}", url_entry[1], documents)

        slo_entry = entry.fields.get("SloId")
        if slo_entry is not None and slo_entry[1] and slo_entry[1] not in slo_ids:
            findings.append(
                Finding(
                    "runbook",
                    f"{relative}:{slo_entry[0]}",
                    f"'{entry.alert_name}' maps to SLO '{slo_entry[1]}', which the SLO registry does not define",
                )
            )

    registered = {entry.alert_name for entry in entries}
    for alert_name in sorted(alert_names - registered):
        findings.append(
            Finding(
                "runbook",
                relative,
                f"alert '{alert_name}' is deployed but has no AlertRunbookEntry, so it reaches operators without response guidance",
            )
        )

    return findings


def check_provisioning_secrets(root: Path) -> list[Finding]:
    """Fail when a provisioned monitoring stack ships literal administrator credentials."""
    findings: list[Finding] = []
    for path in sorted((root / "deploy").rglob("docker-compose*.yml")):
        relative = path.relative_to(root).as_posix()
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
            if line.strip().startswith("#"):
                continue
            match = COMPOSE_SECRET_ASSIGNMENT.search(line)
            if not match:
                continue
            value = match.group("value").strip().strip("\"'")
            if value.startswith("${") or not value:
                continue
            findings.append(
                Finding(
                    "provisioning",
                    f"{relative}:{number}",
                    f"{match.group('key')} is set to a literal value; require it from the environment instead",
                )
            )
    return findings


def parse_arguments(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=REPO_ROOT, help="Repository root to validate.")
    parser.add_argument("--summary", action="store_true", help="Print one line per check instead of full detail.")
    return parser.parse_args(argv)


def run(root: Path) -> tuple[list[Finding], dict[str, int]]:
    emitted = collect_emitted_metrics(root)
    documents = DocumentIndex(root)

    alert_findings, alert_names = check_alerts(root, emitted, documents)
    dashboard_findings = check_dashboards(root, emitted)
    slo_findings, slo_ids = check_slo_registry(root, emitted, documents, alert_names)
    runbook_findings = check_runbook_registry(root, documents, alert_names, slo_ids)
    provisioning_findings = check_provisioning_secrets(root)

    findings = alert_findings + dashboard_findings + slo_findings + runbook_findings + provisioning_findings
    counts = {
        "emitted_metrics": len(emitted),
        "alerts": len(alert_names),
        "slos": len(slo_ids),
        "alert_findings": len(alert_findings),
        "dashboard_findings": len(dashboard_findings),
        "slo_findings": len(slo_findings),
        "runbook_findings": len(runbook_findings),
        "provisioning_findings": len(provisioning_findings),
    }
    return findings, counts


def main(argv: list[str] | None = None) -> int:
    args = parse_arguments(argv)
    root = args.repo_root.resolve()

    if not (root / "src").is_dir():
        print(f"ERROR: '{root}' does not look like the repository root.", file=sys.stderr)
        return 2

    findings, counts = run(root)

    if args.summary:
        print(
            "observability-contract: "
            f"{counts['emitted_metrics']} emitted metrics, {counts['alerts']} alerts, "
            f"{counts['slos']} SLOs, {len(findings)} finding(s)"
        )
    else:
        print(f"Exporter emits {counts['emitted_metrics']} Prometheus metrics.")
        print(f"alert-rules.yml defines {counts['alerts']} alerts.")
        print(f"SloDefinitionRegistry defines {counts['slos']} objectives.")

    if not findings:
        print("Observability contract is consistent: alerts, dashboards, and SLOs resolve to emitted metrics.")
        return 0

    print("", file=sys.stderr)
    print(f"Observability contract validation failed with {len(findings)} finding(s):", file=sys.stderr)
    for finding in findings:
        print(f"  {finding.render()}", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
