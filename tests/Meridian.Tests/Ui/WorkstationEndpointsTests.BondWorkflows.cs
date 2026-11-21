using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Tenancy;
using Meridian.Identity.Auth;
using Meridian.Instruments.FixedIncome;
using Meridian.PortfolioRecords.Accounts;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Storage.FixedIncome;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task BondWorkflowAccountRouteUsesAuthenticatedAccountAndRouteSecurity()
    {
        var book = Guid.NewGuid();
        var account = Guid.NewGuid();
        var security = Guid.NewGuid();
        var model = Substitute.For<IBondWorkflowService>();
        model.CalculateAsync(Arg.Any<BondWorkflowRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<BondWorkflowResult?>(BondWorkflowEndpointsTests.Echo(call.ArgAt<BondWorkflowRequest>(0))));
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(book, OversightBook(book, account)).Object);
            services.AddSingleton(model);
        }, currentUserPermissions: UserPermission.ViewLedgerReports, mapLedgerApi: true);
        var request = new BondWorkflowRequest(Guid.NewGuid(), BondWorkflowEndpointsTests.Context(), 1234.56789m)
        { AccountId = "forged-pricing-account" };
        using var response = await app.GetTestClient().PostAsJsonAsync(
            OversightBaseRoute(book, account) + $"/bond-workflows/{security:D}", request, BondWorkflowEndpointsTests.Transport);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await model.Received(1).CalculateAsync(Arg.Is<BondWorkflowRequest>(row => row.SecurityId == security
            && row.AccountId == account.ToString("D") && row.OriginalFace == 1234.56789m), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("account")]
    [InlineData("tenant")]
    [InlineData("permission")]
    public async Task BondAccountRouteRejectsUnauthorizedAccountOwnershipBeforeCalculation(string defect)
    {
        var book = Guid.NewGuid();
        var account = Guid.NewGuid();
        var security = Guid.NewGuid();
        var model = Substitute.For<IBondWorkflowService>();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(book, OversightBook(book, account)).Object);
            services.AddSingleton(OversightOwnership(new("oversight-fund", defect == "tenant" ? "other-tenant" : "tenant-test", "tenant-test")).Object);
            services.AddSingleton(model);
        }, currentUserPermissions: defect == "permission" ? UserPermission.ViewMarketData : UserPermission.ViewLedgerReports,
            mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync(
            OversightBaseRoute(book, defect == "account" ? Guid.NewGuid() : account) + $"/bond-workflows/{security:D}",
            new BondWorkflowRequest(security, BondWorkflowEndpointsTests.Context()), BondWorkflowEndpointsTests.Transport);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await model.DidNotReceiveWithAnyArgs().CalculateAsync(default!, default);
    }

    [Fact]
    public async Task BondIncomePlanVerifiesNativeSourceBytesAndBindsAuthenticatedScopeThroughActualServices()
    {
        using var fixture = await BondEndpointFixture.CreateAsync();
        await using var app = await CreateAppAsync(fixture.Configure,
            currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserTenantId: fixture.Scope.TenantId, currentUserCompanyId: fixture.Scope.CompanyId, mapLedgerApi: true);
        var request = fixture.PlanningRequest() with { Scope = fixture.Scope with { TenantId = "forged", CompanyId = "forged" } };
        var tampered = request with { OpeningBalances = [request.OpeningBalances[0] with { Amount = request.OpeningBalances[0].Amount + 1m }] };
        using var rejected = await app.GetTestClient().PostAsJsonAsync(fixture.Route + "/bond-income/preview", tampered, BondWorkflowEndpointsTests.Transport);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("CASH_FORECAST_SOURCE_AUTHORITY_REQUIRED");
        using var response = await app.GetTestClient().PostAsJsonAsync(fixture.Route + "/bond-income/preview", request, BondWorkflowEndpointsTests.Transport);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = (await response.Content.ReadFromJsonAsync<AccountBondIncomePlanDto>(BondWorkflowEndpointsTests.Transport))!;
        plan.Scope.Should().Be(fixture.Scope);
        plan.Calculations.Should().ContainSingle().Which.IsBlocked.Should().BeFalse();
        plan.MonitoringInputs.Obligations.Should().NotBeEmpty();
        plan.HoldingsBatchId.Should().Be(fixture.Batch);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("currencyLadders")[0].GetProperty("openingCash").ValueKind.Should().Be(JsonValueKind.String);
        (await fixture.Workspace.ReadAsync<AccountBondIncomePlanDto>(fixture.Scope, "income-plan", plan.MonitoringInputs.RunId))!
            .MonitoringInputs.Obligations.Should().BeEquivalentTo(plan.MonitoringInputs.Obligations);
    }

    [Fact]
    public async Task BondMonitoringUsesRetainedPlanFactsAndSupportsExactRetriesAndIndependentCorrectionRuns()
    {
        using var fixture = await BondEndpointFixture.CreateAsync();
        await using var app = await CreateAppAsync(fixture.Configure,
            currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserTenantId: fixture.Scope.TenantId, currentUserCompanyId: fixture.Scope.CompanyId, mapLedgerApi: true);
        var client = app.GetTestClient();
        using var preview = await client.PostAsJsonAsync(fixture.Route + "/bond-income/preview", fixture.PlanningRequest(), BondWorkflowEndpointsTests.Transport);
        preview.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = (await preview.Content.ReadFromJsonAsync<AccountBondIncomePlanDto>(BondWorkflowEndpointsTests.Transport))!;
        var changedEconomics = plan.MonitoringInputs with
        { Obligations = plan.MonitoringInputs.Obligations.Select(row => row with { Amount = row.Amount + 1m }).ToArray() };
        using var rejected = await client.PostAsJsonAsync(fixture.Route + "/bond-monitoring", changedEconomics, BondWorkflowEndpointsTests.Transport);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await fixture.Monitor.GetHistoryAsync(fixture.Scope)).Should().BeEmpty();

        var forgedFacts = plan.MonitoringInputs with
        {
            Scope = fixture.Scope with { AccountId = "foreign", TenantId = "foreign" },
            CalculationContext = fixture.Context with { KnownAt = DateTimeOffset.UtcNow.AddYears(1) },
            Resets = [new(fixture.Security, fixture.Date, fixture.Date, 9m, true, "forged", [])],
            ExerciseWindows = [new(fixture.Security, 1, fixture.Date, 90, [], [], ["forged server fact"])]
        };
        using var firstResponse = await client.PostAsJsonAsync(fixture.Route + "/bond-monitoring", forgedFacts, BondWorkflowEndpointsTests.Transport);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = (await firstResponse.Content.ReadFromJsonAsync<BondMonitoringSnapshotDto>(BondWorkflowEndpointsTests.Transport))!;
        first.Inputs.Scope.Should().Be(fixture.Scope);
        first.Inputs.CalculationContext.Should().Be(plan.MonitoringInputs.CalculationContext);
        first.Inputs.Resets.Should().BeEquivalentTo(plan.MonitoringInputs.Resets);
        first.Inputs.ExerciseWindows.Should().BeEquivalentTo(plan.MonitoringInputs.ExerciseWindows);
        using var retryResponse = await client.PostAsJsonAsync(fixture.Route + "/bond-monitoring", forgedFacts, BondWorkflowEndpointsTests.Transport);
        retryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retry = (await retryResponse.Content.ReadFromJsonAsync<BondMonitoringSnapshotDto>(BondWorkflowEndpointsTests.Transport))!;
        retry.SnapshotId.Should().Be(first.SnapshotId);
        (await fixture.Monitor.GetHistoryAsync(fixture.Scope)).Should().ContainSingle();
        var correction = forgedFacts with
        {
            RunId = "correction-" + Guid.NewGuid().ToString("N"),
            PlanningRunId = plan.MonitoringInputs.RunId,
            PreviousSnapshotId = first.SnapshotId,
            CorrectionRationale = "Retain reviewed follow-up without rewriting the original source plan."
        };
        using var nextResponse = await client.PostAsJsonAsync(fixture.Route + "/bond-monitoring", correction, BondWorkflowEndpointsTests.Transport);
        nextResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var next = (await nextResponse.Content.ReadFromJsonAsync<BondMonitoringSnapshotDto>(BondWorkflowEndpointsTests.Transport))!;
        next.SnapshotId.Should().NotBe(first.SnapshotId);
        next.Inputs.PlanningRunId.Should().Be(plan.MonitoringInputs.RunId);
        (await fixture.Monitor.GetHistoryAsync(fixture.Scope)).Should().HaveCount(2);
    }

    [Fact]
    public async Task BondComparisonSaveAndRetrieveRemainImmutableAndKeepExactDecimalTransport()
    {
        using var fixture = await BondEndpointFixture.CreateAsync();
        await using var app = await CreateAppAsync(fixture.Configure,
            currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserTenantId: fixture.Scope.TenantId, currentUserCompanyId: fixture.Scope.CompanyId, mapLedgerApi: true);
        var client = app.GetTestClient();
        var request = new BondComparisonRequest("comparison-" + Guid.NewGuid().ToString("N"), fixture.Context,
            [new(fixture.Security, fixture.Context, 1000.123456789m, 100m) { AccountId = "forged-account" }]);
        using var saved = await client.PostAsJsonAsync(fixture.Route + "/bond-comparisons", request, BondWorkflowEndpointsTests.Transport);
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        using var retained = await client.GetAsync(fixture.Route + "/bond-comparisons/" + request.RunId);
        retained.StatusCode.Should().Be(HttpStatusCode.OK);
        var original = await retained.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(original);
        json.RootElement.GetProperty("request").GetProperty("bonds")[0].GetProperty("accountId").GetString().Should().Be(fixture.Scope.AccountId);
        json.RootElement.GetProperty("request").GetProperty("bonds")[0].GetProperty("originalFace").ValueKind.Should().Be(JsonValueKind.String);
        json.RootElement.GetProperty("request").GetProperty("bonds")[0].GetProperty("originalFace").GetString().Should().Be("1000.123456789");
        using var conflict = await client.PostAsJsonAsync(fixture.Route + "/bond-comparisons", request with
        { Bonds = [request.Bonds[0] with { OriginalFace = 2000m }] }, BondWorkflowEndpointsTests.Transport);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.GetStringAsync(fixture.Route + "/bond-comparisons/" + request.RunId)).Should().Be(original);
    }

    [Theory]
    [InlineData("Fixed")]
    [InlineData("ZeroCoupon")]
    [InlineData("SimpleFloater")]
    [InlineData("Overnight")]
    [InlineData("Inflation")]
    [InlineData("Amortizer")]
    [InlineData("SignedCoupon")]
    public async Task AllBondFamiliesPlanTwelveMonthsFromActuallyRetainedAcceptedHoldingsAndCompareQuotes(string family)
    {
        using var fixture = await BondEndpointFixture.CreateAsync(family);
        await using var app = await CreateAppAsync(fixture.Configure,
            currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserTenantId: fixture.Scope.TenantId, currentUserCompanyId: fixture.Scope.CompanyId, mapLedgerApi: true);
        var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync(fixture.Route + "/bond-income/preview", fixture.PlanningRequest(), BondWorkflowEndpointsTests.Transport);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = (await response.Content.ReadFromJsonAsync<AccountBondIncomePlanDto>(BondWorkflowEndpointsTests.Transport))!;
        plan.Blockers.Should().BeEmpty();
        plan.HoldingsBatchId.Should().Be(fixture.Batch);
        plan.HoldingsContentHash.Should().NotBeNullOrWhiteSpace();
        plan.Calculations.Should().ContainSingle().Which.IsBlocked.Should().BeFalse();
        var ladder = plan.CurrencyLadders.Should().ContainSingle().Which;
        ladder.BaseCurrency.Should().Be("USD");
        ladder.IsDecisionReady.Should().BeTrue();
        ladder.Contributions.Should().OnlyContain(row => row.SecurityId == fixture.Security && row.Currency == "USD");
        ladder.Contributions.Sum(row => row.Amount).Should().Be(plan.MonitoringInputs.Obligations
            .Where(row => row.DueDate >= plan.PeriodStart && row.DueDate <= plan.PeriodEnd).Sum(row => row.Amount));
        ladder.Buckets.Last().CumulativeCash.Should().Be(ladder.OpeningCash + ladder.Contributions.Sum(row => row.Amount));
        if (family == "SignedCoupon")
            ladder.Contributions.Should().Contain(row => row.FlowType == "Coupon" && row.Amount < 0m);
        if (family == "ZeroCoupon")
            ladder.Contributions.Should().NotContain(row => row.FlowType == "Coupon");
        if (family == "Inflation")
            ladder.Contributions.Where(row => row.FlowType == "Principal").Sum(row => row.Amount).Should().Be(1100m);
        if (family == "Amortizer")
            ladder.Contributions.Where(row => row.FlowType == "Principal").Select(row => row.Amount).Should().BeEquivalentTo(new[] { 400m, 600m });
        if (family == "SimpleFloater" || family == "Overnight")
            plan.MonitoringInputs.Resets.Should().NotBeEmpty();
        plan.Calculations[0].Issues.Should().Contain(row => row.Code == "DiscountCurveMissing" && !row.BlocksCalculation);

        var compare = new BondComparisonRequest("families-" + Guid.NewGuid().ToString("N"), fixture.Context,
            [new(fixture.Security, fixture.Context, 1000m, 100m), new(fixture.Security, fixture.Context, 1000m, 101m)]);
        using var compared = await client.PostAsJsonAsync(fixture.Route + "/bond-comparisons", compare, BondWorkflowEndpointsTests.Transport);
        compared.StatusCode.Should().Be(HttpStatusCode.OK);
        var alternatives = (await compared.Content.ReadFromJsonAsync<BondComparisonResult>(BondWorkflowEndpointsTests.Transport))!;
        alternatives.Bonds.Should().HaveCount(2).And.OnlyContain(row => !row.IsBlocked);
        alternatives.Bonds[1].DirtyConsideration.Should().BeGreaterThan(alternatives.Bonds[0].DirtyConsideration!.Value);
        alternatives.Bonds[1].Yield.Should().BeLessThan(alternatives.Bonds[0].Yield!.Value);
        (await fixture.Workspace.ReadAsync<AccountBondIncomePlanDto>(fixture.Scope, "income-plan", plan.MonitoringInputs.RunId))!
            .MonitoringInputs.Obligations.Should().BeEquivalentTo(plan.MonitoringInputs.Obligations);
    }

    [Fact]
    public async Task AccountFloaterPlanRetainsExplicitCoverageGapWhenFutureIndexPopulationIsMissing()
    {
        using var fixture = await BondEndpointFixture.CreateAsync("SimpleFloater", missingForward: true);
        await using var app = await CreateAppAsync(fixture.Configure,
            currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserTenantId: fixture.Scope.TenantId, currentUserCompanyId: fixture.Scope.CompanyId, mapLedgerApi: true);
        using var response = await app.GetTestClient().PostAsJsonAsync(fixture.Route + "/bond-income/preview", fixture.PlanningRequest(), BondWorkflowEndpointsTests.Transport);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = (await response.Content.ReadFromJsonAsync<AccountBondIncomePlanDto>(BondWorkflowEndpointsTests.Transport))!;
        plan.Blockers.Should().Contain(row => row.Contains("ForwardCurveMissing"));
        plan.BaseCurrencyLadder.IsDecisionReady.Should().BeFalse();
        plan.MonitoringInputs.Obligations.Should().BeEmpty();
        plan.CurrencyLadders.Should().ContainSingle().Which.Contributions.Should().BeEmpty();
    }

    private sealed class BondEndpointFixture : IDisposable
    {
        private readonly AccountCashForecastEvidenceAdapterTests _source;
        private readonly AccountCashForecastRequestDto _cash;
        public Guid Security { get; } = Guid.NewGuid();
        public Guid Batch { get; } = Guid.NewGuid();
        public AccountCashForecastScopeDto Scope => _cash.Scope;
        public DateOnly Date => _cash.PeriodStart;
        public BondCalculationContext Context { get; private set; }
        public BondMonitoringService Monitor { get; }
        public BondWorkspaceSnapshotStore Workspace { get; }
        private readonly BondWorkflowService _model;
        private readonly IAccountBondHoldingsSource _holdings;
        private readonly FileReconciliationBreakQueueRepository _queue;
        public string Route => OversightBaseRoute(Guid.Parse(Scope.BookId), Guid.Parse(Scope.AccountId));

        private BondEndpointFixture(AccountCashForecastEvidenceAdapterTests source, AccountCashForecastRequestDto cash,
            string? family = null, InMemoryFundAccountService? accounts = null, bool missingForward = false)
        {
            _source = source;
            _cash = cash;
            var economic = new DateTimeOffset(Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            Context = new(economic, Date, DateTimeOffset.UtcNow);
            _model = family is null ? BondWorkflowEndpointsTests.RealService(Security, economic)
                : BondFamilyAcceptance.Service(Security, economic, family, missingForward);
            if (accounts is not null)
                _holdings = new AccountBondHoldingsSource(accounts);
            else
            {
                _holdings = Substitute.For<IAccountBondHoldingsSource>();
                _holdings.GetHoldingsAsync(Guid.Parse(Scope.AccountId), Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
                    .Returns(new AccountBondHoldingsSnapshotDto(Guid.Parse(Scope.AccountId), Date, Batch, Sha256Digest.ComputeUtf8("synthetic accepted holdings"), "synthetic-verified", HoldingLines()));
            }
            Monitor = new(new BondMonitoringFileStore(source.Root), _holdings, _model);
            Workspace = new(source.Root);
            _queue = new(Path.Combine(source.Root, "casework"), NullLogger<FileReconciliationBreakQueueRepository>.Instance);
        }
        public static async Task<BondEndpointFixture> CreateAsync(string? family = null, bool missingForward = false)
        {
            var source = new AccountCashForecastEvidenceAdapterTests();
            var cash = await source.ValidRequestAsync();
            if (family is null)
                return new(source, cash);
            var path = Path.Combine(source.Root, "accepted-family-holdings.json");
            var accounts = new InMemoryFundAccountService(path);
            var account = Guid.Parse(cash.Scope.AccountId);
            await accounts.CreateAccountAsync(new(account, AccountTypeDto.Custody, "SYNTHETIC-" + account.ToString("N"),
                "Synthetic family acceptance", "USD", DateTimeOffset.UtcNow.AddDays(-1), "synthetic"));
            var fixture = new BondEndpointFixture(source, cash, family, accounts, missingForward);
            await accounts.IngestCustodianStatementAsync(new(fixture.Batch, account, fixture.Date, "Synthetic custodian",
                "synthetic-family-v1", null, fixture.HoldingLines(), "synthetic-reviewer",
                Authority: new(1, "synthetic-accepted-source", Sha256Digest.ComputeUtf8("accepted-" + family))));
            // The accepted batch was retained after fixture construction; pin knowledge after acceptance.
            fixture.Context = fixture.Context with { KnownAt = DateTimeOffset.UtcNow };
            var restarted = new AccountBondHoldingsSource(new InMemoryFundAccountService(path));
            var accepted = await restarted.GetHoldingsAsync(account, fixture.Date, fixture.Context.KnownAt);
            accepted!.BatchId.Should().Be(fixture.Batch);
            accepted.Positions.Should().ContainSingle().Which.SecurityId.Should().Be(fixture.Security);
            return fixture;
        }
        private CustodianPositionLineDto[] HoldingLines() => [new(Guid.NewGuid(), Batch, Guid.Parse(Scope.AccountId), Date,
            "SYNTHETIC-BOND", "SecurityId", 1000m, 1000m, "USD", "Synthetic bond", "Bond", false,
            SecurityId: Security, Bond: new(1000m, 1000m, 1m, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace, 100m, false, 0m,
                SourceEvidenceId: "synthetic-accepted-source", OriginalSourceJson: "{}", InvoiceBasis: BondHoldingInvoiceBasis.Nominal))];
        public AccountBondIncomePlanningRequest PlanningRequest() => new(Scope, Date, Date.AddDays(365), Context, [_cash.OpeningCash!], []);
        public void Configure(IServiceCollection services)
        {
            var book = Guid.Parse(Scope.BookId);
            var account = Guid.Parse(Scope.AccountId);
            services.AddSingleton(OversightBooks(book, OversightBook(book, account)).Object);
            services.AddSingleton(OversightOwnership(new("oversight-fund", Scope.TenantId, Scope.CompanyId)).Object);
            services.AddSingleton<IEvidenceArtifactStore>(_source.Store());
            services.AddSingleton<IBondWorkflowService>(_model);
            services.AddSingleton(_holdings);
            services.AddSingleton<IBondMonitoringService>(Monitor);
            services.AddSingleton<IAccountBondIncomePlanningService>(new AccountBondIncomePlanningService(_holdings, _model, Monitor));
            services.AddSingleton(Workspace);
            services.AddSingleton<IBondComparisonService>(new BondComparisonService(_model));
            services.AddSingleton<IReconciliationBreakQueueRepository>(_queue);
            services.AddSingleton(new BondCollectionCaseworkProjection(_queue, Monitor));
        }
        public void Dispose() => _source.Dispose();
    }
}
