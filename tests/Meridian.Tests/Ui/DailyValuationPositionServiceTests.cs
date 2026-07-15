using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class DailyValuationPositionServiceTests
{
    private static readonly Guid SecurityId = Guid.Parse("84f0e04b-354b-4448-a7d7-ef88025280ae");
    private static readonly Guid BookId = Guid.Parse("58a459dc-1eb8-4116-bf40-bf8e3846835d");
    private static readonly Guid PeriodId = Guid.Parse("092fbb21-e03f-4f9a-9cb1-dcbd10d3ee53");
    private static readonly DateTimeOffset ValuationAsOf = DateTimeOffset.Parse("2026-07-15T23:00:00Z");
    private static readonly JsonElement EmptyTerms = JsonDocument.Parse("{}").RootElement.Clone();

    [Fact]
    public async Task ResolveConfiguredAsync_FreshOwnedSnapshot_ResolvesSecurityAndEvidence()
    {
        var store = Substitute.For<IPositionSnapshotStore>();
        store.GetLatestSnapshotAsync(
                "run-a",
                "account-a",
                Arg.Any<PositionSnapshotOwnerScope>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot("run-a", "account-a", ValuationAsOf.AddMinutes(-15)));
        var service = CreateService(store);

        var result = await service.ResolveConfiguredAsync(
            WorkItem() with { PositionSnapshotScopes = [new("run-a", "account-a")] },
            ValuationAsOf);

        result.IsReady.Should().BeTrue();
        result.Positions.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new MarkToMarketPosition("AAPL", 10m, 150m, "account-a", "Equity", SecurityId));
        result.EvidenceLinks.Should().ContainSingle(link =>
            link.Route.Contains("run-a/account-a", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(-3, "stale")]
    [InlineData(1, "dated after")]
    public async Task ResolveConfiguredAsync_StaleOrFutureSnapshot_FailsClosed(
        int offsetDays,
        string expectedBlocker)
    {
        var store = Substitute.For<IPositionSnapshotStore>();
        store.GetLatestSnapshotAsync(
                "run-a",
                "account-a",
                Arg.Any<PositionSnapshotOwnerScope>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot("run-a", "account-a", ValuationAsOf.AddDays(offsetDays)));
        var service = CreateService(store);

        var result = await service.ResolveConfiguredAsync(
            WorkItem() with
            {
                PositionSnapshotScopes = [new("run-a", "account-a")],
                MaximumPositionAgeDays = 1
            },
            ValuationAsOf);

        result.IsReady.Should().BeFalse();
        result.Blockers.Should().ContainSingle(message =>
            message.Contains(expectedBlocker, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveConfiguredAsync_SnapshotStoreReturnsDifferentOwner_FailsClosed()
    {
        var store = Substitute.For<IPositionSnapshotStore>();
        store.GetLatestSnapshotAsync(
                "run-a",
                "account-a",
                Arg.Any<PositionSnapshotOwnerScope>(),
                Arg.Any<CancellationToken>())
            .Returns(Snapshot("run-a", "account-a", ValuationAsOf) with { TenantId = "tenant-other" });
        var service = CreateService(store);

        var result = await service.ResolveConfiguredAsync(
            WorkItem() with { PositionSnapshotScopes = [new("run-a", "account-a")] },
            ValuationAsOf);

        result.IsReady.Should().BeFalse();
        result.Blockers.Should().ContainSingle(message => message.Contains("immutable tenant/company/fund/book/entity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveConfiguredAsync_ScheduleMissingSnapshotOwner_FailsClosedBeforeLookup()
    {
        var store = Substitute.For<IPositionSnapshotStore>();
        var service = CreateService(store);

        var result = await service.ResolveConfiguredAsync(
            WorkItem() with
            {
                EntityId = null,
                PositionSnapshotScopes = [new("run-a", "account-a")]
            },
            ValuationAsOf);

        result.IsReady.Should().BeFalse();
        result.Blockers.Should().ContainSingle(message => message.Contains("tenant, company, fund profile, ledger book, and entity", StringComparison.Ordinal));
        await store.DidNotReceiveWithAnyArgs().GetLatestSnapshotAsync(
            default!,
            default!,
            default!,
            default);
    }

    [Fact]
    public async Task ResolveConfiguredAsync_StaticOverrideHashMismatch_FailsClosed()
    {
        var positions = new[] { new MarkToMarketPosition("AAPL", 10m, 150m, "account-a") };
        var service = CreateService();

        var result = await service.ResolveConfiguredAsync(
            WorkItem() with
            {
                Positions = positions,
                UseStaticPositionOverride = true,
                StaticPositionsAsOfUtc = ValuationAsOf,
                StaticPositionHash = "tampered"
            },
            ValuationAsOf);

        result.IsReady.Should().BeFalse();
        result.Blockers.Should().ContainSingle(message => message.Contains("hash does not match", StringComparison.Ordinal));
        DailyValuationPositionService.ComputeStaticPositionHash(positions)
            .Should().Be(DailyValuationPositionService.ComputeStaticPositionHash(positions.Reverse().ToArray()));
    }

    [Theory]
    [InlineData(false, "USD", "Inactive")]
    [InlineData(true, "EUR", "not valuation base currency")]
    public async Task ResolveAdHocAsync_InactiveOrWrongCurrencySecurity_FailsClosed(
        bool isActive,
        string securityCurrency,
        string expectedBlocker)
    {
        var service = CreateService(
            securityStatus: isActive ? SecurityStatusDto.Active : SecurityStatusDto.Inactive,
            securityCurrency: securityCurrency);

        var result = await service.ResolveAdHocAsync(
            [new MarkToMarketPosition("AAPL", 10m, 150m, "account-a")],
            "USD",
            ValuationAsOf);

        result.IsReady.Should().BeFalse();
        result.Blockers.Should().ContainSingle(message =>
            message.Contains(expectedBlocker, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveAdHocAsync_DuplicateSecurityAccountScope_FailsClosed()
    {
        var service = CreateService();

        var result = await service.ResolveAdHocAsync(
            [
                new MarkToMarketPosition("AAPL", 10m, 150m, "account-a"),
                new MarkToMarketPosition("AAPL", 5m, 151m, "account-a")
            ],
            "USD",
            ValuationAsOf);

        result.IsReady.Should().BeFalse();
        result.Blockers.Should().ContainSingle(message => message.Contains("duplicate security/account", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAdHocAsync_MissingHistoricalSecurityState_DoesNotFallBackToCurrentRecord()
    {
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        registry.GetDefinition("AAPL").Returns(new CanonicalSymbolDefinition
        {
            Canonical = "AAPL",
            DisplayName = "Apple Inc.",
            SecurityId = SecurityId,
            AssetClass = "Equity",
            Exchange = "NASDAQ",
            Currency = "USD",
            Aliases = ["AAPL"]
        });
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        securityMaster.GetByIdAsOfAsync(SecurityId, ValuationAsOf, Arg.Any<CancellationToken>())
            .Returns((SecurityDetailDto?)null);
        securityMaster.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(SecurityDetail(SecurityStatusDto.Active, "USD"));
        var service = new DailyValuationPositionService(null, registry, securityMaster);

        var result = await service.ResolveAdHocAsync(
            [new MarkToMarketPosition("AAPL", 10m, 150m, "account-a")],
            "USD",
            ValuationAsOf);

        result.IsReady.Should().BeFalse();
        result.Blockers.Should().ContainSingle(message => message.Contains("no authoritative as-of record", StringComparison.OrdinalIgnoreCase));
        await securityMaster.DidNotReceive().GetByIdAsync(SecurityId, Arg.Any<CancellationToken>());
    }

    private static DailyValuationPositionService CreateService(
        IPositionSnapshotStore? store = null,
        SecurityStatusDto securityStatus = SecurityStatusDto.Active,
        string securityCurrency = "USD")
    {
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        registry.GetDefinition("AAPL").Returns(new CanonicalSymbolDefinition
        {
            Canonical = "AAPL",
            DisplayName = "Apple Inc.",
            SecurityId = SecurityId,
            AssetClass = "Equity",
            Exchange = "NASDAQ",
            Currency = securityCurrency,
            Aliases = ["AAPL"]
        });
        var securityMaster = Substitute.For<ISecurityMasterQueryService>();
        var detail = SecurityDetail(securityStatus, securityCurrency);
        securityMaster.GetByIdAsOfAsync(SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(detail);
        securityMaster.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(detail);
        return new DailyValuationPositionService(store, registry, securityMaster);
    }

    private static AccountSnapshotRecord Snapshot(string runId, string accountId, DateTimeOffset asOf)
        => new(
            runId,
            accountId,
            accountId,
            "Brokerage",
            Cash: 0m,
            MarginBalance: 0m,
            UnrealisedPnl: 0m,
            RealisedPnl: 0m,
            Positions: [new PositionRecord("AAPL", 10m, 150m, 0m, 0m)],
            AsOf: asOf,
            TenantId: "tenant-a",
            CompanyId: "company-a",
            FundProfileId: "fund-a",
            LedgerBookId: BookId,
            EntityId: "entity-a");

    private static SecurityDetailDto SecurityDetail(SecurityStatusDto status, string currency)
        => new(
            SecurityId,
            "Equity",
            status,
            "Apple Inc.",
            currency,
            EmptyTerms,
            EmptyTerms,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Ticker, "AAPL", true, ValuationAsOf.AddYears(-1))],
            [],
            Version: 1,
            EffectiveFrom: ValuationAsOf.AddYears(-1),
            EffectiveTo: null);

    private static DailyValuationScheduleWorkItem WorkItem()
        => new(
            "daily-a",
            "fund-a",
            "USD",
            "preparer-a",
            BookId,
            PeriodId,
            ValuationAsOf,
            [],
            "policy-a",
            "Listed equity close",
            "Provider close",
            "controller-a",
            ValuationAsOf.AddDays(-1),
            "Daily close",
            EntityId: "entity-a",
            TenantId: "tenant-a",
            CompanyId: "company-a");
}
