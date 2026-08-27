using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// Verifies the corporate-action ingest loop: fan-out across providers, cross-provider
/// consensus voting, dedupe against already-recorded actions, and staged handling for
/// announcements below the configured source threshold.
/// </summary>
public sealed class CorporateActionIngestOrchestratorTests
{
    private static readonly DateOnly ExDate = new(2026, 8, 14);

    [Fact]
    public async Task IngestAsync_ReviewOnlyConsensusRemainsStagedWithoutAppending()
    {
        var securityId = Guid.NewGuid();
        var eventStore = EmptyEventStore(securityId);
        var orchestrator = CreateOrchestrator(
            securityId,
            eventStore,
            out var commandService,
            new StubProvider("finnhub", Dividend(securityId, "finnhub", 0.24m)),
            new StubProvider("tiingo", Dividend(securityId, "tiingo", 0.24m)));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Applied.Should().Be(0);
        result.Staged.Should().Be(1);
        var proposal = result.Proposals.Should().ContainSingle().Subject;
        proposal.AutoApplied.Should().BeFalse();
        proposal.ProviderReleaseStatus.Should().Be(CorporateActionProviderReleaseStatusDto.ReviewOnly);
        proposal.WinningSource.Should().Be("tiingo");
        proposal.AgreeingSources.Should().BeEquivalentTo("finnhub", "tiingo");
        proposal.DissentingSources.Should().BeEmpty();
        commandService.Requests.Should().BeEmpty();
        await eventStore.DidNotReceive().AppendCorporateActionAsync(
            Arg.Any<CorporateActionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_LegacyAppendRequiresAcceptanceEligibleProvidersAndRealEvidence()
    {
        var securityId = Guid.NewGuid();
        var eventStore = EmptyEventStore(securityId);
        var orchestrator = CreateOrchestrator(
            securityId,
            eventStore,
            out var commandService,
            new AcceptanceEligibleStubProvider(
                "provider-a", AcceptanceGradeDividend(securityId, "provider-a", 0.24m)),
            new AcceptanceEligibleStubProvider(
                "provider-b", AcceptanceGradeDividend(securityId, "provider-b", 0.24m)));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Applied.Should().Be(1);
        result.Staged.Should().Be(0);
        result.Proposals.Should().ContainSingle().Which.ProviderReleaseStatus
            .Should().Be(CorporateActionProviderReleaseStatusDto.AcceptanceEligible);
        commandService.Requests.Should().ContainSingle(request =>
            request.SecurityId == securityId
            && request.CorporateAction.DividendPerShare == 0.24m);
    }

    [Fact]
    public async Task IngestAsync_StagesDisputedDividend_WithoutAppending()
    {
        var securityId = Guid.NewGuid();
        var eventStore = EmptyEventStore(securityId);
        var orchestrator = CreateOrchestrator(
            securityId,
            eventStore,
            out var commandService,
            new StubProvider("finnhub", Dividend(securityId, "finnhub", 0.24m)),
            new StubProvider("alphavantage", Dividend(securityId, "alphavantage", 0.26m)));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Applied.Should().Be(0);
        result.Staged.Should().Be(2, "economically distinct provider blocks must not be discarded");
        result.Proposals.Should().HaveCount(2);
        result.Proposals.Should().OnlyContain(static proposal => !proposal.AutoApplied);
        result.Proposals.Should().ContainSingle(proposal =>
            proposal.WinningSource == "alphavantage"
            && proposal.Amount == 0.26m
            && proposal.AgreeingSources.Count == 1
            && proposal.AgreeingSources[0] == "alphavantage"
            && proposal.DissentingSources.Count == 1
            && proposal.DissentingSources[0] == "finnhub");
        result.Proposals.Should().ContainSingle(proposal =>
            proposal.WinningSource == "finnhub"
            && proposal.Amount == 0.24m
            && proposal.AgreeingSources.Count == 1
            && proposal.AgreeingSources[0] == "finnhub"
            && proposal.DissentingSources.Count == 1
            && proposal.DissentingSources[0] == "alphavantage");
        commandService.Requests.Should().BeEmpty();
        await eventStore.DidNotReceive().AppendCorporateActionAsync(
            Arg.Any<CorporateActionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_ValueKeyPreservesDomainDecimalPrecision()
    {
        var securityId = Guid.NewGuid();
        var orchestrator = CreateOrchestrator(
            securityId,
            EmptyEventStore(securityId),
            out _,
            new StubProvider("provider-a", Dividend(securityId, "provider-a", 0.12345671m)),
            new StubProvider("provider-b", Dividend(securityId, "provider-b", 0.12345672m)));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Proposals.Should().HaveCount(2,
            "G29 value keys must not merge distinct values beyond six decimal places");
        result.Proposals.Select(static proposal => proposal.Amount)
            .Should().BeEquivalentTo(new decimal?[] { 0.12345671m, 0.12345672m });
    }

    [Fact]
    public async Task IngestAsync_QuarantinesSecurityAndProviderIdentityMismatches()
    {
        var securityId = Guid.NewGuid();
        var wrongSecurity = Dividend(Guid.NewGuid(), "provider-a", 0.24m);
        var spoofedProvider = Dividend(securityId, "provider-b", 0.25m);
        var orchestrator = CreateOrchestrator(
            securityId,
            EmptyEventStore(securityId),
            out var commandService,
            new StubProvider("provider-a", wrongSecurity, spoofedProvider));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Proposals.Should().BeEmpty();
        result.Errors.Should().Contain(error => error.Contains("SecurityId", StringComparison.Ordinal));
        result.Errors.Should().Contain(error => error.Contains("spoofed SourceProvider", StringComparison.Ordinal));
        commandService.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_SkipsActionsAlreadyRecordedInEventStore()
    {
        var securityId = Guid.NewGuid();
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        eventStore.LoadCorporateActionsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(new[] { ExistingDividendDto(securityId) });
        var orchestrator = CreateOrchestrator(
            securityId,
            eventStore,
            out var commandService,
            new StubProvider("finnhub", Dividend(securityId, "finnhub", 0.24m)),
            new StubProvider("tiingo", Dividend(securityId, "tiingo", 0.24m)));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.DuplicatesSkipped.Should().Be(1);
        result.Applied.Should().Be(0);
        result.Proposals.Should().BeEmpty();
        commandService.Requests.Should().BeEmpty();
        await eventStore.DidNotReceive().AppendCorporateActionAsync(
            Arg.Any<CorporateActionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_RecordsProviderFailure_AndContinuesWithRemainingProviders()
    {
        var securityId = Guid.NewGuid();
        var eventStore = EmptyEventStore(securityId);
        var orchestrator = CreateOrchestrator(
            securityId,
            eventStore,
            out var commandService,
            new ThrowingProvider("nasdaq"),
            new StubProvider("finnhub", Dividend(securityId, "finnhub", 0.24m)));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Errors.Should().ContainSingle(static error => error.StartsWith("nasdaq/"));
        result.Applied.Should().Be(0);
        result.Staged.Should().Be(1, "single-source announcements stay staged for operator review");
        result.Proposals.Should().ContainSingle();
        commandService.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_DryRun_DoesNotWriteEvenWithConsensus()
    {
        var securityId = Guid.NewGuid();
        var eventStore = EmptyEventStore(securityId);
        var orchestrator = CreateOrchestrator(
            securityId,
            eventStore,
            out var commandService,
            new AcceptanceEligibleStubProvider(
                "provider-a", AcceptanceGradeDividend(securityId, "provider-a", 0.24m)),
            new AcceptanceEligibleStubProvider(
                "provider-b", AcceptanceGradeDividend(securityId, "provider-b", 0.24m)));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest(DryRun: true));

        result.Applied.Should().Be(1, "dry run reports what would be applied");
        commandService.Requests.Should().BeEmpty();
        await eventStore.DidNotReceive().AppendCorporateActionAsync(
            Arg.Any<CorporateActionDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_DurablePath_PersistsEveryProviderObservationWithoutCollapsingConsensus()
    {
        var securityId = Guid.NewGuid();
        var eventStore = EmptyEventStore(securityId);
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadActiveAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { MakeProjection(securityId) });
        var operations = Substitute.For<ICorporateActionOperationsService>();
        var recordedRequests = new List<RecordCorporateActionSourceProposalRequestDto>();
        operations.RecordSourceProposalAsync(
                Arg.Any<RecordCorporateActionSourceProposalRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.ArgAt<RecordCorporateActionSourceProposalRequestDto>(0);
                recordedRequests.Add(request);
                var now = DateTimeOffset.UtcNow;
                return new CorporateActionSourceProposalDto(
                    request.ProposalId!.Value,
                    request.ProposedAction.SecurityId,
                    request.ProviderIdentity,
                    request.ProposedAction,
                    request.ProposedAction.PayloadSchemaVersion,
                    CorporateActionEconomicFingerprint.Compute(request.ProposedAction),
                    CorporateActionSourceProposalStates.Observed,
                    Version: 1,
                    request.SupersedesProposalId,
                    AcceptedCorporateActionId: null,
                    InitialCaseId: null,
                    request.Actor,
                    now,
                    now,
                    DisplayMetadata: request.DisplayMetadata);
            });
        var orchestrator = new CorporateActionIngestOrchestrator(
            [
                new StubProvider("finnhub", Dividend(securityId, "finnhub", 0.24m)),
                new StubProvider("tiingo", Dividend(securityId, "tiingo", 0.24m)),
            ],
            store,
            eventStore,
            new RecordingCorporateActionCommandService(),
            NullLogger<CorporateActionIngestOrchestrator>.Instance,
            operations);

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Applied.Should().Be(0, "durable ingest never auto-accepts a canonical fact");
        result.Staged.Should().Be(2);
        result.Proposals.Should().HaveCount(2);
        result.Proposals.Select(static proposal => proposal.ObservationSource)
            .Should().BeEquivalentTo("finnhub", "tiingo");
        recordedRequests.Should().HaveCount(2);
        recordedRequests.Select(static request => request.ProviderIdentity.ProviderId)
            .Should().BeEquivalentTo("finnhub", "tiingo");
        recordedRequests.Should().OnlyContain(request =>
            request.DisplayMetadata!.AgreeingSources.Count == 2
            && request.DisplayMetadata.DissentingSources.Count == 0);
    }

    [Fact]
    public async Task IngestAsync_ProviderDissentRetainsFieldValuesAndEvidenceForEverySource()
    {
        var securityId = Guid.NewGuid();
        var providerA = Dividend(securityId, "provider-a", 0.24m) with
        {
            SourceEventId = "announcement-100",
            SourceEventVersion = "v1",
            EvidenceHash = new string('a', 64),
            EvidenceReference = "provider-event://provider-a/announcement-100/v1",
        };
        var providerB = Dividend(securityId, "provider-b", 0.26m) with
        {
            SourceEventId = "announcement-200",
            SourceEventVersion = "v1",
            EvidenceHash = new string('b', 64),
            EvidenceReference = "provider-event://provider-b/announcement-200/v1",
        };
        var orchestrator = CreateDurableOrchestrator(
            securityId,
            out var recordedRequests,
            new StubProvider("provider-a", providerA),
            new StubProvider("provider-b", providerB));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Errors.Should().BeEmpty();
        recordedRequests.Should().HaveCount(2);
        recordedRequests.Should().OnlyContain(request =>
            request.DisplayMetadata != null
            && request.DisplayMetadata.DissentingFields != null);
        foreach (var request in recordedRequests)
        {
            var amountConflict = request.DisplayMetadata!.DissentingFields!
                .Should().ContainSingle(field => field.Field == nameof(CorporateActionDto.DividendPerShare))
                .Subject;
            amountConflict.Candidates.Select(static candidate => candidate.Source)
                .Should().Equal("provider-a", "provider-b");
            amountConflict.Candidates.Select(static candidate => candidate.Value.GetDecimal())
                .Should().Equal(0.24m, 0.26m);
            amountConflict.Candidates.Select(static candidate => candidate.EvidenceReference)
                .Should().Equal(
                    "provider-event://provider-a/announcement-100/v1",
                    "provider-event://provider-b/announcement-200/v1");
        }
    }

    [Fact]
    public async Task IngestAsync_SynthesizedIdentity_UsesDescriptionWithoutInventingEvidence()
    {
        var securityId = Guid.NewGuid();
        var first = Dividend(securityId, "provider-a", 0.24m) with { Description = "Regular dividend series A" };
        var second = Dividend(securityId, "provider-a", 0.24m) with { Description = "Regular dividend series B" };
        var orchestrator = CreateDurableOrchestrator(
            securityId,
            out var recordedRequests,
            new StubProvider("provider-a", first, second));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Errors.Should().BeEmpty();
        recordedRequests.Should().HaveCount(2);
        recordedRequests.Select(static request => request.ProviderIdentity.SourceEventId)
            .Should().OnlyHaveUniqueItems();
        recordedRequests.Should().OnlyContain(request =>
            request.ProviderIdentity.SourceEventVersion.StartsWith("unverified-content-", StringComparison.Ordinal)
            && request.ProviderIdentity.EvidenceHash == null
            && request.ProviderIdentity.EvidenceReference == null
            && request.ProviderIdentity.ReleaseStatus == CorporateActionProviderReleaseStatusDto.ReviewOnly);
    }

    [Fact]
    public async Task IngestAsync_IndistinguishableSynthesizedObservations_FailClosedWithoutPersistence()
    {
        var securityId = Guid.NewGuid();
        var observation = Dividend(securityId, "provider-a", 0.24m);
        var orchestrator = CreateDurableOrchestrator(
            securityId,
            out var recordedRequests,
            new StubProvider("provider-a", observation, observation));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Staged.Should().Be(0);
        result.Errors.Should().ContainSingle(error =>
            error.Contains("identical synthesized identity fields", StringComparison.Ordinal));
        recordedRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task IngestAsync_NativeProviderEventVersions_DistinguishIdenticalSameDayTerms()
    {
        var securityId = Guid.NewGuid();
        var first = Dividend(securityId, "provider-a", 0.24m) with
        {
            SourceEventId = "announcement-100",
            SourceEventVersion = "v1",
        };
        var second = first with { SourceEventId = "announcement-101" };
        var orchestrator = CreateDurableOrchestrator(
            securityId,
            out var recordedRequests,
            new StubProvider("provider-a", first, second));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Errors.Should().BeEmpty();
        result.Staged.Should().Be(2);
        recordedRequests.Select(static request => request.ProviderIdentity.SourceEventId)
            .Should().BeEquivalentTo("announcement-100", "announcement-101");
        recordedRequests.Should().OnlyContain(request => request.ProviderIdentity.SourceEventVersion == "v1");
    }

    [Fact]
    public async Task IngestAsync_SameProviderEventDifferentVersions_StagesBothForDurableChainResolution()
    {
        var securityId = Guid.NewGuid();
        var first = Dividend(securityId, "provider-a", 0.24m) with
        {
            SourceEventId = "announcement-100",
            SourceEventVersion = "v1",
        };
        var second = first with { SourceEventVersion = "v2", Amount = 0.25m };
        var orchestrator = CreateDurableOrchestrator(
            securityId,
            out var recordedRequests,
            new StubProvider("provider-a", first, second));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Errors.Should().BeEmpty();
        recordedRequests.Should().HaveCount(2);
        recordedRequests.Select(static request => request.ProviderIdentity.SourceEventId)
            .Should().OnlyContain(value => value == "announcement-100");
        recordedRequests.Select(static request => request.ProviderIdentity.SourceEventVersion)
            .Should().BeEquivalentTo("v1", "v2");
    }

    [Fact]
    public async Task IngestAsync_ReviewOnlyAdapterCannotSelfElevateThroughCommandContent()
    {
        var securityId = Guid.NewGuid();
        var command = Dividend(securityId, "tiingo", 0.24m) with
        {
            SourceEventId = "event-100",
            SourceEventVersion = "v1",
            EvidenceHash = new string('a', 64),
            EvidenceReference = "provider://event-100/raw",
            ReleaseStatus = CorporateActionProviderReleaseStatusDto.AcceptanceEligible,
        };
        var orchestrator = CreateDurableOrchestrator(
            securityId,
            out var recordedRequests,
            new StubProvider("tiingo", command));

        await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        recordedRequests.Should().ContainSingle();
        recordedRequests[0].ProviderIdentity.ReleaseStatus
            .Should().Be(CorporateActionProviderReleaseStatusDto.ReviewOnly);
    }

    private static CorporateActionIngestOrchestrator CreateOrchestrator(
        Guid securityId,
        ISecurityMasterEventStore eventStore,
        out RecordingCorporateActionCommandService commandService,
        params ICorporateActionProvider[] providers)
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadActiveAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { MakeProjection(securityId) });
        commandService = new RecordingCorporateActionCommandService();
        return new CorporateActionIngestOrchestrator(
            providers,
            store,
            eventStore,
            commandService,
            NullLogger<CorporateActionIngestOrchestrator>.Instance);
    }

    private static CorporateActionIngestOrchestrator CreateDurableOrchestrator(
        Guid securityId,
        out List<RecordCorporateActionSourceProposalRequestDto> recordedRequests,
        params ICorporateActionProvider[] providers)
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadActiveAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { MakeProjection(securityId) });
        var operations = Substitute.For<ICorporateActionOperationsService>();
        var captured = new List<RecordCorporateActionSourceProposalRequestDto>();
        recordedRequests = captured;
        operations.RecordSourceProposalAsync(
                Arg.Any<RecordCorporateActionSourceProposalRequestDto>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.ArgAt<RecordCorporateActionSourceProposalRequestDto>(0);
                captured.Add(request);
                var now = DateTimeOffset.UtcNow;
                return new CorporateActionSourceProposalDto(
                    request.ProposalId!.Value,
                    request.ProposedAction.SecurityId,
                    request.ProviderIdentity,
                    request.ProposedAction,
                    request.ProposedAction.PayloadSchemaVersion,
                    CorporateActionEconomicFingerprint.Compute(request.ProposedAction),
                    request.ProviderIdentity.SourceEventVersion.StartsWith(
                        "unverified-content-", StringComparison.Ordinal)
                    || request.ProviderIdentity.ReleaseStatus == CorporateActionProviderReleaseStatusDto.ReviewOnly
                        ? CorporateActionSourceProposalStates.ReviewRequired
                        : CorporateActionSourceProposalStates.Observed,
                    Version: 1,
                    request.SupersedesProposalId,
                    AcceptedCorporateActionId: null,
                    InitialCaseId: null,
                    request.Actor,
                    now,
                    now,
                    DisplayMetadata: request.DisplayMetadata);
            });
        return new CorporateActionIngestOrchestrator(
            providers,
            store,
            EmptyEventStore(securityId),
            new RecordingCorporateActionCommandService(),
            NullLogger<CorporateActionIngestOrchestrator>.Instance,
            operations);
    }

    private static ISecurityMasterEventStore EmptyEventStore(Guid securityId)
    {
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        eventStore.LoadCorporateActionsAsync(securityId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CorporateActionDto>());
        return eventStore;
    }

    private static CorporateActionCommand Dividend(Guid securityId, string source, decimal amount)
        => new(
            SecurityId: securityId,
            ActionType: "Dividend",
            ExDate: ExDate,
            RecordDate: null,
            PayableDate: ExDate.AddDays(14),
            Amount: amount,
            Currency: "USD",
            SplitFromFactor: null,
            SplitToFactor: null,
            Description: "Quarterly cash dividend",
            SourceProvider: source);

    private static CorporateActionCommand AcceptanceGradeDividend(
        Guid securityId,
        string source,
        decimal amount) =>
        Dividend(securityId, source, amount) with
        {
            SourceEventId = $"{source}-event-100",
            SourceEventVersion = "v1",
            EvidenceHash = new string('a', 64),
            EvidenceReference = $"provider://{source}/corporate-actions/event-100/raw",
        };

    private static CorporateActionDto ExistingDividendDto(Guid securityId)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "Dividend",
            ExDate: ExDate,
            PayDate: null,
            DividendPerShare: 0.24m,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);

    private static SecurityProjectionRecord MakeProjection(Guid securityId)
        => new(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: "Acme Corp",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "ACME",
            CommonTerms: JsonSerializer.SerializeToElement(new { displayName = "Acme Corp", currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { schemaVersion = 1 }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = "test", updatedBy = "codex" }),
            Version: 3,
            EffectiveFrom: DateTimeOffset.UtcNow.AddYears(-1),
            EffectiveTo: null,
            Identifiers: new[]
            {
                new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "ACME", true, DateTimeOffset.UtcNow.AddYears(-1), null, null)
            },
            Aliases: Array.Empty<SecurityAliasDto>());

    private sealed class StubProvider(string providerId, params CorporateActionCommand[] commands)
        : ICorporateActionProvider
    {
        public string ProviderId => providerId;

        public Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
            string ticker, Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionCommand>>(commands);
    }

    private sealed class AcceptanceEligibleStubProvider(
        string providerId,
        params CorporateActionCommand[] commands) : ICorporateActionProvider
    {
        public string ProviderId => providerId;

        public CorporateActionProviderReleaseStatusDto ReleaseStatus =>
            CorporateActionProviderReleaseStatusDto.AcceptanceEligible;

        public Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
            string ticker, Guid securityId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CorporateActionCommand>>(commands);
    }

    private sealed class ThrowingProvider(string providerId) : ICorporateActionProvider
    {
        public string ProviderId => providerId;

        public Task<IReadOnlyList<CorporateActionCommand>> FetchAsync(
            string ticker, Guid securityId, CancellationToken ct = default)
            => throw new HttpRequestException("upstream unavailable");
    }

    private sealed class RecordingCorporateActionCommandService : ISecurityMasterCorporateActionCommandService
    {
        public List<SecurityMasterCorporateActionAppendRequestDto> Requests { get; } = [];

        public Task<SecurityMasterCorporateActionAppendResultDto> AppendAsync(
            SecurityMasterCorporateActionAppendRequestDto request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new SecurityMasterCorporateActionAppendResultDto(
                request.CorporateAction,
                new SecurityMasterCorporateActionAuditDto(
                    $"audit:{request.CorporateAction.CorpActId:D}",
                    request.SecurityId,
                    request.CorporateAction.CorpActId,
                    request.CorporateAction.EventType,
                    request.SourceSystem,
                    request.Actor,
                    DateTimeOffset.UtcNow,
                    request.SourceRecordId,
                    request.Reason,
                    request.CorrelationId)));
        }
    }
}
