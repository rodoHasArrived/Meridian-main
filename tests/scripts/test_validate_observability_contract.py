import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = REPO_ROOT / "build" / "scripts" / "ci" / "validate-observability-contract.py"
SPEC = importlib.util.spec_from_file_location("validate_observability_contract", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


EXPORTER_SOURCE = """
namespace Meridian.Application.Monitoring;

public static class PrometheusMetrics
{
    private static readonly Counter Published = Prometheus.Metrics.CreateCounter(
        "mdc_events_published_total",
        "Total events published");

    private static readonly Gauge DropRate = Prometheus.Metrics.CreateGauge(
        "mdc_drop_rate_percent",
        "Drop rate percent");

    private static readonly Histogram Latency = Prometheus.Metrics.CreateHistogram(
        "mdc_processing_latency_microseconds",
        "Processing latency");
}
"""

RUNBOOK_DOC = """# Operator Runbook

**Status:** active

## High Drop Rate

Body.

## Application Down

Body.
"""

SLO_DOC = """# Service Level Objectives

**Status:** active

## SLO-ING-002

Body.
"""

STUB_DOC = """# Meridian Operations Archive Stub

**Status:** archive-migration-stub

## High Drop Rate

Body.
"""


def alert_rules(expr: str = "mdc_drop_rate_percent > 1", runbook: str = "docs/operators/operator-runbook.md#high-drop-rate") -> str:
    return f"""groups:
  - name: mdc_pipeline
    rules:
      - alert: MeridianHighDropRate
        expr: {expr}
        for: 5m
        annotations:
          summary: "High drop rate"
          runbook_url: "{runbook}"
"""


def slo_registry(
    metric: str = "mdc_drop_rate_percent",
    alert: str = "MeridianHighDropRate",
    runbook: str = "docs/operators/operator-runbook.md#high-drop-rate",
    slo_doc: str = "docs/operators/service-level-objectives.md#slo-ing-002",
) -> str:
    return f"""namespace Meridian.Platform.Monitoring.Core;

public sealed class SloDefinitionRegistry
{{
    private void RegisterDefaults()
    {{
        Register(new SloDefinition
        {{
            Id = "SLO-ING-002",
            Name = "Event Drop Rate",
            MetricName = "{metric}",
            AlertRuleName = "{alert}",
            RunbookSection = "{runbook}",
            SloDocSection = "{slo_doc}"
        }});
    }}
}}
"""


def runbook_registry(
    alert: str = "MeridianHighDropRate",
    runbook: str = "docs/operators/operator-runbook.md#high-drop-rate",
    slo_id: str = "SLO-ING-002",
) -> str:
    return f"""namespace Meridian.Platform.Monitoring.Core;

public sealed class AlertRunbookRegistry
{{
    private void RegisterDefaults()
    {{
        Register(new AlertRunbookEntry
        {{
            AlertName = "{alert}",
            Severity = "warning",
            RunbookUrl = "{runbook}",
            SloId = "{slo_id}"
        }});
    }}
}}
"""


def dashboard(expr: str = "rate(mdc_events_published_total[1m])") -> str:
    return json.dumps(
        {
            "title": "Meridian Overview",
            "panels": [{"title": "Published", "targets": [{"expr": expr, "refId": "A"}]}],
        }
    )


def compose(admin_password: str = "${GF_SECURITY_ADMIN_PASSWORD:?required}") -> str:
    return f"""services:
  grafana:
    image: grafana/grafana:latest
    environment:
      - GF_SECURITY_ADMIN_PASSWORD={admin_password}
"""


# The exposition endpoint. Every other check reasons about "emitted" metrics, which is only
# meaningful when this serves the registry those declarations land in — it did not, and the gate
# would have reported a clean contract over alerts that could never fire.
ENDPOINT_SOURCE = """namespace Meridian.Application.UI;

public sealed class StatusEndpointHandlers
{
    public async Task<string> GetPrometheusMetricsAsync(CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await Prometheus.Metrics.DefaultRegistry.CollectAndExportAsTextAsync(buffer, cancellationToken);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
"""


class ObservabilityContractFixture:
    """Builds a minimal repository tree that satisfies the gate, for targeted mutation."""

    def __init__(self, root: Path) -> None:
        self.root = root
        monitoring_core = root / "src" / "Meridian.Platform" / "Monitoring" / "Core"
        monitoring_core.mkdir(parents=True)
        (root / "src" / "Meridian.Application" / "Monitoring").mkdir(parents=True)
        (root / "src" / "Meridian.Application" / "Http" / "Endpoints").mkdir(parents=True)
        (root / "deploy" / "monitoring" / "grafana").mkdir(parents=True)
        (root / "deploy" / "docker").mkdir(parents=True)
        (root / "docs" / "operators").mkdir(parents=True)

        self.exporter = root / "src" / "Meridian.Application" / "Monitoring" / "PrometheusMetrics.cs"
        self.endpoint = root / "src" / "Meridian.Application" / "Http" / "Endpoints" / "StatusEndpointHandlers.cs"
        self.slo_registry = monitoring_core / "SloDefinitionRegistry.cs"
        self.runbook_registry = monitoring_core / "AlertRunbookRegistry.cs"
        self.alerts = root / "deploy" / "monitoring" / "alert-rules.yml"
        self.dashboard = root / "deploy" / "monitoring" / "grafana" / "overview.json"
        self.compose = root / "deploy" / "docker" / "docker-compose.yml"
        self.runbook_doc = root / "docs" / "operators" / "operator-runbook.md"
        self.slo_doc = root / "docs" / "operators" / "service-level-objectives.md"

        self.exporter.write_text(EXPORTER_SOURCE, encoding="utf-8")
        self.endpoint.write_text(ENDPOINT_SOURCE, encoding="utf-8")
        self.slo_registry.write_text(slo_registry(), encoding="utf-8")
        self.runbook_registry.write_text(runbook_registry(), encoding="utf-8")
        self.alerts.write_text(alert_rules(), encoding="utf-8")
        self.dashboard.write_text(dashboard(), encoding="utf-8")
        self.compose.write_text(compose(), encoding="utf-8")
        self.runbook_doc.write_text(RUNBOOK_DOC, encoding="utf-8")
        self.slo_doc.write_text(SLO_DOC, encoding="utf-8")

    def run(self) -> list:
        findings, _ = MODULE.run(self.root)
        return findings

    def messages(self) -> list[str]:
        return [finding.render() for finding in self.run()]


class ValidateObservabilityContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.fixture = ObservabilityContractFixture(Path(self._tmp.name))

    def test_reports_when_the_endpoint_does_not_serve_the_registry(self):
        # The defect this whole gate rested on: /metrics hand-wrote ten series and never
        # touched the prometheus-net registry, so 81 declarations were never scraped and every
        # other check here was reasoning about metrics nothing exposed.
        self.fixture.endpoint.write_text(
            "namespace Meridian.Application.UI;\n\n"
            "public sealed class StatusEndpointHandlers\n"
            "{\n"
            "    public string GetPrometheusMetrics() => \"mdc_published 0\";\n"
            "}\n",
            encoding="utf-8",
        )

        messages = self.fixture.messages()

        self.assertTrue(any("[exporter]" in m for m in messages), msg=messages)
        self.assertTrue(any("never scraped" in m for m in messages), msg=messages)

    def test_reports_when_the_endpoint_source_is_missing(self):
        # An absent exporter cannot be read as a passing one; the gate would otherwise assume
        # exposure it never verified.
        self.fixture.endpoint.unlink()

        messages = self.fixture.messages()

        self.assertTrue(any("[exporter]" in m for m in messages), msg=messages)

    def test_a_commented_out_registry_export_does_not_count(self):
        # strip_comments runs first, so a call left behind in a comment cannot satisfy this.
        self.fixture.endpoint.write_text(
            "namespace Meridian.Application.UI;\n\n"
            "public sealed class StatusEndpointHandlers\n"
            "{\n"
            "    // await registry.CollectAndExportAsTextAsync(buffer, cancellationToken);\n"
            "    public string GetPrometheusMetrics() => string.Empty;\n"
            "}\n",
            encoding="utf-8",
        )

        messages = self.fixture.messages()

        self.assertTrue(any("[exporter]" in m for m in messages), msg=messages)

    def test_consistent_fixture_produces_no_findings(self):
        self.assertEqual(self.fixture.messages(), [])

    def test_alert_on_unexported_metric_fails(self):
        self.fixture.alerts.write_text(alert_rules(expr="mdc_pipeline_queue_utilization > 0.9"), encoding="utf-8")

        messages = self.fixture.messages()

        self.assertTrue(
            any("mdc_pipeline_queue_utilization" in m and "never emits" in m for m in messages),
            msg=messages,
        )

    def test_histogram_derived_suffixes_resolve_to_base_series(self):
        self.fixture.alerts.write_text(
            alert_rules(expr="histogram_quantile(0.99, rate(mdc_processing_latency_microseconds_bucket[5m])) > 5000"),
            encoding="utf-8",
        )

        self.assertEqual(self.fixture.messages(), [])

    def test_derived_suffix_on_non_histogram_does_not_resolve(self):
        # `_bucket` is only meaningful for a histogram; a counter has no bucket series.
        self.fixture.alerts.write_text(
            alert_rules(expr="rate(mdc_events_published_total_bucket[5m]) > 0"),
            encoding="utf-8",
        )

        messages = self.fixture.messages()

        self.assertTrue(any("mdc_events_published_total_bucket" in m for m in messages), msg=messages)

    def test_prometheus_builtin_series_are_out_of_scope(self):
        self.fixture.alerts.write_text(alert_rules(expr='up{job="meridian"} == 0'), encoding="utf-8")

        self.assertEqual(self.fixture.messages(), [])

    def test_block_scalar_expression_is_validated(self):
        # `expr: |` puts the PromQL on following indented lines; capturing only the marker
        # would leave nothing to check, so an unexported series would pass the gate.
        self.fixture.alerts.write_text(
            """groups:
  - name: mdc_pipeline
    rules:
      - alert: MeridianHighDropRate
        expr: |
          mdc_pipeline_queue_utilization > 0.9
          or mdc_drop_rate_percent > 1
        for: 5m
        annotations:
          summary: "High drop rate"
          runbook_url: "docs/operators/operator-runbook.md#high-drop-rate"
""",
            encoding="utf-8",
        )

        messages = self.fixture.messages()

        self.assertTrue(any("mdc_pipeline_queue_utilization" in m for m in messages), msg=messages)

    def test_folded_block_scalar_expression_is_validated(self):
        self.fixture.alerts.write_text(
            """groups:
  - name: mdc_pipeline
    rules:
      - alert: MeridianHighDropRate
        expr: >
          mdc_drop_rate_percent > 1
        for: 5m
        annotations:
          summary: "High drop rate"
          runbook_url: "docs/operators/operator-runbook.md#high-drop-rate"
""",
            encoding="utf-8",
        )

        self.assertEqual(self.fixture.messages(), [])

    def test_unparsable_dashboard_fails(self):
        # A truncated dashboard Grafana cannot load must not look like one with no PromQL.
        self.fixture.dashboard.write_text('{"panels": [{"targets": [', encoding="utf-8")

        messages = self.fixture.messages()

        self.assertTrue(any("not valid JSON" in m for m in messages), msg=messages)

    def test_dashboard_on_unexported_metric_fails(self):
        self.fixture.dashboard.write_text(dashboard(expr="mdc_data_quality_score"), encoding="utf-8")

        messages = self.fixture.messages()

        self.assertTrue(any("mdc_data_quality_score" in m and "never emits" in m for m in messages), msg=messages)

    def test_runbook_anchor_that_does_not_resolve_fails(self):
        self.fixture.alerts.write_text(
            alert_rules(runbook="docs/operators/operator-runbook.md#no-such-section"),
            encoding="utf-8",
        )

        messages = self.fixture.messages()

        self.assertTrue(any("#no-such-section" in m and "does not resolve" in m for m in messages), msg=messages)

    def test_runbook_link_to_archive_stub_fails(self):
        stub = self.fixture.root / "docs" / "operations"
        stub.mkdir(parents=True)
        (stub / "operator-runbook.md").write_text(STUB_DOC, encoding="utf-8")
        self.fixture.alerts.write_text(
            alert_rules(runbook="docs/operations/operator-runbook.md#high-drop-rate"),
            encoding="utf-8",
        )

        messages = self.fixture.messages()

        self.assertTrue(any("archive-migration stub" in m for m in messages), msg=messages)

    def test_missing_runbook_document_fails(self):
        self.fixture.alerts.write_text(
            alert_rules(runbook="docs/operators/does-not-exist.md#high-drop-rate"),
            encoding="utf-8",
        )

        messages = self.fixture.messages()

        self.assertTrue(any("does not exist" in m for m in messages), msg=messages)

    def test_alert_without_runbook_annotation_fails(self):
        self.fixture.alerts.write_text(
            """groups:
  - name: mdc_pipeline
    rules:
      - alert: MeridianHighDropRate
        expr: mdc_drop_rate_percent > 1
        annotations:
          summary: "High drop rate"
""",
            encoding="utf-8",
        )

        messages = self.fixture.messages()

        self.assertTrue(any("no runbook_url annotation" in m for m in messages), msg=messages)

    def test_slo_naming_missing_alert_fails(self):
        self.fixture.slo_registry.write_text(slo_registry(alert="MeridianGhostAlert"), encoding="utf-8")

        messages = self.fixture.messages()

        self.assertTrue(any("MeridianGhostAlert" in m and "does not define" in m for m in messages), msg=messages)

    def test_slo_on_unexported_metric_fails(self):
        self.fixture.slo_registry.write_text(slo_registry(metric="mdc_storage_write_errors_total"), encoding="utf-8")

        messages = self.fixture.messages()

        self.assertTrue(
            any("mdc_storage_write_errors_total" in m and "never emits" in m for m in messages),
            msg=messages,
        )

    def test_slo_doc_anchor_that_does_not_resolve_fails(self):
        self.fixture.slo_registry.write_text(
            slo_registry(slo_doc="docs/operators/service-level-objectives.md#slo-ing-999"),
            encoding="utf-8",
        )

        messages = self.fixture.messages()

        self.assertTrue(any("#slo-ing-999" in m for m in messages), msg=messages)

    def test_deployed_alert_without_runbook_entry_fails(self):
        self.fixture.runbook_registry.write_text(runbook_registry(alert="MeridianSomethingElse"), encoding="utf-8")

        messages = self.fixture.messages()

        self.assertTrue(
            any("MeridianHighDropRate" in m and "no AlertRunbookEntry" in m for m in messages),
            msg=messages,
        )

    def test_runbook_entry_mapping_unknown_slo_fails(self):
        self.fixture.runbook_registry.write_text(runbook_registry(slo_id="SLO-GHOST-001"), encoding="utf-8")

        messages = self.fixture.messages()

        self.assertTrue(any("SLO-GHOST-001" in m for m in messages), msg=messages)

    def test_literal_grafana_admin_password_fails(self):
        self.fixture.compose.write_text(compose(admin_password="admin"), encoding="utf-8")

        messages = self.fixture.messages()

        self.assertTrue(any("GF_SECURITY_ADMIN_PASSWORD" in m and "literal value" in m for m in messages), msg=messages)

    def test_environment_supplied_grafana_password_passes(self):
        self.fixture.compose.write_text(compose(admin_password="${GF_SECURITY_ADMIN_PASSWORD}"), encoding="utf-8")

        self.assertEqual(self.fixture.messages(), [])

    def test_required_form_with_a_spaced_message_passes(self):
        # The message contains spaces; a value regex that stops at whitespace would read only
        # "${GF_SECURITY_ADMIN_PASSWORD:?set" and reject a correct declaration.
        self.fixture.compose.write_text(
            compose(admin_password="${GF_SECURITY_ADMIN_PASSWORD:?set it before starting the monitoring profile}"),
            encoding="utf-8",
        )

        self.assertEqual(self.fixture.messages(), [])

    def test_default_value_expansion_does_not_bypass_the_gate(self):
        # Compose starts with "admin" whenever the variable is unset, so accepting anything
        # beginning with "${" would ship a known password behind a passing gate.
        self.fixture.compose.write_text(
            compose(admin_password="${GF_SECURITY_ADMIN_PASSWORD:-admin}"), encoding="utf-8"
        )

        messages = self.fixture.messages()

        self.assertTrue(any("supplies a default" in m for m in messages), msg=messages)

    def test_dash_default_expansion_does_not_bypass_the_gate(self):
        self.fixture.compose.write_text(
            compose(admin_password="${GF_SECURITY_ADMIN_PASSWORD-admin}"), encoding="utf-8"
        )

        messages = self.fixture.messages()

        self.assertTrue(any("supplies a default" in m for m in messages), msg=messages)

    def test_commented_out_metric_registration_is_not_treated_as_emitted(self):
        # Deleting a metric but leaving its declaration commented out would otherwise keep every
        # dependent alert and SLO passing while Prometheus exposed no such series.
        self.fixture.exporter.write_text(
            EXPORTER_SOURCE
            + '\n// private static readonly Gauge Gone = Prometheus.Metrics.CreateGauge("mdc_gone_gauge", "gone");\n',
            encoding="utf-8",
        )
        self.fixture.alerts.write_text(alert_rules(expr="mdc_gone_gauge > 1"), encoding="utf-8")

        messages = self.fixture.messages()

        self.assertTrue(any("mdc_gone_gauge" in m and "never emits" in m for m in messages), msg=messages)

    def test_block_commented_metric_registration_is_not_treated_as_emitted(self):
        self.fixture.exporter.write_text(
            EXPORTER_SOURCE
            + '\n/* private static readonly Gauge Gone = Prometheus.Metrics.CreateGauge("mdc_block_gauge", "gone"); */\n',
            encoding="utf-8",
        )
        self.fixture.alerts.write_text(alert_rules(expr="mdc_block_gauge > 1"), encoding="utf-8")

        messages = self.fixture.messages()

        self.assertTrue(any("mdc_block_gauge" in m and "never emits" in m for m in messages), msg=messages)

    def test_a_metric_name_inside_a_string_literal_is_still_recognised(self):
        # Comment stripping must not corrupt string literals: the metric name itself lives in one.
        self.fixture.alerts.write_text(alert_rules(expr="mdc_drop_rate_percent > 1"), encoding="utf-8")

        self.assertEqual(self.fixture.messages(), [])

    def test_commented_credential_is_not_a_finding(self):
        self.fixture.compose.write_text(
            "services:\n  grafana:\n    environment:\n      # - GF_SECURITY_ADMIN_PASSWORD=admin\n",
            encoding="utf-8",
        )

        self.assertEqual(self.fixture.messages(), [])


class SlugifyHeadingTests(unittest.TestCase):
    def test_slug_matches_github_anchor_shape(self):
        self.assertEqual(MODULE.slugify_heading("High Drop Rate"), "high-drop-rate")
        self.assertEqual(MODULE.slugify_heading("SLO-ING-001"), "slo-ing-001")
        self.assertEqual(MODULE.slugify_heading("Freshness SLA Violation"), "freshness-sla-violation")

    def test_slug_strips_inline_code_and_links(self):
        self.assertEqual(MODULE.slugify_heading("Use `mdc_drop_rate_percent`"), "use-mdc_drop_rate_percent")
        self.assertEqual(MODULE.slugify_heading("See [the runbook](./runbook.md)"), "see-the-runbook")


class RepositoryContractTests(unittest.TestCase):
    """The real repository must satisfy the gate, not just the synthetic fixture."""

    def test_repository_observability_contract_is_consistent(self):
        findings, counts = MODULE.run(REPO_ROOT)

        self.assertEqual([f.render() for f in findings], [])
        self.assertGreater(counts["emitted_metrics"], 0)
        self.assertGreater(counts["alerts"], 0)
        self.assertGreater(counts["slos"], 0)


if __name__ == "__main__":
    unittest.main()
