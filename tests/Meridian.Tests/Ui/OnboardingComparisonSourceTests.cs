using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.FinancialOperations.AccountingSystem;
using Meridian.FinancialOperations.Onboarding;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Fixture = Meridian.Tests.Ui.OnboardingWorkspaceServiceTests.Fixture;

namespace Meridian.Tests.Ui;

public sealed class OnboardingComparisonSourceTests
{
    [Fact]
    public async Task MissingLiveDependencies_PreserveWorkspaceReadsButRefuseSourceCapture()
    {
        using var fixture = new SourceFixture();
        var source = new OnboardingComparisonSource(new AccountingSystemIntegrationService([]));
        var service = fixture.Service(source);
        var workspace = await service.CreateAsync(fixture.Request, Fixture.Owner);
        (await service.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId)).Should().NotBeNull();
        await Assert.ThrowsAsync<OnboardingValidationException>(() => source.GetSelectionAsync(workspace));
        await Assert.ThrowsAsync<OnboardingValidationException>(() => fixture.StoreFixture.Compare(
            service, workspace, Fixture.January, "missing-import"));
        (await service.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!.Comparisons.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingProviderPositions_RetainBlockedComparisonWithoutInventingZeroPositions()
    {
        using var fixture = new SourceFixture();
        var source = fixture.Source();
        var service = fixture.Service(source);
        var workspace = await service.CreateAsync(fixture.Request, Fixture.Owner);
        workspace = await fixture.StoreFixture.Compare(service, workspace, Fixture.January, "missing-import");
        var capture = workspace.Comparisons.Single().Inputs;
        capture.Observations.Should().NotContain(row => row.Kind == "Position");
        capture.MissingSources.Should().Contain(row => row.SourceKind == "Position");
        capture.MissingSources.Should().Contain(row => row.SourceKind == "ExternalGl");
        capture.Snapshots.Should().Contain(snapshot => snapshot.SourceKind == "AccountMembership");
        workspace.Comparisons.Single().CoveragePercent.Should().Be(0);
        workspace.Readiness.IsReady.Should().BeFalse();
        workspace.AuthorityPosture.Should().Be(OnboardingWorkspaceService.ReadOnlyAuthorityPosture);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("book")]
    [InlineData("entity")]
    [InlineData("account")]
    public async Task InvalidSelectedScope_IsRejectedBeforeFinancialSourceReads(string mismatch)
    {
        using var fixture = new SourceFixture();
        var workspace = await fixture.StoreFixture.Service().CreateAsync(fixture.Request, Fixture.Owner);
        workspace = workspace with
        {
            Scope = mismatch switch
            {
                "tenant" => workspace.Scope with { TenantId = "foreign-tenant" },
                "company" => workspace.Scope with { CompanyId = "foreign-company" },
                "book" => workspace.Scope with { LedgerBookId = Guid.NewGuid() },
                "entity" => workspace.Scope with { EntityId = Guid.NewGuid().ToString("D") },
                _ => workspace.Scope with { AccountIds = [Guid.NewGuid().ToString("D")] }
            }
        };
        await Assert.ThrowsAsync<OnboardingValidationException>(() => fixture.Source().CaptureAsync(workspace,
            new(workspace.Version, Fixture.January, "provider", "import", "map", "version")));
        fixture.Accounts.Verify(query => query.GetBalanceTimelineAsync(It.IsAny<Guid>(), It.IsAny<DateOnly?>(),
            It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Accounts.Verify(query => query.GetReconciliationRunsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpeningReconciliationContents_AreFrozenWithTheirActualAmountsAndOriginalDate()
    {
        using var fixture = new SourceFixture();
        var runId = Guid.NewGuid();
        var run = new AccountReconciliationRunDto(runId, fixture.Account.AccountId, Fixture.January, "Completed",
            1, 0, 1, 2m, Fixture.Now.AddDays(-2), Fixture.Now.AddDays(-1), "reconciler");
        var results = new List<AccountReconciliationResultDto>
        {
            new(Guid.NewGuid(), runId, "Opening cash", false, "Balance", "Break", 102.001m, 100.001m, -2m, "Cash mismatch.")
        };
        fixture.Accounts.Setup(query => query.GetReconciliationRunsAsync(fixture.Account.AccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([run]);
        fixture.Accounts.Setup(query => query.GetReconciliationResultsAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(results);
        var service = fixture.Service(fixture.Source());
        var workspace = await service.CreateAsync(fixture.Request, Fixture.Owner);
        workspace = await fixture.StoreFixture.Compare(service, workspace, Fixture.February, "missing-gl");
        var comparison = workspace.Comparisons.Single();
        var opening = comparison.Inputs.Snapshots.Single(snapshot => snapshot.SourceKind == "OpeningBalance");
        using var payload = JsonDocument.Parse(opening.PayloadJson);
        payload.RootElement.GetProperty("run").GetProperty("asOfDate").GetString().Should().Be("2026-01-31");
        var difference = comparison.Differences.Single(row => row.InstrumentId == "opening:Opening cash");
        difference.MeridianAmount.Should().Be(102.001m);
        difference.ExternalAmount.Should().Be(100.001m);
        difference.Difference.Should().Be(2m);
        var original = JsonSerializer.Serialize(comparison);
        results[0] = results[0] with { ActualAmount = 102.001m, IsMatch = true, Status = "Matched" };
        JsonSerializer.Serialize(await fixture.StoreFixture.Service().ReplayComparisonAsync(Fixture.Tenant, Fixture.Company,
            workspace.WorkspaceId, comparison.ComparisonId)).Should().Be(original);
    }

    [Fact]
    public async Task PositionSourceFromAnotherAccountOrDate_CannotEnterTheRetainedPacket()
    {
        using var fixture = new SourceFixture();
        fixture.Accounts.Setup(query => query.GetCustodianPositionsAsync(fixture.Account.AccountId,
                It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Fixture.January,
                "AAPL", "Ticker", 10m, 100m, "USD", "Apple", "Equity", false)]);
        var service = fixture.Service(fixture.Source());
        var workspace = await service.CreateAsync(fixture.Request, Fixture.Owner);
        await Assert.ThrowsAsync<OnboardingValidationException>(() => fixture.StoreFixture.Compare(service, workspace, Fixture.January, "import"));
        (await service.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId))!.Comparisons.Should().BeEmpty();
    }

    [Fact]
    public async Task HistoricalProviderAsOf_AcceptsLaterReceiptAndReplaysAfterRawSourceDisappears()
    {
        using var fixture = new SourceFixture();
        var (source, rawPath) = await RetainProviderComparison(fixture, Fixture.January, 0m);
        var service = fixture.Service(source);
        var workspace = await service.CreateAsync(fixture.Request, Fixture.Owner);
        workspace = await fixture.StoreFixture.Compare(service, workspace, Fixture.January, "missing-gl");
        var comparison = workspace.Comparisons.Single();
        comparison.Inputs.MissingSources.Should().NotContain(row => row.SourceKind == "ProviderLedger");
        comparison.Inputs.Observations.Should().Contain(row => row.Kind == "Balance"
            && row.MeridianAmount == 100m && row.ExternalAmount == 100m && row.MissingReason == null);
        comparison.Inputs.Observations.Should().Contain(row => row.Kind == "Nav"
            && row.MeridianAmount == 200m && row.ExternalAmount == 200m && row.MissingReason == null);
        comparison.Inputs.Observations.Should().Contain(row => row.Kind == "Position" && row.InstrumentId == "position-quantity:AAPL");
        comparison.Inputs.Snapshots.Single(row => row.SourceKind == "ProviderSource").PayloadJson.Should().Contain("2026-01-31");
        var frozen = JsonSerializer.Serialize(comparison);
        File.Delete(rawPath);
        JsonSerializer.Serialize(await fixture.StoreFixture.Service().ReplayComparisonAsync(Fixture.Tenant, Fixture.Company,
            workspace.WorkspaceId, comparison.ComparisonId)).Should().Be(frozen);
    }

    [Fact]
    public async Task WrongEconomicDate_IsMissingEvenWhenProviderReceiptMatchesComparison()
    {
        using var fixture = new SourceFixture();
        var (source, _) = await RetainProviderComparison(fixture, Fixture.February, 0m);
        var service = fixture.Service(source);
        var workspace = await service.CreateAsync(fixture.Request, Fixture.Owner);
        workspace = await fixture.StoreFixture.Compare(service, workspace, Fixture.January, "missing-gl");
        workspace.Comparisons.Single().Inputs.MissingSources.Should().Contain(row => row.SourceKind == "ProviderLedger");
        workspace.Comparisons.Single().Inputs.Observations.Should().NotContain(row => row.Kind == "Position");
    }

    [Fact]
    public async Task MissingNavComponent_CannotBeInterpretedAsZero()
    {
        using var fixture = new SourceFixture();
        var (source, _) = await RetainProviderComparison(fixture, Fixture.January, null);
        var service = fixture.Service(source);
        var workspace = await service.CreateAsync(fixture.Request, Fixture.Owner);
        workspace = await fixture.StoreFixture.Compare(service, workspace, Fixture.January, "missing-gl");
        var nav = workspace.Comparisons.Single().Inputs.Observations.Single(row => row.Kind == "Nav");
        nav.MissingReason.Should().NotBeNullOrWhiteSpace();
        workspace.Comparisons.Single().Inputs.MissingSources.Should().Contain(row => row.SourceKind == "Nav");
        workspace.Readiness.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task ChangedInternalPosition_CannotReinterpretAnEarlierProviderComparison()
    {
        using var fixture = new SourceFixture();
        var (source, _) = await RetainProviderComparison(fixture, Fixture.January, 0m);
        fixture.Accounts.Setup(query => query.GetCustodianPositionsAsync(fixture.Account.AccountId, It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>())).ReturnsAsync([new(Guid.NewGuid(), Guid.NewGuid(), fixture.Account.AccountId,
                    Fixture.January, "AAPL", "Ticker", 12m, 120m, "USD", "Apple", "Equity", false)]);
        var service = fixture.Service(source);
        var workspace = await service.CreateAsync(fixture.Request, Fixture.Owner);
        workspace = await fixture.StoreFixture.Compare(service, workspace, Fixture.January, "missing-gl");
        var position = workspace.Comparisons.Single().Inputs.Observations.Single(row => row.Kind == "Position");
        position.MissingReason.Should().NotBeNullOrWhiteSpace();
        workspace.Comparisons.Single().Inputs.MissingSources.Should().Contain(row => row.SourceKind == "Position");
        workspace.Readiness.IsReady.Should().BeFalse();
    }

    private static async Task<(OnboardingComparisonSource Source, string RawPath)> RetainProviderComparison(
        SourceFixture fixture, DateOnly economicDate, decimal? accruedInterest)
    {
        const string provider = "custodian";
        const string external = "selected-external-account";
        var root = Path.Combine(Path.GetDirectoryName(fixture.StoreFixture.StorePath)!, "brokerage");
        var options = new BrokeragePortfolioSyncOptions(root, TimeSpan.FromHours(1), provider);
        var account = fixture.Account;
        var accountId = account.AccountId;
        var scope = fixture.Request.Scope;
        var synced = Fixture.Now;
        var created = synced.AddMinutes(1);
        var book = new LedgerBookDto(scope.LedgerBookId, scope.FundProfileId, accountId,
            FundStructureNodeKindDto.Account, "Account book", "USD", created, created);
        fixture.Books.Setup(query => query.GetBookAsync(scope.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        fixture.Books.Setup(query => query.ListBooksAsync(It.IsAny<LedgerBookQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync([book]);
        var fundAccounts = new Mock<IFundAccountService>(MockBehavior.Strict);
        fundAccounts.Setup(query => query.GetAccountAsync(accountId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        fixture.Accounts.Setup(query => query.GetBalanceTimelineAsync(accountId, It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(),
                It.IsAny<CancellationToken>())).ReturnsAsync([new(Guid.NewGuid(), accountId, account.FundId, Fixture.January,
                    "USD", 100m, 100m, accruedInterest, 0m, "retained-ledger", synced.AddHours(-1), "ledger:evidence")]);
        fixture.Accounts.Setup(query => query.GetCustodianPositionsAsync(accountId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new(Guid.NewGuid(), Guid.NewGuid(), accountId, Fixture.January, "AAPL", "Ticker", 10m, 100m,
                "USD", "Apple", "Equity", false)]);
        var sync = new BrokeragePortfolioSyncService(options, [], [], [], Mock.Of<IServiceProvider>(),
            NullLogger<BrokeragePortfolioSyncService>.Instance);
        var providerComparisons = new ProviderLedgerReconciliationService(sync, fundAccounts.Object, options,
            NullLogger<ProviderLedgerReconciliationService>.Instance, ledgerBookService: fixture.Books.Object,
            fundProfileTenancyRegistry: fixture.Tenancy.Object);
        var rawPath = Path.Combine(root, "raw", "source.json");
        var projectionPath = Path.Combine(root, "projections", accountId.ToString("N"), "current.json");
        var detailPath = Path.Combine(root, "reconciliation", accountId.ToString("N"), "latest.json");
        var asOf = new DateTimeOffset(economicDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var portfolio = new BrokeragePortfolioSnapshotDto(
            new(provider, external, "Selected external account", "Active", "USD", synced),
            new(100m, 200m, 100m, "USD"),
            [new("AAPL", 10m, 10m, 10m, 100m, 0m, "Equity", Currency: "USD")], synced,
            new(provider, external, asOf, "USD", "Active", BrokerageMarginRegime.Cash, 100m, 200m, 100m), IsComplete: true);
        var link = new WorkstationBrokerageAccountLinkDto(accountId, provider, external, "Selected external account", synced);
        var status = new WorkstationBrokerageSyncStatusDto(accountId, provider, external, WorkstationBrokerageSyncHealth.Healthy,
            true, false, synced, synced, null, 1, 0, 0, 0, 0, []);
        var projection = new FundAccountBrokerageSyncActivityDto(accountId, link, status, new(100m, 200m, 100m, "USD", 0m),
            [new("AAPL", 10m, 10m, 10m, 100m, 0m, "Equity", null, Currency: "USD")], [], [], [], synced, rawPath, projectionPath);
        var summary = new ProviderLedgerReconciliationSummaryDto(Guid.NewGuid(), accountId, created,
            ProviderLedgerReconciliationStatusDto.Matched, 3, 3, 0, 0, 0.01m, 60, provider, external, synced, Fixture.January, detailPath);
        var shadow = new ProviderShadowBookComparisonDto(accountId, created, "USD", 3, 3, 0, 0,
            [Line("account-cash", 100m), Line("total-equity", 200m), Line("position-quantity:AAPL", 10m)]);
        var detail = new ProviderLedgerReconciliationDetailDto(summary, [], [], [], [], ShadowBookComparison: shadow);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        foreach (var (path, value) in new (string, object)[]
                 {
                     (rawPath, new { CapturedAt = synced, ProviderId = provider, ExternalAccountId = external, Portfolio = portfolio }),
                     (projectionPath, projection), (detailPath, detail)
                 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, json));
        }
        return (new(fixture.Accounting, fixture.Accounts.Object, fixture.Books.Object, fixture.Tenancy.Object,
            providerComparisons, brokerageSnapshots: sync, brokerageOptions: options), rawPath);

        static ProviderShadowBookComparisonLineDto Line(string dimension, decimal amount) => new(dimension, dimension,
            "Internal ledger", "Provider source", amount, amount, 0m, ProviderLedgerReconciliationCheckStatusDto.Matched, "Matched.");
    }

    private sealed class SourceFixture : IDisposable
    {
        internal Fixture StoreFixture { get; } = new();
        internal Mock<IAccountQueryService> Accounts { get; } = new(MockBehavior.Strict);
        internal Mock<ILedgerBookService> Books { get; } = new(MockBehavior.Strict);
        internal Mock<IFundProfileTenancyRegistry> Tenancy { get; } = new(MockBehavior.Strict);
        internal AccountingSystemIntegrationService Accounting { get; } = new([]);
        internal AccountSummaryDto Account { get; }
        internal CreateOnboardingWorkspaceRequestDto Request { get; }

        internal SourceFixture()
        {
            var scope = StoreFixture.Scope;
            var fundId = Guid.Parse("e2a06b5e-a89d-4697-974b-0a2e9463f9d2");
            Request = StoreFixture.Request() with { Scope = scope with { FundProfileId = fundId.ToString("D") } };
            Account = new(Guid.Parse(Fixture.Account), default, Guid.Parse(scope.EntityId), fundId, null, null,
                "ACCOUNT", "Selected account", "USD", "Custodian", true, Fixture.Now.AddYears(-1), null,
                null, scope.LedgerBookId.ToString("D"), null, null);
            var book = new LedgerBookDto(scope.LedgerBookId, Request.Scope.FundProfileId, fundId,
                FundStructureNodeKindDto.Fund, "Selected book", "USD", Fixture.Now, Fixture.Now);
            Books.Setup(query => query.GetBookAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => id == book.LedgerBookId ? book : null);
            Tenancy.Setup(query => query.ResolveAsync(Request.Scope.FundProfileId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundProfileOwnership(Request.Scope.FundProfileId, Fixture.Tenant, Fixture.Company));
            Accounts.Setup(query => query.GetAccountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => id == Account.AccountId ? Account : null);
            Accounts.Setup(query => query.GetBalanceTimelineAsync(Account.AccountId, It.IsAny<DateOnly?>(),
                It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Accounts.Setup(query => query.GetCustodianPositionsAsync(Account.AccountId, It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Accounts.Setup(query => query.GetReconciliationRunsAsync(Account.AccountId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        }

        internal OnboardingComparisonSource Source() => new(Accounting, Accounts.Object, Books.Object, Tenancy.Object);
        internal OnboardingWorkspaceService Service(OnboardingComparisonSource source)
            => new(new FileOnboardingWorkspaceStore(StoreFixture.StorePath), source);
        public void Dispose() => StoreFixture.Dispose();
    }
}
