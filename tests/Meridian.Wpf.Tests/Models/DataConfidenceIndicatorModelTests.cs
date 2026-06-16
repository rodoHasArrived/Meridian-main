#if WINDOWS
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Tests.Models;

public sealed class DataConfidenceIndicatorModelTests
{
    [Fact]
    public void FromEvidence_WithReadyFreshEvidence_UsesCurrentReconciledLabels()
    {
        var asOf = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);

        var model = DataConfidenceIndicatorModel.FromEvidence(
            EvidenceStatusDto.Ready,
            new EvidenceFreshnessDto(asOf, IsStale: false, Reason: null),
            "Portfolio ledger");

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Current);
        model.ReconciliationLabel.Should().Be(DataConfidenceLabels.Reconciled);
        model.FreshnessLabel.Should().Be("2026-06-15 12:30 UTC");
        model.ProviderLabel.Should().Be("Portfolio ledger");
        model.Tone.Should().Be(WorkspaceTone.Success);
        model.AccessibleExplanation.Should().Contain("Current · Reconciled");
    }

    [Fact]
    public void FromEvidence_WithReviewRequiredStaleEvidence_SurfacesPartialUnreconciledNotes()
    {
        var model = DataConfidenceIndicatorModel.FromEvidence(
            EvidenceStatusDto.ReviewRequired,
            new EvidenceFreshnessDto(new DateTimeOffset(2026, 6, 14, 9, 0, 0, TimeSpan.Zero), IsStale: true, Reason: "Source file is older than policy."),
            "Accounting import");

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Partial);
        model.ReconciliationLabel.Should().Be(DataConfidenceLabels.Unreconciled);
        model.Notes.Should().Be("Source file is older than policy.");
        model.Tone.Should().Be(WorkspaceTone.Warning);
    }

    [Fact]
    public void FromProviderStatus_WithDegradedProvider_UsesProviderDegradedLabelAndStatus()
    {
        var model = DataConfidenceIndicatorModel.FromProviderStatus(new ProviderStatusInfo
        {
            Name = "ibkr",
            DisplayName = "Interactive Brokers",
            IsEnabled = true,
            IsConnected = true,
            Status = "Degraded",
            LastMessageReceivedAt = new DateTimeOffset(2026, 6, 15, 10, 5, 0, TimeSpan.Zero),
            LastFailureKind = "Heartbeat timeout"
        });

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.ProviderDegraded);
        model.ProviderLabel.Should().Be("Interactive Brokers · Degraded");
        model.Notes.Should().Be("Heartbeat timeout");
        model.Tone.Should().Be(WorkspaceTone.Warning);
    }

    [Fact]
    public void Unknown_UsesExplicitUnavailableState()
    {
        var model = DataConfidenceIndicatorModel.Unknown();

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Unknown);
        model.ReconciliationLabel.Should().Be(DataConfidenceLabels.Unknown);
        model.FreshnessLabel.Should().Be("As of unavailable");
        model.Tone.Should().Be(WorkspaceTone.Neutral);
    }
}
#endif
