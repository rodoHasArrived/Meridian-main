using System.Globalization;
using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.Tests.Reconciliation;

public sealed partial class StatementRunWorkflowServiceTests
{
    [Theory]
    [InlineData("price")]
    [InlineData("settlement")]
    [InlineData("both")]
    public async Task CreateAsync_ConfiguredTransactionAssignmentPrefersClosestEconomicEvidence(string dimension)
    {
        var path = await WriteStatementAsync("rank-transactions.csv",
            "EXT-1,SPY,10,10,100.5,trade,2026-05-28,2026-05-30,USD,,");
        var near = PolicyTransaction("z-near", 10m, 100m) with
        {
            Price = 10.1m,
            SettlementDate = new DateOnly(2026, 5, 31)
        };
        var far = PolicyTransaction("a-far", 10m, 100m) with
        {
            Price = dimension == "settlement" ? near.Price : 20m,
            SettlementDate = dimension == "price" ? near.SettlementDate : new DateOnly(2026, 6, 2)
        };
        var profile = PolicyProfile(transactions: [new("rank-all-dimensions", 1m, TimeSpan.FromDays(4), 20m)]);
        var artifacts = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [], [far, near]), profile, artifacts);

        var result = await workflow.CreateAsync(Request(path, profile.ProfileId));
        var group = (await artifacts.GetAsync(result.Import.ImportId))!.MatchGroups.Should().ContainSingle().Subject;
        group.InternalEvidenceReferences.Should().Equal("internal:z-near");
        group.MatchTier.Should().Be("Tolerance");
        result.Breaks.Should().ContainSingle(item => item.SourceReference == "internal:a-far");
    }

    [Fact]
    public async Task CreateAsync_ConfiguredPositionAssignmentRanksObservedPrices()
    {
        var path = await WriteStatementAsync("rank-positions.csv",
            "EXT-1,SPY,10,10.1,0,position,2026-05-28,,USD,,");
        var near = new InternalPortfolioPosition("z-near", "EXT-1", "SPY", new(2026, 5, 28),
            10m, 100m, "internal:near")
        { Price = 10m };
        var far = near with { PositionId = "a-far", Price = 20m, EvidenceReference = "internal:far" };
        var profile = PolicyProfile(positions: [new("rank-price", 0m, 200m, 20m)]);
        var artifacts = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([far, near], [], []), profile, artifacts);

        var result = await workflow.CreateAsync(Request(path, profile.ProfileId));
        (await artifacts.GetAsync(result.Import.ImportId))!.MatchGroups.Should().ContainSingle().Which
            .InternalEvidenceReferences.Should().Equal("internal:near");
    }

    [Fact]
    public async Task CreateAsync_ConfiguredCashAssignmentRanksSettlementDistance()
    {
        var path = await WriteStatementAsync("rank-cash.csv",
            "EXT-1,,0,0,1005,cash,2026-05-31,,USD,,");
        var profile = PolicyProfile(cash: [new("rank-date", 10m, null, TimeSpan.FromDays(4))]);
        var artifacts = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [
            new("a-far", "EXT-1", "USD", 1000m, "internal:far", new DateOnly(2026, 5, 28)),
            new("z-near", "EXT-1", "USD", 1000m, "internal:near", new DateOnly(2026, 5, 30))], []), profile, artifacts);

        var result = await workflow.CreateAsync(Request(path, profile.ProfileId));
        (await artifacts.GetAsync(result.Import.ImportId))!.MatchGroups.Should().ContainSingle().Which
            .InternalEvidenceReferences.Should().Equal("internal:near");
    }

    [Theory]
    [InlineData("external-id", false)]
    [InlineData("identity", false)]
    [InlineData("tolerance", false)]
    [InlineData("one-to-many", false)]
    [InlineData("many-to-one", false)]
    [InlineData("external-id", true)]
    [InlineData("identity", true)]
    [InlineData("tolerance", true)]
    [InlineData("one-to-many", true)]
    [InlineData("many-to-one", true)]
    public async Task CreateAsync_SecurityTransactionsRequireCompatibleCurrencies(string scenario, bool hasFx)
    {
        var externalId = scenario == "external-id" ? "EXT-9" : "";
        var rows = scenario == "many-to-one"
            ? new[]
            {
                "EXT-1,SPY,6,10,60,trade,2026-05-28,2026-05-30,EUR,,",
                "EXT-1,SPY,4,10,40,trade,2026-05-28,2026-05-30,EUR,,"
            }
            : new[] { $"EXT-1,SPY,10,10,100,trade,2026-05-28,2026-05-30,EUR,,{externalId}" };
        var path = await WriteStatementAsync("currency.csv", rows);
        var total = hasFx ? 200m : 100m;
        if (scenario == "tolerance")
            total -= 0.5m;
        var transactions = scenario == "one-to-many"
            ? new[] { PolicyTransaction("a", 6m, total * .6m), PolicyTransaction("b", 4m, total * .4m) }
            : new[] { PolicyTransaction("a", 10m, total) with { ExternalTransactionId = externalId } };
        transactions = transactions.Select(transaction => transaction with
        {
            Price = transaction.NetAmount / transaction.Quantity
        }).ToArray();
        var profile = PolicyProfile(transactions: [new("amount-price", 1m, TimeSpan.Zero, 1m)]);
        var artifacts = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [], transactions), profile, artifacts,
            hasFx ? new TableReconciliationFxRateProvider([new("EUR", "USD", 2m, new(2026, 5, 1))]) : null);

        var result = await workflow.CreateAsync(Request(path, profile.ProfileId));
        var artifact = await artifacts.GetAsync(result.Import.ImportId);

        artifact.Should().NotBeNull();
        if (hasFx)
        {
            result.Breaks.Should().BeEmpty("FX converts both amount and price to a comparable currency");
            artifact!.MatchGroups.Should().ContainSingle().Which.MatchTier
                .Should().Be(scenario == "tolerance" ? "Tolerance" : "Exact");
        }
        else
        {
            artifact!.MatchCount.Should().Be(0, "security and equal numerical amounts cannot substitute for currency compatibility");
            artifact.MatchGroups.Should().BeEmpty();
            result.Breaks.Should().NotBeEmpty();
        }
    }

    [Theory]
    [InlineData(1005, 1, true)]
    [InlineData(1011, 1, false)]
    [InlineData(1005, 2, false)]
    public async Task CreateAsync_CashRulesUseBasisPointsAndSettlementWindow(decimal amount, int days, bool matches)
    {
        var path = await WriteStatementAsync("cash-policy.csv",
            $"EXT-1,,0,0,{amount.ToString(CultureInfo.InvariantCulture)},cash,2026-05-31,,USD,,");
        var profile = PolicyProfile(cash:
        [
            new("strict-first", 1m, null, TimeSpan.FromDays(1)),
            new("bps-second", 1m, 100m, TimeSpan.FromDays(1)),
            new("bps-third", 1m, 100m, TimeSpan.FromDays(1))
        ]);
        var artifacts = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [new("cash", "EXT-1", "USD", 1000m, "internal:cash",
            new DateOnly(2026, 5, 31).AddDays(-days))], []), profile, artifacts);

        var result = await workflow.CreateAsync(Request(path, profile.ProfileId));
        var artifact = (await artifacts.GetAsync(result.Import.ImportId))!;

        artifact.MatchCount.Should().Be(matches ? 1 : 0);
        if (matches)
        {
            result.Breaks.Should().BeEmpty();
            artifact.MatchGroups.Should().ContainSingle().Which.RuleIds.Should()
                .Equal("bps-second", "statement-cash-tolerance-v1");
        }
    }

    [Theory]
    [InlineData(false, 1, 10.2, true)]
    [InlineData(false, 2, 10.2, false)]
    [InlineData(false, 1, 10.3, false)]
    [InlineData(true, 1, 10.2, true)]
    [InlineData(true, 2, 10.2, false)]
    [InlineData(true, 1, 10.3, false)]
    public async Task CreateAsync_TransactionRulesRetainAppliedRuleAndEnforceEveryCondition(
        bool split, int days, decimal price, bool matches)
    {
        var path = await WriteStatementAsync("transaction-policy.csv",
            $"EXT-1,SPY,10,{price.ToString(CultureInfo.InvariantCulture)},100.5,trade,2026-05-28,2026-05-30,USD,,");
        var date = new DateOnly(2026, 5, 30).AddDays(days);
        var transactions = split
            ? new[] { PolicyTransaction("a", 6m, 60m) with { SettlementDate = date, Price = 10m },
                PolicyTransaction("b", 4m, 40m) with { SettlementDate = date, Price = 10m } }
            : new[] { PolicyTransaction("a", 10m, 100m) with { SettlementDate = date, Price = 10m } };
        var profile = PolicyProfile(transactions:
        [
            new("strict-first", 1m, TimeSpan.Zero, 0m),
            new("window-price-second", 1m, TimeSpan.FromDays(1), .25m),
            new("window-price-third", 1m, TimeSpan.FromDays(1), .25m)
        ]);
        var artifacts = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [], transactions), profile, artifacts);

        var result = await workflow.CreateAsync(Request(path, profile.ProfileId));
        var artifact = (await artifacts.GetAsync(result.Import.ImportId))!;

        artifact.MatchCount.Should().Be(matches ? 1 : 0);
        if (matches)
        {
            result.Breaks.Should().BeEmpty();
            var group = artifact.MatchGroups.Should().ContainSingle().Subject;
            group.MatchTier.Should().Be("Tolerance");
            group.RuleIds.Should().Equal("window-price-second",
                split ? "statement-transaction-split-v1" : "statement-transaction-tolerance-v1");
            group.InternalEvidenceReferences.Should().HaveCount(split ? 2 : 1);
        }
    }

    [Theory]
    [InlineData(10.2, true)]
    [InlineData(10.3, false)]
    public async Task CreateAsync_PositionRulesEnforcePriceAndUseLaterEligibleRule(decimal price, bool matches)
    {
        var path = await WriteStatementAsync("position-policy.csv",
            $"EXT-1,SPY,10,{price.ToString(CultureInfo.InvariantCulture)},0,position,2026-05-28,,USD,,");
        var profile = PolicyProfile(positions:
        [
            new("strict-first", 0m, 5m, 0m),
            new("price-second", 0m, 5m, .25m),
            new("price-third", 0m, 5m, .25m)
        ]);
        var artifacts = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([new("position", "EXT-1", "SPY", new(2026, 5, 28),
            10m, 100m, "internal:position")], [], []), profile, artifacts);

        var result = await workflow.CreateAsync(Request(path, profile.ProfileId));
        var artifact = (await artifacts.GetAsync(result.Import.ImportId))!;

        artifact.MatchCount.Should().Be(matches ? 1 : 0);
        if (matches)
        {
            result.Breaks.Should().BeEmpty();
            artifact.MatchGroups.Should().ContainSingle().Which.RuleIds.Should()
                .Equal("price-second", "statement-position-tolerance-v1");
        }
    }

    [Fact]
    public async Task CreateAsync_MissingInternalPricesPreserveQuantityAndNetCashComparisons()
    {
        var path = await WriteStatementAsync("partial-price-evidence.csv",
            "EXT-1,SPY,10,100,1000,position,2026-05-28,,USD,,",
            "EXT-1,SPY,1,100,-101,trade,2026-05-28,2026-05-30,USD,1,EXT-9");
        var artifacts = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new(
            [new("position", "EXT-1", "SPY", new(2026, 5, 28), 10m, null, "internal:position")], [],
            [PolicyTransaction("fee-trade", 1m, -101m) with { ExternalTransactionId = "EXT-9" }]),
            StatementToleranceProfile.Default, artifacts);

        var result = await workflow.CreateAsync(Request(path));
        var artifact = (await artifacts.GetAsync(result.Import.ImportId))!;

        result.Breaks.Should().BeEmpty("quantity-only position snapshots and fee-inclusive cash do not supply an execution price");
        artifact.MatchCount.Should().Be(2);
        artifact.MatchGroups.Should().OnlyContain(group => group.MatchTier == "Exact");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CreateAsync_PrecisionAndRulePrecedenceApplyAcrossPairsAndSplits(bool exactSplit)
    {
        var path = await WriteStatementAsync("split-precedence.csv",
            "EXT-1,SPY,10,10,100,trade,2026-05-28,2026-05-30,USD,,");
        var profile = PolicyProfile(transactions:
        [
            new("first", .1m, TimeSpan.FromDays(1), .1m),
            new("second", 1m, TimeSpan.FromDays(1), .1m)
        ]);
        var date = new DateOnly(2026, 5, 30).AddDays(exactSplit ? 0 : 1);
        var transactions = new[]
        {
            PolicyTransaction("a", 6m, 60m) with { SettlementDate = date, Price = 10m },
            PolicyTransaction("b", 4m, 40m) with { SettlementDate = date, Price = 10m },
            PolicyTransaction("later-pair", 10m, 99.5m) with { Price = 10m }
        };
        var artifacts = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [], transactions), profile, artifacts);

        var result = await workflow.CreateAsync(Request(path, profile.ProfileId));
        var artifact = (await artifacts.GetAsync(result.Import.ImportId))!;

        var group = artifact.MatchGroups.Should().ContainSingle().Subject;
        group.InternalEvidenceReferences.Should().Equal("internal:a", "internal:b");
        group.MatchTier.Should().Be(exactSplit ? "Exact" : "Tolerance");
        group.RuleIds.Should().Equal(exactSplit
            ? ["statement-transaction-split-v1"]
            : ["first", "statement-transaction-split-v1"]);
        result.Breaks.Should().ContainSingle().Which.SourceReference.Should().Be("internal:later-pair");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAsync_ExactAmountsCannotBypassObservedPriceConditions(bool split)
    {
        var path = await WriteStatementAsync("exact-price-policy.csv",
            "EXT-1,SPY,10,10.3,100,trade,2026-05-28,2026-05-30,USD,,EXT-9");
        var transactions = split
            ? new[] { PolicyTransaction("a", 6m, 60m) with { Price = 10m },
                PolicyTransaction("b", 4m, 40m) with { Price = 10m } }
            : new[] { PolicyTransaction("a", 10m, 100m) with { Price = 10m, ExternalTransactionId = "EXT-9" } };
        var artifacts = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [], transactions), StatementToleranceProfile.Default, artifacts);

        var result = await workflow.CreateAsync(Request(path));

        (await artifacts.GetAsync(result.Import.ImportId))!.MatchCount.Should().Be(0);
        result.Breaks.Should().NotBeEmpty();
    }

    private static InternalLedgerTransaction PolicyTransaction(string id, decimal quantity, decimal amount) =>
        new(id, null, "EXT-1", "SPY", "USD", new(2026, 5, 28), new(2026, 5, 30), "trade",
            quantity, amount, $"internal:{id}");

    private static StatementToleranceProfile PolicyProfile(IReadOnlyList<CashToleranceRule>? cash = null,
        IReadOnlyList<PositionToleranceRule>? positions = null, IReadOnlyList<TransactionToleranceRule>? transactions = null) =>
        new("workflow-policy", 3, cash ?? [], positions ?? [], transactions ?? []);

    private StatementRunWorkflowService CreatePolicyWorkflow(InternalReconciliationPopulations populations,
        StatementToleranceProfile profile, InMemoryStatementRunMatchArtifactStore artifacts,
        IReconciliationFxRateProvider? fx = null)
    {
        var imports = new JsonCanonicalStatementStore(_root, new AtomicFileWriterAdapter());
        return new(imports,
            new JsonReconciliationCaseStore(_root, new AtomicFileWriterAdapter()),
            new JsonReconciliationBreakStore(_root, new AtomicFileWriterAdapter()),
            new CsvBrokerStatementService(imports),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()),
            Populations(populations), fx, new InMemoryStatementToleranceProfileProvider([profile]),
            new InMemoryStatementRunRecoveryRepository(), artifacts,
            caseworkCommitStore: EmptyEphemeralStatementCaseworkCommitStore.Instance);
    }
}
