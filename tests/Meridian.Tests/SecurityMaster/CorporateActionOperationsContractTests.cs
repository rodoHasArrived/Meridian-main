using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

public sealed class CorporateActionOperationsContractTests
{
    private static readonly Guid SecurityId = Guid.Parse("4e685849-9caf-4b02-9ef2-641317161ba1");
    private static readonly DateOnly ExDate = new(2026, 8, 14);

    [Fact]
    public void EconomicFingerprint_DifferentSameDayActions_RemainDistinct()
    {
        var first = Dividend(amount: 0.24m, corporateActionId: Guid.NewGuid());
        var second = Dividend(amount: 0.26m, corporateActionId: Guid.NewGuid());

        CorporateActionEconomicFingerprint.Compute(first)
            .Should().NotBe(CorporateActionEconomicFingerprint.Compute(second),
                "economic fingerprinting must not collapse actions merely because security, type, and ex-date match");
    }

    [Fact]
    public void EconomicFingerprint_SameEconomics_IsProviderAndAppendIdentityIndependent()
    {
        var first = Dividend(
            amount: 0.24m,
            corporateActionId: Guid.NewGuid(),
            payload: JsonSerializer.SerializeToElement(new { memo = "quarterly", sequence = 7 }));
        var second = Dividend(
            amount: 0.24m,
            corporateActionId: Guid.NewGuid(),
            currency: " usd ",
            payload: JsonSerializer.SerializeToElement(new { sequence = 7, memo = "quarterly" }));

        CorporateActionEconomicFingerprint.Compute(first)
            .Should().Be(CorporateActionEconomicFingerprint.Compute(second),
                "append identity, normalized currency casing, and JSON object property order are not economic differences");
    }

    [Fact]
    public void EconomicFingerprint_SchemaEquivalentPayloadLexicalForms_Converge()
    {
        var expiry = new DateOnly(2026, 9, 1);
        var first = Dividend(0.24m, Guid.NewGuid()) with
        {
            EventType = CorporateActionEventTypes.TenderOffer,
            Payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                [CorporateActionPayloads.OfferPricePerShare] = "10.5000",
                [CorporateActionPayloads.OfferExpiryDate] = "09/01/2026",
                [CorporateActionPayloads.IsPartialTender] = "TRUE",
            }),
        };
        var second = first with
        {
            CorpActId = Guid.NewGuid(),
            Payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                [CorporateActionPayloads.IsPartialTender] = true,
                [CorporateActionPayloads.OfferExpiryDate] = expiry.ToString("yyyy-MM-dd"),
                [CorporateActionPayloads.OfferPricePerShare] = 10.5m,
            }),
        };

        CorporateActionEconomicFingerprint.Compute(first)
            .Should().Be(CorporateActionEconomicFingerprint.Compute(second));

        var materiallyDifferent = second with
        {
            Payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                [CorporateActionPayloads.IsPartialTender] = true,
                [CorporateActionPayloads.OfferExpiryDate] = expiry.ToString("yyyy-MM-dd"),
                [CorporateActionPayloads.OfferPricePerShare] = 10.51m,
            }),
        };
        CorporateActionEconomicFingerprint.Compute(first)
            .Should().NotBe(CorporateActionEconomicFingerprint.Compute(materiallyDifferent));
    }

    [Fact]
    public void ProviderEventIdentity_IsSeparateFromEconomicFingerprint()
    {
        var action = Dividend(amount: 0.24m, corporateActionId: Guid.NewGuid());
        var fingerprint = CorporateActionEconomicFingerprint.Compute(action);
        var observedAt = new DateTimeOffset(2026, 8, 12, 15, 30, 0, TimeSpan.Zero);

        var first = new CorporateActionSourceProposalDto(
            Guid.NewGuid(), SecurityId,
            new CorporateActionProviderEventIdentityDto("provider-a", "event-100", "v1", observedAt),
            action, action.PayloadSchemaVersion, fingerprint, CorporateActionSourceProposalStates.Observed,
            1, null, null, null, "ingest", observedAt, observedAt);
        var replayFromAnotherSource = first with
        {
            ProposalId = Guid.NewGuid(),
            ProviderIdentity = new CorporateActionProviderEventIdentityDto(
                "provider-b", "event-900", "2026-08-12", observedAt),
        };

        first.EconomicFingerprint.Should().Be(replayFromAnotherSource.EconomicFingerprint);
        first.ProviderIdentity.Should().NotBe(replayFromAnotherSource.ProviderIdentity,
            "provider event/version is the primary replay identity and cannot be replaced by the economic fingerprint");
    }

    [Fact]
    public void SourceReplayEquality_RejectsChangedEvidenceUnderSameProviderIdentity()
    {
        var action = Dividend(amount: 0.24m, corporateActionId: Guid.NewGuid());
        var fingerprint = CorporateActionEconomicFingerprint.Compute(action);
        var original = new CorporateActionSourceProposalDto(
            Guid.NewGuid(),
            SecurityId,
            new CorporateActionProviderEventIdentityDto(
                "provider-a",
                "event-100",
                "v1",
                new DateTimeOffset(2026, 8, 12, 15, 30, 0, TimeSpan.Zero),
                EvidenceHash: "sha256:original",
                EvidenceReference: "provider://event-100/v1/raw"),
            action,
            action.PayloadSchemaVersion,
            fingerprint,
            CorporateActionSourceProposalStates.Observed,
            1,
            null,
            null,
            null,
            "ingest",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(
                original,
                original with { ProposalId = Guid.NewGuid() })
            .Should().BeTrue();
        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(
                original,
                original with
                {
                    ProposalId = Guid.NewGuid(),
                    ProviderIdentity = original.ProviderIdentity with { EvidenceHash = "sha256:changed" },
                })
            .Should().BeFalse();
        CorporateActionSourceProposalReplayComparer.HasSameSourcePayload(
                original,
                original with
                {
                    ProposalId = Guid.NewGuid(),
                    ProviderIdentity = original.ProviderIdentity with
                    {
                        EvidenceReference = "provider://event-100/v1/replaced",
                    },
                })
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("provider-event://corporate-actions/alpaca/event-1/v1")]
    [InlineData("provider://event-1/v1")]
    [InlineData("urn:sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void EvidenceReference_ApprovedProviderAndUrnSchemesRoundTrip(string reference)
    {
        CorporateActionEvidenceKinds.IsTrustedReference(reference).Should().BeTrue();
        CorporateActionEvidenceKinds.IsTrustedReference("arbitrary://not-retained/reference")
            .Should().BeFalse();
    }

    [Fact]
    public void DissentEvidence_RequiresDifferingValuesAndTypedEvidencePerSource()
    {
        var complete = new CorporateActionSourceDisplayMetadataDto(
            "ACME",
            "provider-a",
            ["provider-a"],
            ["provider-b"],
            [
                new CorporateActionDissentFieldDto(
                    nameof(CorporateActionDto.DividendPerShare),
                    [
                        new CorporateActionConflictCandidateDto(
                            "provider-a",
                            JsonSerializer.SerializeToElement(0.24m),
                            "provider-event://provider-a/event-100/v1"),
                        new CorporateActionConflictCandidateDto(
                            "provider-b",
                            JsonSerializer.SerializeToElement(0.26m),
                            "provider-event://provider-b/event-200/v1"),
                    ]),
            ]);

        CorporateActionDissentEvidencePolicy.HasCompleteFieldCandidates(complete, "provider-a")
            .Should().BeTrue();
        CorporateActionDissentEvidencePolicy.HasCompleteFieldCandidates(
                complete with { DissentingFields = [] },
                "provider-a")
            .Should().BeFalse("provider names alone are not a recoverable field-level conflict");
        var completeField = complete.DissentingFields![0];
        CorporateActionDissentEvidencePolicy.HasCompleteFieldCandidates(
                complete with
                {
                    DissentingFields =
                    [
                        completeField with
                        {
                            Candidates =
                            [
                                completeField.Candidates[0],
                                completeField.Candidates[1] with
                                {
                                    EvidenceReference = null,
                                },
                            ],
                        },
                    ],
                },
                "provider-a")
            .Should().BeFalse();
    }

    [Fact]
    public void LifecyclePolicy_RestatementHandoff_ReturnsToAccountingReview()
    {
        CorporateActionCaseTransitionPolicy.CanTransition(
                CorporateActionCaseStates.AccountingReview,
                CorporateActionCaseStates.RestatementRequired)
            .Should().BeTrue();
        CorporateActionCaseTransitionPolicy.CanTransition(
                CorporateActionCaseStates.ReadyForApproval,
                CorporateActionCaseStates.RestatementRequired)
            .Should().BeTrue();
        CorporateActionCaseTransitionPolicy.CanTransition(
                CorporateActionCaseStates.RestatementRequired,
                CorporateActionCaseStates.AccountingReview)
            .Should().BeTrue();
        CorporateActionCaseTransitionPolicy.CanTransition(
                CorporateActionCaseStates.Closed,
                CorporateActionCaseStates.RestatementRequired)
            .Should().BeTrue("closed cases may enter the governed correction/restatement lane");
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.ReadyForApproval)]
    [InlineData(CorporateActionCaseStates.Approved)]
    [InlineData(CorporateActionCaseStates.Scheduled)]
    [InlineData(CorporateActionCaseStates.Posted)]
    [InlineData(CorporateActionCaseStates.Reconciled)]
    [InlineData(CorporateActionCaseStates.Reported)]
    [InlineData(CorporateActionCaseStates.Closed)]
    [InlineData(CorporateActionCaseStates.Cancelled)]
    [InlineData(CorporateActionCaseStates.Superseded)]
    public void LifecyclePolicy_ContentFrozenStatesRequireGovernedReopen(string state)
    {
        CorporateActionCaseStates.IsContentFrozen(state).Should().BeTrue();
        CorporateActionCaseStates.IsContentFrozen(CorporateActionCaseStates.RestatementRequired)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.Detected, CorporateActionCaseStates.TermsConfirmed)]
    [InlineData(CorporateActionCaseStates.NeedsTerms, CorporateActionCaseStates.TermsConfirmed)]
    [InlineData(CorporateActionCaseStates.Disputed, CorporateActionCaseStates.TermsConfirmed)]
    [InlineData(CorporateActionCaseStates.TermsConfirmed, CorporateActionCaseStates.ElectionPending)]
    [InlineData(CorporateActionCaseStates.TermsConfirmed, CorporateActionCaseStates.AllocationPending)]
    [InlineData(CorporateActionCaseStates.TermsConfirmed, CorporateActionCaseStates.AccountingReview)]
    [InlineData(CorporateActionCaseStates.ElectionPending, CorporateActionCaseStates.ElectionSubmitted)]
    [InlineData(CorporateActionCaseStates.ElectionSubmitted, CorporateActionCaseStates.AllocationPending)]
    [InlineData(CorporateActionCaseStates.ElectionSubmitted, CorporateActionCaseStates.AccountingReview)]
    [InlineData(CorporateActionCaseStates.AllocationPending, CorporateActionCaseStates.AccountingReview)]
    [InlineData(CorporateActionCaseStates.AccountingReview, CorporateActionCaseStates.ReadyForApproval)]
    [InlineData(CorporateActionCaseStates.Blocked, CorporateActionCaseStates.AccountingReview)]
    [InlineData(CorporateActionCaseStates.RestatementRequired, CorporateActionCaseStates.AccountingReview)]
    public void LifecyclePolicy_TermsDependentTransitionsRequireZeroOpenConflicts(
        string fromState,
        string toState)
    {
        CorporateActionCaseTransitionPolicy.CanTransition(fromState, toState).Should().BeTrue();
        CorporateActionCaseTransitionPolicy.RequiresConflictFreeTerms(fromState, toState)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.TermsConfirmed)]
    [InlineData(CorporateActionCaseStates.ElectionPending)]
    [InlineData(CorporateActionCaseStates.ElectionSubmitted)]
    [InlineData(CorporateActionCaseStates.AllocationPending)]
    [InlineData(CorporateActionCaseStates.AccountingReview)]
    [InlineData(CorporateActionCaseStates.ReadyForApproval)]
    [InlineData(CorporateActionCaseStates.Approved)]
    [InlineData(CorporateActionCaseStates.Scheduled)]
    [InlineData(CorporateActionCaseStates.Posted)]
    [InlineData(CorporateActionCaseStates.Reconciled)]
    [InlineData(CorporateActionCaseStates.Reported)]
    [InlineData(CorporateActionCaseStates.Closed)]
    public void LifecyclePolicy_PostConfirmationStatesRejectNewConflicts(string state)
    {
        CorporateActionCaseStates.PresupposesConfirmedTerms(state).Should().BeTrue();
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.Blocked)]
    [InlineData(CorporateActionCaseStates.RestatementRequired)]
    [InlineData(CorporateActionCaseStates.Disputed)]
    [InlineData(CorporateActionCaseStates.NeedsTerms)]
    public void LifecyclePolicy_GovernedCorrectionStatesMayRetainConflicts(string state)
    {
        CorporateActionCaseStates.PresupposesConfirmedTerms(state).Should().BeFalse();
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.TermsConfirmed)]
    [InlineData(CorporateActionCaseStates.ElectionPending)]
    [InlineData(CorporateActionCaseStates.ElectionSubmitted)]
    [InlineData(CorporateActionCaseStates.AllocationPending)]
    [InlineData(CorporateActionCaseStates.AccountingReview)]
    public void StorePolicy_RejectsPostConfirmationConflictRecording(string state)
    {
        var processingCase = ProcessingCase(state);

        var act = () => PostgresCorporateActionOperationsStore
            .EnsureConflictCanBeRecorded(processingCase);

        var exception = act.Should().Throw<CorporateActionSourceConflictException>().Which;
        exception.Code.Should().Be(CorporateActionProblemCodes.SourceConflict);
        exception.Message.Should().Contain("governed");
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.Blocked)]
    [InlineData(CorporateActionCaseStates.RestatementRequired)]
    public void StorePolicy_AllowsConflictRecordingInsideGovernedCorrectionLane(string state)
    {
        var act = () => PostgresCorporateActionOperationsStore
            .EnsureConflictCanBeRecorded(ProcessingCase(state));

        act.Should().NotThrow();
    }

    [Fact]
    public void TransitionAuthority_DoesNotLeakAcrossOperatingLanes()
    {
        var electionOnly = Authority(canRecordElection: true);
        CorporateActionCaseTransitionAuthorization.IsAuthorized(
                CorporateActionCaseStates.TermsConfirmed,
                electionOnly,
                policyOverride: false,
                out var requiredForTerms)
            .Should().BeFalse();
        requiredForTerms.Should().Be(nameof(CorporateActionCaseTransitionAuthorityDto.CanResolveTerms));

        var termsOnly = Authority(canResolveTerms: true);
        CorporateActionCaseTransitionAuthorization.IsAuthorized(
                CorporateActionCaseStates.ReadyForApproval,
                termsOnly,
                policyOverride: false,
                out var requiredForApprovalReadiness)
            .Should().BeFalse();
        requiredForApprovalReadiness.Should().Be(
            nameof(CorporateActionCaseTransitionAuthorityDto.CanPrepareAccounting));

        var accountingOnly = Authority(canPrepareAccounting: true);
        CorporateActionCaseTransitionAuthorization.IsAuthorized(
                CorporateActionCaseStates.ElectionSubmitted,
                accountingOnly,
                policyOverride: false,
                out var requiredForElection)
            .Should().BeFalse();
        requiredForElection.Should().Be(
            nameof(CorporateActionCaseTransitionAuthorityDto.CanRecordElection));
    }

    [Fact]
    public void TransitionAuthority_ReopenAndPolicyOverrideRemainExplicitCapabilities()
    {
        CorporateActionCaseTransitionAuthorization.IsAuthorized(
                CorporateActionCaseStates.RestatementRequired,
                Authority(canReopenCase: true),
                policyOverride: false,
                out _)
            .Should().BeTrue();

        CorporateActionCaseTransitionAuthorization.IsAuthorized(
                CorporateActionCaseStates.TermsConfirmed,
                Authority(canResolveTerms: true),
                policyOverride: true,
                out var requiredForOverride)
            .Should().BeFalse();
        requiredForOverride.Should().Be(
            nameof(CorporateActionCaseTransitionAuthorityDto.CanOverridePolicy));

        CorporateActionCaseTransitionAuthorization.IsAuthorized(
                CorporateActionCaseStates.TermsConfirmed,
                Authority(canResolveTerms: true, canOverridePolicy: true),
                policyOverride: true,
                out _)
            .Should().BeTrue();

        CorporateActionCaseTransitionAuthorization.IsAuthorized(
                CorporateActionCaseStates.Blocked,
                Authority(canOverridePolicy: true),
                policyOverride: false,
                out _)
            .Should().BeFalse("policy override is additive and is not a standalone workflow lane");
        CorporateActionCaseTransitionAuthorization.IsAuthorized(
                CorporateActionCaseStates.Blocked,
                Authority(canReopenCase: true),
                policyOverride: false,
                out _)
            .Should().BeFalse("reopen authority applies only to the restatement-required transition");
    }

    [Theory]
    [InlineData(CorporateActionCaseStates.Approved)]
    [InlineData(CorporateActionCaseStates.Scheduled)]
    [InlineData(CorporateActionCaseStates.Posted)]
    [InlineData(CorporateActionCaseStates.Reconciled)]
    [InlineData(CorporateActionCaseStates.Reported)]
    [InlineData(CorporateActionCaseStates.Closed)]
    public void LifecyclePolicy_GenericPreparationCannotGrantDownstreamOutcome(string downstreamState)
    {
        CorporateActionCaseStates.RequiresDownstreamAuthority(downstreamState).Should().BeTrue();

        var preparationStates = new[]
        {
            CorporateActionCaseStates.Detected,
            CorporateActionCaseStates.NeedsTerms,
            CorporateActionCaseStates.Disputed,
            CorporateActionCaseStates.TermsConfirmed,
            CorporateActionCaseStates.ElectionPending,
            CorporateActionCaseStates.ElectionSubmitted,
            CorporateActionCaseStates.AllocationPending,
            CorporateActionCaseStates.AccountingReview,
            CorporateActionCaseStates.ReadyForApproval,
            CorporateActionCaseStates.Blocked,
            CorporateActionCaseStates.RestatementRequired,
        };

        preparationStates.Should().OnlyContain(state =>
            !CorporateActionCaseTransitionPolicy.CanTransition(state, downstreamState));
    }

    [Fact]
    public void DurableInboxContract_CarriesStrongAcceptanceIdentityVersionAndScope()
    {
        var entry = new CorporateActionDurableInboxEntryDto(
            SecurityId,
            "ACME",
            CorporateActionEventTypes.Dividend,
            ExDate,
            ExDate.AddDays(1),
            ExDate.AddDays(14),
            0.24m,
            "USD",
            null,
            null,
            "provider-a",
            ["provider-a", "provider-b"],
            [],
            AutoApplied: false,
            ProposalId: Guid.NewGuid(),
            Version: 3,
            ProposalState: CorporateActionSourceProposalStates.ReviewRequired,
            AcceptanceScope: new CorporateActionCaseScopeDto(
                "tenant-a", "company-a", FundProfileId: "fund-a", LedgerBookId: "book-a", AccountingBasis: "GAAP"),
            ActionAvailability: new CorporateActionSourceProposalActionAvailabilityDto(
                CanAccept: true, CanReject: true, CanCompareEvidence: true, Blockers: []));

        var json = JsonSerializer.SerializeToElement(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.GetProperty("proposalId").GetGuid().Should().Be(entry.ProposalId);
        json.GetProperty("version").GetInt64().Should().Be(3);
        json.GetProperty("proposalState").GetString().Should().Be(CorporateActionSourceProposalStates.ReviewRequired);
        json.GetProperty("acceptanceScope").GetProperty("tenantId").GetString().Should().Be("tenant-a");
        json.GetProperty("acceptanceScope").GetProperty("companyId").GetString().Should().Be("company-a");
        json.GetProperty("actionAvailability").GetProperty("canAccept").GetBoolean().Should().BeTrue();
    }

    private static CorporateActionDto Dividend(
        decimal amount,
        Guid corporateActionId,
        string currency = "USD",
        JsonElement? payload = null) =>
        new(
            corporateActionId,
            SecurityId,
            CorporateActionEventTypes.Dividend,
            ExDate,
            ExDate.AddDays(14),
            amount,
            currency,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            RecordDate: ExDate.AddDays(1),
            Payload: payload);

    private static CorporateActionCaseTransitionAuthorityDto Authority(
        bool canResolveTerms = false,
        bool canRecordElection = false,
        bool canPrepareAccounting = false,
        bool canOverridePolicy = false,
        bool canReopenCase = false) =>
        new(
            canResolveTerms,
            canRecordElection,
            canPrepareAccounting,
            canOverridePolicy,
            canReopenCase);

    private static CorporateActionProcessingCaseDto ProcessingCase(string state) =>
        new(
            Guid.Parse("57d852c5-7a92-41c4-8ff9-386cf87cc1c6"),
            Guid.Parse("bb390ac9-90e8-474b-9d90-bc5e673a6f75"),
            Guid.Parse("d55769d5-454e-4dad-be61-8239ba131c3c"),
            SecurityId,
            new CorporateActionCaseScopeDto("tenant-a", "company-a"),
            state,
            Version: 3,
            MethodologyProfileId: "clearwater-corporate-actions/v1",
            AssignedTo: null,
            BlockedReason: null,
            CreatedBy: "operations-user",
            CreatedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
            UpdatedBy: "operations-user",
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}
