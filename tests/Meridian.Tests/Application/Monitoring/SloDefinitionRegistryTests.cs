using FluentAssertions;
using Meridian.Application.Monitoring.Core;
using Xunit;

namespace Meridian.Tests.Application.Monitoring;

/// <summary>
/// Tests for <see cref="SloDefinitionRegistry"/> and <see cref="AlertRunbookRegistry"/>.
/// Validates SLO registration, evaluation, compliance scoring, and alert-runbook linkage.
/// </summary>
public sealed class SloDefinitionRegistryTests
{
    [Fact]
    public void Instance_HasDefaultSloDefinitions()
    {
        var registry = SloDefinitionRegistry.Instance;
        var all = registry.GetAll();

        all.Should().NotBeEmpty();
        all.Count.Should().BeGreaterThanOrEqualTo(7);
    }

    [Fact]
    public void Get_ExistingSloId_ReturnsDefinition()
    {
        var registry = SloDefinitionRegistry.Instance;

        var slo = registry.Get("SLO-ING-001");

        slo.Should().NotBeNull();
        slo!.Name.Should().Be("End-to-End Ingestion Latency");
        slo.MetricName.Should().Be("mdc_provider_latency_seconds");
        slo.Subsystem.Should().Be(SloSubsystem.Ingestion);
    }

    [Fact]
    public void Get_NonExistentSloId_ReturnsNull()
    {
        var registry = SloDefinitionRegistry.Instance;

        registry.Get("SLO-NONEXISTENT").Should().BeNull();
    }

    [Fact]
    public void GetAll_FiltersBySubsystem()
    {
        var registry = SloDefinitionRegistry.Instance;

        var ingestion = registry.GetAll(SloSubsystem.Ingestion);
        ingestion.Should().HaveCountGreaterThanOrEqualTo(2);
        ingestion.Should().OnlyContain(d => d.Subsystem == SloSubsystem.Ingestion);
    }

    [Fact]
    public void AllSlos_HaveRequiredFields()
    {
        var registry = SloDefinitionRegistry.Instance;
        var all = registry.GetAll();

        foreach (var slo in all)
        {
            slo.Id.Should().NotBeNullOrWhiteSpace($"SLO missing Id");
            slo.Name.Should().NotBeNullOrWhiteSpace($"SLO {slo.Id} missing Name");
            slo.MetricName.Should().NotBeNullOrWhiteSpace($"SLO {slo.Id} missing MetricName");
            slo.Unit.Should().NotBeNullOrWhiteSpace($"SLO {slo.Id} missing Unit");
            slo.AlertRuleName.Should().NotBeNullOrWhiteSpace($"SLO {slo.Id} missing AlertRuleName");
            slo.RunbookSection.Should().NotBeNullOrWhiteSpace($"SLO {slo.Id} missing RunbookSection");
        }
    }

    [Fact]
    public void Evaluate_HealthyLatency_ReturnsHealthy()
    {
        var registry = SloDefinitionRegistry.Instance;

        var result = registry.Evaluate("SLO-ING-001", 1.0); // 1s < 2s target

        result.State.Should().Be(SloComplianceState.Healthy);
        result.Score.Should().Be(100.0);
    }

    [Fact]
    public void Evaluate_WarningLatency_ReturnsWarning()
    {
        var registry = SloDefinitionRegistry.Instance;

        var result = registry.Evaluate("SLO-ING-001", 3.5); // Between 2s and 5s

        result.State.Should().Be(SloComplianceState.Warning);
        result.Score.Should().BeInRange(1, 99);
    }

    [Fact]
    public void Evaluate_ViolationLatency_ReturnsViolation()
    {
        var registry = SloDefinitionRegistry.Instance;

        var result = registry.Evaluate("SLO-ING-001", 10.0); // > 5s critical

        result.State.Should().Be(SloComplianceState.Violation);
        result.Score.Should().Be(0);
    }

    [Fact]
    public void Evaluate_NonexistentSlo_ReturnsUnknown()
    {
        var registry = SloDefinitionRegistry.Instance;

        var result = registry.Evaluate("SLO-FAKE", 1.0);

        result.State.Should().Be(SloComplianceState.Unknown);
    }

    [Fact]
    public void GetDashboard_ReturnsGroupedSubsystems()
    {
        var registry = SloDefinitionRegistry.Instance;

        var dashboard = registry.GetDashboard();

        dashboard.TotalSlos.Should().BeGreaterThanOrEqualTo(7);
        dashboard.Subsystems.Should().NotBeEmpty();
        dashboard.Subsystems.Should().Contain(s => s.Subsystem == "Ingestion");
        dashboard.Subsystems.Should().Contain(s => s.Subsystem == "DataFreshness");
        dashboard.Subsystems.Should().Contain(s => s.Subsystem == "Storage");
    }

    [Fact]
    public void Register_CustomSlo_CanBeRetrieved()
    {
        var registry = SloDefinitionRegistry.Instance;
        var alertRegistry = AlertRunbookRegistry.Instance;

        // Register a matching alert entry so cross-registry consistency tests pass
        // even when running in parallel.
        alertRegistry.Register(new AlertRunbookEntry
        {
            AlertName = "CustomAlert",
            Severity = "info",
            IncidentPriority = "P3",
            Summary = "Custom test alert",
            RunbookUrl = "docs/operations/operator-runbook.md#custom",
            ProbableCauses = new[] { "Test" },
            ImmediateActions = new[] { "None" }
        });

        registry.Register(new SloDefinition
        {
            Id = "SLO-CUSTOM-001",
            Subsystem = SloSubsystem.Ingestion,
            Name = "Custom Test SLO",
            MetricName = "custom_metric",
            TargetValue = 10.0,
            CriticalThreshold = 50.0,
            Unit = "count",
            AlertRuleName = "CustomAlert",
            RunbookSection = "docs/custom-runbook.md"
        });

        try
        {
            var retrieved = registry.Get("SLO-CUSTOM-001");
            retrieved.Should().NotBeNull();
            retrieved!.Name.Should().Be("Custom Test SLO");
        }
        finally
        {
            // Clean up to avoid polluting the singleton for other tests
            registry.Unregister("SLO-CUSTOM-001");
        }
    }
}

/// <summary>
/// Tests for <see cref="AlertRunbookRegistry"/>.
/// </summary>
public sealed class AlertRunbookRegistryTests
{
    [Fact]
    public void Instance_HasDefaultEntries()
    {
        var registry = AlertRunbookRegistry.Instance;
        var all = registry.GetAll();

        all.Should().NotBeEmpty();
        all.Count.Should().BeGreaterThanOrEqualTo(11);
    }

    [Fact]
    public void GetByAlertName_ExistingAlert_ReturnsEntry()
    {
        var registry = AlertRunbookRegistry.Instance;

        var entry = registry.GetByAlertName("MeridianDown");

        entry.Should().NotBeNull();
        entry!.Severity.Should().Be("critical");
        entry.IncidentPriority.Should().Be("P1");
        entry.RunbookUrl.Should().Contain("operator-runbook.md");
    }

    [Fact]
    public void GetByAlertName_CaseInsensitive()
    {
        var registry = AlertRunbookRegistry.Instance;

        var entry = registry.GetByAlertName("mdcdown");

        entry.Should().NotBeNull();
    }

    [Fact]
    public void GetByAlertName_NonExistent_ReturnsNull()
    {
        var registry = AlertRunbookRegistry.Instance;

        registry.GetByAlertName("NonExistentAlert").Should().BeNull();
    }

    [Fact]
    public void GetRunbookUrl_ExistingAlert_ReturnsUrl()
    {
        var registry = AlertRunbookRegistry.Instance;

        var url = registry.GetRunbookUrl("MeridianStorageWriteErrors");

        url.Should().NotBeNull();
        url.Should().Contain("storage-write-errors");
    }

    [Fact]
    public void AllEntries_HaveRequiredFields()
    {
        var registry = AlertRunbookRegistry.Instance;
        var all = registry.GetAll();

        foreach (var entry in all)
        {
            entry.AlertName.Should().NotBeNullOrWhiteSpace($"Entry missing AlertName");
            entry.Severity.Should().NotBeNullOrWhiteSpace($"Alert {entry.AlertName} missing Severity");
            entry.IncidentPriority.Should().NotBeNullOrWhiteSpace($"Alert {entry.AlertName} missing IncidentPriority");
            entry.RunbookUrl.Should().NotBeNullOrWhiteSpace($"Alert {entry.AlertName} missing RunbookUrl");
            entry.ProbableCauses.Should().NotBeEmpty($"Alert {entry.AlertName} missing ProbableCauses");
            entry.ImmediateActions.Should().NotBeEmpty($"Alert {entry.AlertName} missing ImmediateActions");
        }
    }

    [Fact]
    public void AllEntries_MapToRunbookSections()
    {
        var registry = AlertRunbookRegistry.Instance;
        var all = registry.GetAll();

        foreach (var entry in all)
        {
            entry.RunbookUrl.Should().StartWith("docs/operations/operator-runbook.md#",
                because: $"Alert {entry.AlertName} should link to a specific runbook section");
        }
    }

    [Fact]
    public void GetBySeverity_CriticalAlerts_ReturnsOnlyCritical()
    {
        var registry = AlertRunbookRegistry.Instance;

        var critical = registry.GetBySeverity("critical");

        critical.Should().NotBeEmpty();
        critical.Should().OnlyContain(e => e.Severity == "critical");
    }

    [Fact]
    public void EnrichWithRunbook_AddsContextToAlert()
    {
        var registry = AlertRunbookRegistry.Instance;

        var alert = MonitoringAlert.Critical(
            "test",
            AlertCategory.Connection,
            "MeridianDown",
            "Service is unreachable");

        var enriched = registry.EnrichWithRunbook(alert);

        enriched.Context.Should().NotBeNull();
        enriched.Context!.Should().ContainKey("runbookUrl");
        enriched.Context.Should().ContainKey("incidentPriority");
    }

    [Fact]
    public void EnrichWithRunbook_NoMatch_ReturnsOriginal()
    {
        var registry = AlertRunbookRegistry.Instance;

        var alert = MonitoringAlert.Info("test", AlertCategory.Connection, "UnknownAlert", "Test");

        var enriched = registry.EnrichWithRunbook(alert);

        enriched.Should().Be(alert);
    }

    [Fact]
    public void CriticalAlerts_HaveSloMapping()
    {
        var registry = AlertRunbookRegistry.Instance;
        var critical = registry.GetBySeverity("critical");

        // Critical alerts should generally map to an SLO
        foreach (var entry in critical)
        {
            entry.SloId.Should().NotBeNullOrEmpty(
                because: $"Critical alert {entry.AlertName} should map to an SLO for proper escalation");
        }
    }

    [Fact]
    public void SloAlertMappings_AreConsistent()
    {
        var runbookRegistry = AlertRunbookRegistry.Instance;
        var sloRegistry = SloDefinitionRegistry.Instance;

        var slos = sloRegistry.GetAll();

        foreach (var slo in slos)
        {
            if (!string.IsNullOrEmpty(slo.AlertRuleName))
            {
                var alertEntry = runbookRegistry.GetByAlertName(slo.AlertRuleName);
                alertEntry.Should().NotBeNull(
                    because: $"SLO {slo.Id} references alert {slo.AlertRuleName} which should exist in AlertRunbookRegistry");
            }
        }
    }
}
