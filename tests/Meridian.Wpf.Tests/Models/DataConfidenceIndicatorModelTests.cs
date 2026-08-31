#if WINDOWS
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Tests.Models;

public sealed class DataConfidenceIndicatorModelTests
{
    [Fact]
    public void FromEvidence_WithReadyFreshEvidenceAndSuppliedReconciliation_UsesCurrentReconciledLabels()
    {
        var asOf = new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero);

        var model = DataConfidenceIndicatorModel.FromEvidence(
            EvidenceStatusDto.Ready,
            new EvidenceFreshnessDto(asOf, IsStale: false, Reason: null),
            "Portfolio ledger",
            reconciliationStatus: DataConfidenceReconciliationStatus.Reconciled);

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Current);
        model.ReconciliationLabel.Should().Be(DataConfidenceLabels.Reconciled);
        model.FreshnessLabel.Should().Be("2026-06-15 12:30 UTC");
        model.ProviderLabel.Should().Be("Portfolio ledger");
        model.Tone.Should().Be(WorkspaceTone.Success);
        model.AccessibleExplanation.Should().Contain("Current · Reconciled");
    }

    [Fact]
    public void FromEvidence_WithoutASuppliedReconciliation_DoesNotClaimReconciled()
    {
        // EvidenceStatusDto describes evidence readiness only; a Ready delivery package or
        // provider artifact is not a reconciliation result.
        var model = DataConfidenceIndicatorModel.FromEvidence(
            EvidenceStatusDto.Ready,
            new EvidenceFreshnessDto(new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero), IsStale: false, Reason: null),
            "Delivery package");

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Current);
        model.ReconciliationLabel.Should().Be(DataConfidenceLabels.Unknown);
        model.Tone.Should().Be(WorkspaceTone.Neutral);
    }

    [Fact]
    public void FromEvidence_ReadyWithoutAnAsOfInstant_StaysUnknownInsteadOfCurrent()
    {
        // The shared DTO permits a missing timestamp; "Current · As of unavailable" would
        // contradict itself.
        var model = DataConfidenceIndicatorModel.FromEvidence(
            EvidenceStatusDto.Ready,
            new EvidenceFreshnessDto(AsOf: null, IsStale: false, Reason: null),
            "Portfolio ledger");

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Unknown);
        model.FreshnessLabel.Should().Be("As of unavailable");
    }

    [Fact]
    public void FromEvidence_WithReviewRequiredStaleEvidence_SurfacesPartialAndNotes()
    {
        var model = DataConfidenceIndicatorModel.FromEvidence(
            EvidenceStatusDto.ReviewRequired,
            new EvidenceFreshnessDto(new DateTimeOffset(2026, 6, 14, 9, 0, 0, TimeSpan.Zero), IsStale: true, Reason: "Source file is older than policy."),
            "Accounting import");

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Partial);
        model.ReconciliationLabel.Should().Be(DataConfidenceLabels.Unknown);
        model.Notes.Should().Be("Source file is older than policy.");
        model.Tone.Should().Be(WorkspaceTone.Warning);
    }

    [Fact]
    public void FromEvidence_BlockedEvidence_ReadsAsBlockedWithDangerTone()
    {
        // Blocked evidence is a hard failure (rejected approval, failed delivery); the badge
        // must present it as a danger state distinct from routine review, matching the
        // workstation's existing evidence presentation.
        var model = DataConfidenceIndicatorModel.FromEvidence(
            EvidenceStatusDto.Blocked,
            new EvidenceFreshnessDto(new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero), IsStale: false, Reason: null),
            "Report delivery",
            notes: "Delivery to the administrator failed.");

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Blocked);
        model.Tone.Should().Be(WorkspaceTone.Danger);
        model.AccessibleExplanation.Should().Contain(DataConfidenceLabels.Blocked);
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
    public void FromProviderStatus_ConnectedWithoutAnyFreshnessSignal_DoesNotClaimCurrent()
    {
        // A connected socket is not proof of current data.
        var model = DataConfidenceIndicatorModel.FromProviderStatus(new ProviderStatusInfo
        {
            Name = "polygon",
            DisplayName = "Polygon.io",
            IsEnabled = true,
            IsConnected = true,
            Status = "Connected"
        });

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Unknown);
        model.FreshnessLabel.Should().Be("As of unavailable");
    }

    [Fact]
    public void FromProviderStatus_ConnectionTimeAlone_IsNotAFreshnessSignal()
    {
        // A provider that just connected but has never delivered a message or heartbeat
        // must not present as Current on the strength of its connection time.
        var model = DataConfidenceIndicatorModel.FromProviderStatus(new ProviderStatusInfo
        {
            Name = "polygon",
            DisplayName = "Polygon.io",
            IsEnabled = true,
            IsConnected = true,
            Status = "Connected",
            LastConnectedAt = DateTime.UtcNow
        });

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Unknown);
        model.FreshnessLabel.Should().Be("As of unavailable");
    }

    [Fact]
    public void FromProviderStatus_WithAFreshnessWindow_MarksOldDataStaleWhileConnected()
    {
        var model = DataConfidenceIndicatorModel.FromProviderStatus(
            new ProviderStatusInfo
            {
                Name = "polygon",
                DisplayName = "Polygon.io",
                IsEnabled = true,
                IsConnected = true,
                Status = "Connected",
                LastMessageReceivedAt = DateTimeOffset.UtcNow.AddHours(-6)
            },
            freshnessWindow: TimeSpan.FromMinutes(15));

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Stale);
        model.Tone.Should().Be(WorkspaceTone.Warning);
    }

    [Fact]
    public void FromProviderStatus_WithTheSharedRouteContract_MapsConnectionStateAndFreshness()
    {
        var lastMessage = new DateTimeOffset(2026, 6, 15, 10, 5, 0, TimeSpan.Zero);
        var model = DataConfidenceIndicatorModel.FromProviderStatus(
            new Meridian.Contracts.Api.ProviderStatusResponse(
                ProviderId: "polygon",
                Name: "Polygon.io",
                ProviderType: "MarketData",
                IsConnected: true,
                IsEnabled: true,
                Priority: 1,
                ActiveSubscriptions: 3,
                LastHeartbeat: lastMessage,
                ConnectionState: "Streaming",
                LastMessageReceivedAt: lastMessage),
            reconciliationStatus: DataConfidenceReconciliationStatus.Reconciled);

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Current);
        model.ProviderLabel.Should().Be("Polygon.io · Streaming");
        model.FreshnessLabel.Should().Be("2026-06-15 10:05 UTC");
        model.Tone.Should().Be(WorkspaceTone.Success);
    }

    [Fact]
    public void FromProviderStatus_RecoveringSubscriptionsWithoutFailures_ReadAsDegraded()
    {
        // The contract reports Recovering separately from Failed: a connected provider with
        // recent traffic but a stream mid-recovery must not present its data as Current.
        var model = DataConfidenceIndicatorModel.FromProviderStatus(
            new Meridian.Contracts.Api.ProviderStatusResponse(
                ProviderId: "polygon",
                Name: "Polygon.io",
                ProviderType: "MarketData",
                IsConnected: true,
                IsEnabled: true,
                Priority: 1,
                ActiveSubscriptions: 3,
                LastHeartbeat: null,
                ConnectionState: "Streaming",
                LastMessageReceivedAt: DateTimeOffset.UtcNow,
                FailedSubscriptions: 0,
                RecoveringSubscriptions: 1));

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.ProviderDegraded);
        model.Tone.Should().Be(WorkspaceTone.Warning);
    }

    [Fact]
    public void FromProviderStatus_ReconnectingSharedContractProvider_ReadsAsDegraded()
    {
        // The supervisor can publish a connected snapshot while a reconnect is still in
        // progress, and the provider-health endpoint classifies that flag as yellow — the
        // badge must not present the same state as Current.
        var model = DataConfidenceIndicatorModel.FromProviderStatus(
            new Meridian.Contracts.Api.ProviderStatusResponse(
                ProviderId: "polygon",
                Name: "Polygon.io",
                ProviderType: "MarketData",
                IsConnected: true,
                IsEnabled: true,
                Priority: 1,
                ActiveSubscriptions: 3,
                LastHeartbeat: null,
                ConnectionState: "Streaming",
                IsReconnecting: true,
                LastMessageReceivedAt: DateTimeOffset.UtcNow));

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.ProviderDegraded);
        model.Tone.Should().Be(WorkspaceTone.Warning);
    }

    [Fact]
    public void FromProviderStatus_ControlTrafficDoesNotMaskSubscriptionStaleness()
    {
        // The transport advances LastMessageReceivedAt for control frames too, so fresh
        // wire traffic must not present stale subscribed data as Current; the contract's
        // LastSubscriptionMessageAt is authoritative when the provider tracks it.
        var model = DataConfidenceIndicatorModel.FromProviderStatus(
            new Meridian.Contracts.Api.ProviderStatusResponse(
                ProviderId: "polygon",
                Name: "Polygon.io",
                ProviderType: "MarketData",
                IsConnected: true,
                IsEnabled: true,
                Priority: 1,
                ActiveSubscriptions: 3,
                LastHeartbeat: null,
                ConnectionState: "Streaming",
                LastMessageReceivedAt: DateTimeOffset.UtcNow,
                LastSubscriptionMessageAt: DateTimeOffset.UtcNow.AddHours(-6)),
            freshnessWindow: TimeSpan.FromMinutes(15));

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Stale);
        model.Tone.Should().Be(WorkspaceTone.Warning);
    }

    [Fact]
    public void WithUpdatedFields_TheExplanationDescribesTheCurrentValues()
    {
        // Explanation is computed from the record's current fields: a `with` update must
        // never leave the tooltip and accessible text describing the replaced values.
        var updated = DataConfidenceIndicatorModel.Unknown() with
        {
            ConfidenceLevel = DataConfidenceLevel.Current,
            Notes = "Feed restored."
        };

        updated.Explanation.Should().Contain(DataConfidenceLabels.Current);
        updated.Explanation.Should().Contain("Feed restored.");
        updated.Explanation.Should().NotContain(DataConfidenceLabels.Unknown + " value",
            "the pre-update confidence level must not survive in the explanation");
    }

    [Fact]
    public void FromProviderStatus_ReconnectingLegacyProvider_ReadsAsDegraded()
    {
        var model = DataConfidenceIndicatorModel.FromProviderStatus(new ProviderStatusInfo
        {
            Name = "polygon",
            DisplayName = "Polygon.io",
            IsEnabled = true,
            IsConnected = true,
            Status = "Connected",
            IsReconnecting = true,
            LastMessageReceivedAt = DateTimeOffset.UtcNow
        });

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.ProviderDegraded);
        model.Tone.Should().Be(WorkspaceTone.Warning);
    }

    [Fact]
    public void FromProviderStatus_StreamMarkedDegradedByTheContract_ReadsAsDegraded()
    {
        // The contract carries per-stream degradation the aggregate counters may not
        // reflect; a stream the route itself marks degraded is a partial degradation.
        var model = DataConfidenceIndicatorModel.FromProviderStatus(
            new Meridian.Contracts.Api.ProviderStatusResponse(
                ProviderId: "polygon",
                Name: "Polygon.io",
                ProviderType: "MarketData",
                IsConnected: true,
                IsEnabled: true,
                Priority: 1,
                ActiveSubscriptions: 3,
                LastHeartbeat: null,
                ConnectionState: "Streaming",
                LastMessageReceivedAt: DateTimeOffset.UtcNow,
                Streams:
                [
                    new Meridian.Contracts.Api.ProviderStreamStatusResponse(
                        AssetClass: "us_equity",
                        Feed: "sip",
                        Entitlement: "delayed",
                        LifecycleState: "Streaming",
                        IsConnected: true,
                        IsDegraded: true,
                        DegradationReason: "Entitlement downgraded to delayed data.")
                ]));

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.ProviderDegraded);
        model.Tone.Should().Be(WorkspaceTone.Warning);
        // The stream's reason is the sole cause of the warning, so the notes, tooltip, and
        // accessible explanation must carry it instead of a generic lifecycle value.
        model.Notes.Should().Be("Entitlement downgraded to delayed data.");
        model.AccessibleExplanation.Should().Contain("Entitlement downgraded to delayed data.");
    }

    [Fact]
    public void FromEvidence_BlankSource_UsesTheSameFallbackInLabelAndExplanation()
    {
        var model = DataConfidenceIndicatorModel.FromEvidence(
            EvidenceStatusDto.Ready,
            new EvidenceFreshnessDto(new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero), IsStale: false, Reason: null),
            sourceSystem: "  ");

        model.ProviderLabel.Should().Be("Evidence");
        model.Explanation.Should().Contain("Evidence");
        model.Explanation.Should().NotContain("source not reported",
            "the tooltip must not contradict the visible provider label");
    }

    [Fact]
    public void FromProviderStatus_MetricsSnapshotTimeAlone_IsNotAFreshnessSignal()
    {
        // The route populates LastHeartbeat from a stored metrics-snapshot timestamp when
        // it has no live diagnostics; a snapshot time is not evidence that data arrived.
        var model = DataConfidenceIndicatorModel.FromProviderStatus(
            new Meridian.Contracts.Api.ProviderStatusResponse(
                ProviderId: "polygon",
                Name: "Polygon.io",
                ProviderType: "MarketData",
                IsConnected: true,
                IsEnabled: true,
                Priority: 1,
                ActiveSubscriptions: 0,
                LastHeartbeat: DateTimeOffset.UtcNow,
                ConnectionState: "Connected"));

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Unknown);
        model.FreshnessLabel.Should().Be("As of unavailable");
    }

    [Fact]
    public void FromProviderStatus_UnknownConnectivityFromTheSharedContract_IsNotDegraded()
    {
        // The route deliberately emits IsConnected = null with ConnectionState "unknown"
        // when it has neither runtime diagnostics nor stored metrics; unavailable health
        // must stay Unknown rather than read as Provider Degraded.
        var model = DataConfidenceIndicatorModel.FromProviderStatus(
            new Meridian.Contracts.Api.ProviderStatusResponse(
                ProviderId: "polygon",
                Name: "Polygon.io",
                ProviderType: "MarketData",
                IsConnected: null,
                IsEnabled: true,
                Priority: 1,
                ActiveSubscriptions: 0,
                LastHeartbeat: null,
                ConnectionState: "unknown"));

        model.ConfidenceLabel.Should().Be(DataConfidenceLabels.Unknown);
        model.ProviderLabel.Should().Be("Polygon.io · unknown");
    }

    [Fact]
    public void FromProviderStatus_ExplanationUsesTheNameFallbackWhenDisplayNameIsBlank()
    {
        var model = DataConfidenceIndicatorModel.FromProviderStatus(new ProviderStatusInfo
        {
            Name = "ibkr",
            DisplayName = " ",
            IsEnabled = true,
            IsConnected = true,
            Status = "Connected",
            LastMessageReceivedAt = new DateTimeOffset(2026, 6, 15, 10, 5, 0, TimeSpan.Zero)
        });

        model.ProviderLabel.Should().StartWith("ibkr");
        model.Explanation.Should().Contain("ibkr", "the tooltip and the visible provider label must not contradict each other");
        model.Explanation.Should().NotContain("source not reported");
    }

    [Fact]
    public void Tone_GivesUnreconciledWarningPrecedenceOverCurrent()
    {
        var model = DataConfidenceIndicatorModel.Unknown() with
        {
            ConfidenceLevel = DataConfidenceLevel.Current,
            ReconciliationStatus = DataConfidenceReconciliationStatus.Unreconciled
        };

        model.Tone.Should().Be(WorkspaceTone.Warning, "a reconciliation exception must not be visually suppressed by a Current badge");
    }

    [Fact]
    public void FreshnessLabel_ConvertsOffsetTimestampsToUtcBeforeLabelingThemUtc()
    {
        var model = DataConfidenceIndicatorModel.Unknown() with
        {
            FreshnessTimestamp = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.FromHours(2))
        };

        model.FreshnessLabel.Should().Be("2026-06-15 10:00 UTC");
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

    [Fact]
    public void Unknown_WithANote_CarriesTheNoteIntoTheExplanation()
    {
        var model = DataConfidenceIndicatorModel.Unknown(notes: "Valuation feed offline.");

        model.Explanation.Should().Contain("Valuation feed offline.");
        model.AccessibleExplanation.Should().Contain("Valuation feed offline.");
    }
}
#endif
