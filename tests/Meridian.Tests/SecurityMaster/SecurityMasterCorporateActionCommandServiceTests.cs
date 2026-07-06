using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterCorporateActionCommandServiceTests
{
    [Fact]
    public async Task AppendAsync_WithValidDividend_AppendsThroughEventStoreAndReturnsAudit()
    {
        var securityId = Guid.NewGuid();
        var action = CreateDividend(securityId);
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var service = new SecurityMasterCorporateActionCommandService(
            eventStore,
            NullLogger<SecurityMasterCorporateActionCommandService>.Instance);

        var result = await service.AppendAsync(new SecurityMasterCorporateActionAppendRequestDto(
            securityId,
            action,
            SourceSystem: "workstation-http",
            Actor: "analyst@example.com",
            SourceRecordId: "case-42",
            Reason: "operator import",
            CorrelationId: "trace-42"));

        result.CorporateAction.Should().Be(action);
        result.Audit.SecurityId.Should().Be(securityId);
        result.Audit.CorporateActionId.Should().Be(action.CorpActId);
        result.Audit.SourceSystem.Should().Be("workstation-http");
        result.Audit.Actor.Should().Be("analyst@example.com");
        result.Audit.SourceRecordId.Should().Be("case-42");
        result.Audit.Reason.Should().Be("operator import");
        result.Audit.CorrelationId.Should().Be("trace-42");
        await eventStore.Received(1).AppendCorporateActionAsync(action, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendAsync_WithMismatchedSecurityId_RejectsBeforeWrite()
    {
        var action = CreateDividend(Guid.NewGuid());
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var service = new SecurityMasterCorporateActionCommandService(
            eventStore,
            NullLogger<SecurityMasterCorporateActionCommandService>.Instance);

        var append = async () => await service.AppendAsync(new SecurityMasterCorporateActionAppendRequestDto(
            Guid.NewGuid(),
            action,
            SourceSystem: "workstation-http",
            Actor: "analyst@example.com"));

        await append.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Corporate action SecurityId must match route parameter*");
        await eventStore.DidNotReceiveWithAnyArgs().AppendCorporateActionAsync(default!, default);
    }

    [Fact]
    public async Task AppendAsync_WithInvalidStockSplit_RejectsBeforeWrite()
    {
        var securityId = Guid.NewGuid();
        var action = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "StockSplit",
            ExDate: new DateOnly(2026, 6, 22),
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: 0m,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var service = new SecurityMasterCorporateActionCommandService(
            eventStore,
            NullLogger<SecurityMasterCorporateActionCommandService>.Instance);

        var append = async () => await service.AppendAsync(new SecurityMasterCorporateActionAppendRequestDto(
            securityId,
            action,
            SourceSystem: "Polygon",
            Actor: "polygon-corporate-action-fetcher"));

        await append.Should().ThrowAsync<ArgumentException>()
            .WithMessage("StockSplit SplitRatio must be greater than 0 and less than or equal to 1000.*");
        await eventStore.DidNotReceiveWithAnyArgs().AppendCorporateActionAsync(default!, default);
    }

    [Fact]
    public async Task AppendAsync_WithLegacySplitVocabulary_NormalizesBeforeWrite()
    {
        var securityId = Guid.NewGuid();
        var action = new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "Split",
            ExDate: new DateOnly(2026, 6, 22),
            PayDate: null,
            DividendPerShare: null,
            Currency: null,
            SplitRatio: 2m,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var service = new SecurityMasterCorporateActionCommandService(
            eventStore,
            NullLogger<SecurityMasterCorporateActionCommandService>.Instance);

        var result = await service.AppendAsync(new SecurityMasterCorporateActionAppendRequestDto(
            securityId,
            action,
            SourceSystem: "Tiingo",
            Actor: "corporate-action-ingest-orchestrator"));

        result.CorporateAction.EventType.Should().Be("StockSplit");
        await eventStore.Received(1).AppendCorporateActionAsync(
            Arg.Is<CorporateActionDto>(dto => dto.EventType == "StockSplit"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendAsync_WithPayDateBeforeRecordDate_RejectsBeforeWrite()
    {
        var securityId = Guid.NewGuid();
        var action = CreateDividend(securityId) with
        {
            RecordDate = new DateOnly(2026, 7, 10),
            PayDate = new DateOnly(2026, 7, 1)
        };
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var service = new SecurityMasterCorporateActionCommandService(
            eventStore,
            NullLogger<SecurityMasterCorporateActionCommandService>.Instance);

        var append = async () => await service.AppendAsync(new SecurityMasterCorporateActionAppendRequestDto(
            securityId,
            action,
            SourceSystem: "Polygon",
            Actor: "polygon-corporate-action-fetcher"));

        await append.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Corporate action PayDate must be on or after RecordDate.*");
        await eventStore.DidNotReceiveWithAnyArgs().AppendCorporateActionAsync(default!, default);
    }

    [Fact]
    public async Task AppendAsync_WithCashDividendMissingCurrency_RejectsBeforeWrite()
    {
        var securityId = Guid.NewGuid();
        var action = CreateDividend(securityId) with { Currency = null };
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        var service = new SecurityMasterCorporateActionCommandService(
            eventStore,
            NullLogger<SecurityMasterCorporateActionCommandService>.Instance);

        var append = async () => await service.AppendAsync(new SecurityMasterCorporateActionAppendRequestDto(
            securityId,
            action,
            SourceSystem: "Polygon",
            Actor: "polygon-corporate-action-fetcher"));

        await append.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Corporate action cash amounts must include Currency.*");
        await eventStore.DidNotReceiveWithAnyArgs().AppendCorporateActionAsync(default!, default);
    }

    private static CorporateActionDto CreateDividend(Guid securityId)
        => new(
            CorpActId: Guid.NewGuid(),
            SecurityId: securityId,
            EventType: "Dividend",
            ExDate: new DateOnly(2026, 6, 22),
            PayDate: new DateOnly(2026, 7, 15),
            DividendPerShare: 0.24m,
            Currency: "USD",
            SplitRatio: null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null);
}
