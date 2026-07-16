using System.Text.Json;
using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Application.SecurityMaster.Rebuild;
using Meridian.Contracts.Catalog;
using Meridian.Contracts.Domain;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
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
        store.GetSnapshotHistoryAsync(
                "run-a",
                "account-a",
                Arg.Any<PositionSnapshotOwnerScope>(),
                Arg.Any<DateTimeOffset>(),
                ValuationAsOf,
                Arg.Any<CancellationToken>())
            .Returns(_ => History(Snapshot("run-a", "account-a", ValuationAsOf.AddMinutes(-15))));
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
    [InlineData(1, "at or before")]
    public async Task ResolveConfiguredAsync_StaleOrFutureSnapshot_FailsClosed(
        int offsetDays,
        string expectedBlocker)
    {
        var store = Substitute.For<IPositionSnapshotStore>();
        store.GetSnapshotHistoryAsync(
                "run-a",
                "account-a",
                Arg.Any<PositionSnapshotOwnerScope>(),
                Arg.Any<DateTimeOffset>(),
                ValuationAsOf,
                Arg.Any<CancellationToken>())
            .Returns(_ => History(Snapshot("run-a", "account-a", ValuationAsOf.AddDays(offsetDays))));
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
    public async Task ResolveConfiguredAsync_NewerPostCutoffSnapshot_DoesNotHideValidAsOfHistory()
    {
        var store = Substitute.For<IPositionSnapshotStore>();
        var validAsOf = Snapshot("run-a", "account-a", ValuationAsOf.AddMinutes(-15));
        var afterCutoff = Snapshot("run-a", "account-a", ValuationAsOf.AddMinutes(5)) with
        {
            Positions = [new PositionRecord("AAPL", 99m, 999m, 0m, 0m)]
        };
        store.GetSnapshotHistoryAsync(
                "run-a",
                "account-a",
                Arg.Any<PositionSnapshotOwnerScope>(),
                Arg.Any<DateTimeOffset>(),
                ValuationAsOf,
                Arg.Any<CancellationToken>())
            .Returns(_ => History(validAsOf, afterCutoff));
        var service = CreateService(store);

        var result = await service.ResolveConfiguredAsync(
            WorkItem() with { PositionSnapshotScopes = [new("run-a", "account-a")] },
            ValuationAsOf);

        result.IsReady.Should().BeTrue();
        result.Positions.Should().ContainSingle().Which.Quantity.Should().Be(10m);
        result.EvidenceLinks.Should().ContainSingle().Which.CapturedAtUtc.Should().Be(validAsOf.AsOf);
    }

    [Fact]
    public async Task ResolveConfiguredAsync_CompleteFlatSnapshot_IsReadyWithoutPositions()
    {
        var store = Substitute.For<IPositionSnapshotStore>();
        var flat = Snapshot("run-a", "account-a", ValuationAsOf.AddMinutes(-15)) with { Positions = [] };
        store.GetSnapshotHistoryAsync(
                "run-a",
                "account-a",
                Arg.Any<PositionSnapshotOwnerScope>(),
                Arg.Any<DateTimeOffset>(),
                ValuationAsOf,
                Arg.Any<CancellationToken>())
            .Returns(_ => History(flat));
        var service = CreateService(store);

        var result = await service.ResolveConfiguredAsync(
            WorkItem() with { PositionSnapshotScopes = [new("run-a", "account-a")] },
            ValuationAsOf);

        result.IsReady.Should().BeTrue();
        result.Positions.Should().BeEmpty();
        result.Blockers.Should().BeEmpty();
        result.EvidenceLinks.Should().ContainSingle();
    }

    [Fact]
    public async Task ResolveConfiguredAsync_SnapshotStoreReturnsDifferentOwner_FailsClosed()
    {
        var store = Substitute.For<IPositionSnapshotStore>();
        store.GetSnapshotHistoryAsync(
                "run-a",
                "account-a",
                Arg.Any<PositionSnapshotOwnerScope>(),
                Arg.Any<DateTimeOffset>(),
                ValuationAsOf,
                Arg.Any<CancellationToken>())
            .Returns(_ => History(Snapshot("run-a", "account-a", ValuationAsOf) with { TenantId = "tenant-other" }));
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
        store.DidNotReceiveWithAnyArgs().GetSnapshotHistoryAsync(
            default!,
            default!,
            default!,
            default,
            default,
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
        securityMaster.GetRecordedByIdAsOfAsync(SecurityId, ValuationAsOf, Arg.Any<CancellationToken>())
            .Returns((SecurityDetailDto?)null);
        securityMaster.GetByIdAsOfAsync(SecurityId, ValuationAsOf, Arg.Any<CancellationToken>())
            .Returns(SecurityDetail(SecurityStatusDto.Active, "USD"));
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
        await securityMaster.DidNotReceive().GetByIdAsOfAsync(SecurityId, ValuationAsOf, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAdHocAsync_PostCutoffSecurityAlias_CannotAuthorizeValuation()
    {
        const string postCutoffAlias = "POSTCUT";
        var historical = HistoricalProjection(SecurityId);
        var current = historical with
        {
            Version = 2,
            Aliases =
            [
                new SecurityAliasDto(
                    Guid.NewGuid(),
                    SecurityId,
                    "Ticker",
                    postCutoffAlias,
                    Provider: null,
                    SecurityAliasScope.Operations,
                    Reason: "Added after valuation cutoff",
                    CreatedBy: "test",
                    CreatedAt: ValuationAsOf.AddMinutes(1),
                    ValidFrom: ValuationAsOf.AddMinutes(1),
                    ValidTo: null,
                    IsEnabled: true)
            ]
        };
        var eventStore = Substitute.For<ISecurityMasterEventStore>();
        eventStore.LoadAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new SecurityMasterEventEnvelope(
                GlobalSequence: 1,
                SecurityId,
                StreamVersion: 1,
                EventType: "SecCreated",
                EventTimestamp: ValuationAsOf.AddDays(-1),
                Actor: "test",
                CorrelationId: null,
                CausationId: null,
                Payload: JsonSerializer.SerializeToElement(
                    historical,
                    Meridian.Core.Serialization.SecurityMasterJsonContext.Default.SecurityProjectionRecord),
                Metadata: JsonSerializer.SerializeToElement(new { }))
        });
        var store = Substitute.For<ISecurityMasterStore>();
        store.GetProjectionAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(current);
        var snapshotStore = Substitute.For<ISecurityMasterSnapshotStore>();
        var securityMaster = new Meridian.Application.SecurityMaster.SecurityMasterQueryService(
            eventStore,
            store,
            new SecurityMasterAggregateRebuilder(eventStore, snapshotStore));
        var registry = Substitute.For<ICanonicalSymbolRegistry>();
        registry.GetDefinition(postCutoffAlias).Returns(new CanonicalSymbolDefinition
        {
            Canonical = postCutoffAlias,
            DisplayName = "Post-cutoff alias",
            SecurityId = SecurityId,
            AssetClass = "Equity",
            Exchange = "NASDAQ",
            Currency = "USD",
            Aliases = [postCutoffAlias]
        });
        var service = new DailyValuationPositionService(null, registry, securityMaster);

        var result = await service.ResolveAdHocAsync(
            [new MarkToMarketPosition(postCutoffAlias, 10m, 150m, "account-a")],
            "USD",
            ValuationAsOf);

        result.IsReady.Should().BeFalse();
        result.Positions.Should().BeEmpty();
        result.Blockers.Should().ContainSingle(message =>
            message.Contains("does not match authoritative Security Master record", StringComparison.Ordinal));
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
        securityMaster.GetRecordedByIdAsOfAsync(SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
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

    private static async IAsyncEnumerable<AccountSnapshotRecord> History(
        params AccountSnapshotRecord[] snapshots)
    {
        await Task.Yield();
        foreach (var snapshot in snapshots)
        {
            yield return snapshot;
        }
    }

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

    private static SecurityProjectionRecord HistoricalProjection(Guid securityId)
        => new(
            securityId,
            AssetClass: "Equity",
            SecurityStatusDto.Active,
            DisplayName: "Historical Security",
            Currency: "USD",
            PrimaryIdentifierKind: "Ticker",
            PrimaryIdentifierValue: "HIST",
            CommonTerms: JsonSerializer.SerializeToElement(new
            {
                displayName = "Historical Security",
                currency = "USD",
                exchange = "XNYS"
            }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1,
                shareClass = "Common",
                classification = "Common"
            }),
            Provenance: JsonSerializer.SerializeToElement(new
            {
                sourceSystem = "test",
                updatedBy = "codex",
                asOf = ValuationAsOf.AddDays(-1)
            }),
            Version: 1,
            EffectiveFrom: ValuationAsOf.AddYears(-1),
            EffectiveTo: null,
            Identifiers:
            [
                new SecurityIdentifierDto(
                    SecurityIdentifierKind.Ticker,
                    "HIST",
                    IsPrimary: true,
                    ValidFrom: ValuationAsOf.AddYears(-1))
            ],
            Aliases: []);

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
