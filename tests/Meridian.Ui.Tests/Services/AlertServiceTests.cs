using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

/// <summary>
/// Tests for <see cref="AlertService"/> — alert management, deduplication,
/// suppression, severity escalation, grouping, and playbook attachment.
/// </summary>
public sealed class AlertServiceTests
{
    private static AlertService CreateService() => AlertService.Instance;

    // ── Singleton ────────────────────────────────────────────────────

    [Fact]
    public void Instance_ShouldReturnSingleton()
    {
        var a = AlertService.Instance;
        var b = AlertService.Instance;
        a.Should().BeSameAs(b);
    }

    // ── RaiseAlert ───────────────────────────────────────────────────

    [Fact]
    public void RaiseAlert_ShouldCreateNewAlert()
    {
        var svc = CreateService();

        var alert = svc.RaiseAlert(
            "Test Alert " + Guid.NewGuid(),
            "Description",
            AlertSeverity.Warning,
            BusinessImpact.Low,
            "TestCategory");

        alert.Should().NotBeNull();
        alert.Id.Should().NotBeNullOrEmpty();
        alert.OccurrenceCount.Should().Be(1);
        alert.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void RaiseAlert_DuplicateAlert_ShouldIncrementOccurrenceCount()
    {
        var svc = CreateService();
        var uniqueTitle = "DuplicateTest-" + Guid.NewGuid();
        var category = "Dup-" + Guid.NewGuid();

        var first = svc.RaiseAlert(uniqueTitle, "d1", AlertSeverity.Warning, BusinessImpact.Low, category);
        var second = svc.RaiseAlert(uniqueTitle, "d2", AlertSeverity.Warning, BusinessImpact.Low, category);

        second.Should().BeSameAs(first);
        first.OccurrenceCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void RaiseAlert_DuplicateAlert_ShouldMergeAffectedResources()
    {
        var svc = CreateService();
        var title = "ResourceMerge-" + Guid.NewGuid();
        var cat = "Merge-" + Guid.NewGuid();

        svc.RaiseAlert(title, "d", AlertSeverity.Info, BusinessImpact.None, cat,
            affectedResources: new[] { "SPY" });
        var updated = svc.RaiseAlert(title, "d", AlertSeverity.Info, BusinessImpact.None, cat,
            affectedResources: new[] { "AAPL", "SPY" });

        updated.AffectedResources.Should().Contain("SPY");
        updated.AffectedResources.Should().Contain("AAPL");
    }

    [Fact]
    public void RaiseAlert_SeverityEscalation_AfterManyOccurrences()
    {
        var svc = CreateService();
        var title = "Escalate-" + Guid.NewGuid();
        var cat = "Esc-" + Guid.NewGuid();

        Alert alert = null!;
        for (int i = 0; i < 7; i++)
        {
            alert = svc.RaiseAlert(title, "desc", AlertSeverity.Warning, BusinessImpact.Medium, cat);
        }

        // After 6+ occurrences, severity should escalate to Error if it was below Error
        alert.Severity.Should().Be(AlertSeverity.Error);
    }

    [Fact]
    public void RaiseAlert_ShouldRaiseAlertRaisedEvent()
    {
        var svc = CreateService();
        AlertEventArgs? received = null;
        svc.AlertRaised += (_, args) => received = args;

        svc.RaiseAlert("EventTest-" + Guid.NewGuid(), "desc",
            AlertSeverity.Info, BusinessImpact.None, "EventCat-" + Guid.NewGuid());

        received.Should().NotBeNull();
        received!.Alert.Should().NotBeNull();
    }

    [Fact]
    public void RaiseAlert_WithPlaybookId_ShouldAttachPlaybook()
    {
        var svc = CreateService();
        var playbookId = "test-playbook-" + Guid.NewGuid();
        svc.RegisterPlaybook(playbookId, new AlertPlaybook
        {
            Title = "Test Playbook",
            Categories = new[] { "TestCat" },
            PossibleCauses = new[] { "Cause 1" },
            RemediationSteps = new[] { new RemediationStep(1, "Step 1", "Do something") }
        });

        var alert = svc.RaiseAlert("PlaybookTest-" + Guid.NewGuid(), "desc",
            AlertSeverity.Warning, BusinessImpact.Low, "SomeCat-" + Guid.NewGuid(),
            playbookId: playbookId);

        alert.Playbook.Should().NotBeNull();
        alert.Playbook!.Title.Should().Be("Test Playbook");
    }

    // ── ResolveAlert ─────────────────────────────────────────────────

    [Fact]
    public void ResolveAlert_ShouldMarkAsResolved()
    {
        var svc = CreateService();
        var alert = svc.RaiseAlert("Resolve-" + Guid.NewGuid(), "desc",
            AlertSeverity.Warning, BusinessImpact.Low, "ResolveCat-" + Guid.NewGuid());

        svc.ResolveAlert(alert.Id);

        alert.IsResolved.Should().BeTrue();
        alert.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public void ResolveAlert_ShouldRaiseAlertResolvedEvent()
    {
        var svc = CreateService();
        var alert = svc.RaiseAlert("ResolveEvt-" + Guid.NewGuid(), "desc",
            AlertSeverity.Info, BusinessImpact.None, "ResolveEvtCat-" + Guid.NewGuid());

        AlertEventArgs? received = null;
        svc.AlertResolved += (_, args) => received = args;

        svc.ResolveAlert(alert.Id);

        received.Should().NotBeNull();
        received!.Alert.Id.Should().Be(alert.Id);
    }

    [Fact]
    public void ResolveAlert_ShouldMoveToResolvedList()
    {
        var svc = CreateService();
        var alert = svc.RaiseAlert("ResolveList-" + Guid.NewGuid(), "desc",
            AlertSeverity.Warning, BusinessImpact.Low, "ResListCat-" + Guid.NewGuid());

        svc.ResolveAlert(alert.Id);

        svc.GetResolvedAlerts().Should().Contain(a => a.Id == alert.Id);
    }

    [Fact]
    public void ResolveAlert_NonExistentId_ShouldNotThrow()
    {
        var svc = CreateService();
        var act = () => svc.ResolveAlert("non-existent-id");
        act.Should().NotThrow();
    }

    // ── SnoozeAlert ──────────────────────────────────────────────────

    [Fact]
    public void SnoozeAlert_ShouldMarkAsSnoozed()
    {
        var svc = CreateService();
        var alert = svc.RaiseAlert("Snooze-" + Guid.NewGuid(), "desc",
            AlertSeverity.Info, BusinessImpact.None, "SnoozeCat-" + Guid.NewGuid());

        svc.SnoozeAlert(alert.Id, TimeSpan.FromMinutes(30));

        alert.IsSnoozed.Should().BeTrue();
        alert.SnoozedUntil.Should().NotBeNull();
    }

    // ── AddSuppressionRule ───────────────────────────────────────────

    [Fact]
    public void AddSuppressionRule_ShouldSuppressMatchingAlerts()
    {
        var svc = CreateService();
        var cat = "SuppCat-" + Guid.NewGuid();

        svc.AddSuppressionRule(cat, null, TimeSpan.FromHours(1));

        var alert = svc.RaiseAlert("Suppress-" + Guid.NewGuid(), "desc",
            AlertSeverity.Warning, BusinessImpact.Low, cat);

        alert.IsSuppressed.Should().BeTrue();
    }

    [Fact]
    public void AddSuppressionRule_ShouldNotSuppressCriticalAlerts()
    {
        var svc = CreateService();
        var cat = "NoCritSupp-" + Guid.NewGuid();

        svc.AddSuppressionRule(cat, null, TimeSpan.FromHours(1));

        var alert = svc.RaiseAlert("CriticalAlert-" + Guid.NewGuid(), "desc",
            AlertSeverity.Critical, BusinessImpact.Critical, cat);

        alert.IsSuppressed.Should().BeFalse();
    }

    // ── GetGroupedAlerts ─────────────────────────────────────────────

    [Fact]
    public void GetGroupedAlerts_ShouldExcludeSnoozedAlerts()
    {
        var svc = CreateService();
        var cat = "GroupSnooze-" + Guid.NewGuid();

        var alert = svc.RaiseAlert("GroupSnz-" + Guid.NewGuid(), "desc",
            AlertSeverity.Warning, BusinessImpact.Low, cat);

        svc.SnoozeAlert(alert.Id, TimeSpan.FromHours(1));

        var groups = svc.GetGroupedAlerts();
        groups.Should().NotContain(g => g.Category == cat);
    }

    [Fact]
    public void GetGroupedAlerts_ShouldOrderBySeverityDescending()
    {
        var svc = CreateService();
        var suffix = Guid.NewGuid().ToString();

        svc.RaiseAlert("Low-" + suffix, "d", AlertSeverity.Info, BusinessImpact.None, "Ord-" + suffix);
        svc.RaiseAlert("High-" + suffix, "d", AlertSeverity.Error, BusinessImpact.High, "OrdH-" + suffix);

        var groups = svc.GetGroupedAlerts();
        if (groups.Count >= 2)
        {
            ((int)groups[0].Severity).Should().BeGreaterThanOrEqualTo((int)groups[1].Severity);
        }
    }

    // ── GetSummary ───────────────────────────────────────────────────

    [Fact]
    public void GetSummary_ShouldReturnValidCounts()
    {
        var svc = CreateService();
        var summary = svc.GetSummary();

        summary.Should().NotBeNull();
        summary.TotalActive.Should().BeGreaterThanOrEqualTo(0);
        summary.CriticalCount.Should().BeGreaterThanOrEqualTo(0);
        summary.ErrorCount.Should().BeGreaterThanOrEqualTo(0);
        summary.WarningCount.Should().BeGreaterThanOrEqualTo(0);
        summary.InfoCount.Should().BeGreaterThanOrEqualTo(0);
    }

    // ── RegisterPlaybook ─────────────────────────────────────────────

    [Fact]
    public void RegisterPlaybook_ShouldAutoAttachByCategory()
    {
        var svc = CreateService();
        // Default playbooks are registered in constructor — "Connection" category
        var alert = svc.RaiseAlert("ConnTest-" + Guid.NewGuid(), "desc",
            AlertSeverity.Warning, BusinessImpact.Medium, "Connection");

        alert.Playbook.Should().NotBeNull();
        alert.Playbook!.Title.Should().Contain("Connection");
    }

    // ── Data model tests ─────────────────────────────────────────────

    [Fact]
    public void Alert_ShouldHaveDefaultValues()
    {
        var alert = new Alert();
        alert.Id.Should().BeEmpty();
        alert.Title.Should().BeEmpty();
        alert.AffectedResources.Should().NotBeNull();
        alert.AffectedResources.Should().BeEmpty();
        alert.IsResolved.Should().BeFalse();
        alert.IsSnoozed.Should().BeFalse();
        alert.IsSuppressed.Should().BeFalse();
    }

    [Fact]
    public void AlertGroup_ShouldHaveDefaultValues()
    {
        var group = new AlertGroup();
        group.Category.Should().BeEmpty();
        group.Title.Should().BeEmpty();
        group.AffectedResources.Should().NotBeNull();
        group.Count.Should().Be(0);
    }

    [Fact]
    public void AlertSummary_ShouldHaveDefaultValues()
    {
        var summary = new AlertSummary();
        summary.CriticalCount.Should().Be(0);
        summary.ErrorCount.Should().Be(0);
        summary.WarningCount.Should().Be(0);
        summary.InfoCount.Should().Be(0);
        summary.SnoozedCount.Should().Be(0);
        summary.SuppressedCount.Should().Be(0);
        summary.TotalActive.Should().Be(0);
    }

    [Fact]
    public void RemediationStep_ShouldStoreValues()
    {
        var step = new RemediationStep(1, "Check logs", "Review recent log entries");
        step.Priority.Should().Be(1);
        step.Title.Should().Be("Check logs");
        step.Description.Should().Be("Review recent log entries");
    }

    [Fact]
    public void AlertPlaybook_ShouldHaveDefaultValues()
    {
        var playbook = new AlertPlaybook();
        playbook.Title.Should().BeEmpty();
        playbook.Categories.Should().BeEmpty();
        playbook.PossibleCauses.Should().BeEmpty();
        playbook.RemediationSteps.Should().BeEmpty();
    }

    [Theory]
    [InlineData(AlertSeverity.Info)]
    [InlineData(AlertSeverity.Warning)]
    [InlineData(AlertSeverity.Error)]
    [InlineData(AlertSeverity.Critical)]
    [InlineData(AlertSeverity.Emergency)]
    public void AlertSeverity_AllValues_ShouldBeDefined(AlertSeverity severity)
    {
        Enum.IsDefined(typeof(AlertSeverity), severity).Should().BeTrue();
    }

    [Theory]
    [InlineData(BusinessImpact.None)]
    [InlineData(BusinessImpact.Low)]
    [InlineData(BusinessImpact.Medium)]
    [InlineData(BusinessImpact.High)]
    [InlineData(BusinessImpact.Critical)]
    public void BusinessImpact_AllValues_ShouldBeDefined(BusinessImpact impact)
    {
        Enum.IsDefined(typeof(BusinessImpact), impact).Should().BeTrue();
    }
}
