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
    public async Task IngestAsync_AppendsDividend_WhenTwoProvidersAgree()
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

        result.Applied.Should().Be(1);
        result.Staged.Should().Be(0);
        var proposal = result.Proposals.Should().ContainSingle().Subject;
        proposal.AutoApplied.Should().BeTrue();
        proposal.WinningSource.Should().Be("tiingo");
        proposal.AgreeingSources.Should().BeEquivalentTo("finnhub", "tiingo");
        proposal.DissentingSources.Should().BeEmpty();
        commandService.Requests.Should().ContainSingle(request =>
            request.SecurityId == securityId &&
            request.SourceSystem == "tiingo" &&
            request.CorporateAction.EventType == "Dividend" &&
            request.CorporateAction.DividendPerShare == 0.24m);
        await eventStore.DidNotReceive().AppendCorporateActionAsync(
            Arg.Any<CorporateActionDto>(), Arg.Any<CancellationToken>());
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
        result.Staged.Should().Be(1);
        var proposal = result.Proposals.Should().ContainSingle().Subject;
        proposal.AutoApplied.Should().BeFalse();
        proposal.WinningSource.Should().Be("alphavantage");
        proposal.AgreeingSources.Should().BeEquivalentTo("alphavantage");
        proposal.DissentingSources.Should().BeEquivalentTo("finnhub");
        commandService.Requests.Should().BeEmpty();
        await eventStore.DidNotReceive().AppendCorporateActionAsync(
            Arg.Any<CorporateActionDto>(), Arg.Any<CancellationToken>());
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
            new StubProvider("finnhub", Dividend(securityId, "finnhub", 0.24m)),
            new StubProvider("tiingo", Dividend(securityId, "tiingo", 0.24m)));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest(DryRun: true));

        result.Applied.Should().Be(1, "dry run reports what would be applied");
        commandService.Requests.Should().BeEmpty();
        await eventStore.DidNotReceive().AppendCorporateActionAsync(
            Arg.Any<CorporateActionDto>(), Arg.Any<CancellationToken>());
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
