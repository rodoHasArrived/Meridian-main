using FluentAssertions;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Storage.Archival;

namespace Meridian.Tests.Reconciliation;

public sealed partial class StatementRunWorkflowServiceTests
{
    [Theory]
    [InlineData("external-id", false)]
    [InlineData("pair", false)]
    [InlineData("tolerance", false)]
    [InlineData("statement-split", false)]
    [InlineData("internal-split", false)]
    [InlineData("external-id", true)]
    [InlineData("pair", true)]
    [InlineData("tolerance", true)]
    [InlineData("statement-split", true)]
    [InlineData("internal-split", true)]
    public async Task CreateAsync_RequiresCompatibleTransactionCurrencyAcrossEveryMatchStage(string shape, bool hasFx)
    {
        var path = shape == "internal-split"
            ? await WriteStatementAsync("currency.csv",
                "EXT-1,SPY,6,10,-60,trade,2026-05-28,2026-05-30,EUR,,",
                "EXT-1,SPY,4,10,-40,trade,2026-05-28,2026-05-30,EUR,,")
            : await WriteStatementAsync("currency.csv", "EXT-1,SPY,10,10,-100,trade,2026-05-28,2026-05-30,EUR,,EXT-9");
        var amount = hasFx ? -125m : -100m;
        var transactions = shape == "statement-split"
            ? new[] { PolicyTransaction("a", amount * 0.6m, 6m), PolicyTransaction("b", amount * 0.4m, 4m) }
            : new[] { PolicyTransaction("a", shape == "tolerance" ? amount + 0.005m : amount, 10m) with
                { ExternalTransactionId = shape == "external-id" ? "EXT-9" : null } };
        var store = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [], transactions), StatementToleranceProfile.Default, store,
            hasFx ? new TableReconciliationFxRateProvider([new("EUR", "USD", 1.25m, new DateOnly(2026, 5, 1))]) : null);

        var result = await workflow.CreateAsync(Request(path));
        var artifact = await store.GetAsync(result.Import.ImportId);

        artifact.Should().NotBeNull();
        if (hasFx)
        {
            result.Breaks.Should().BeEmpty();
            artifact!.MatchGroups.Should().ContainSingle();
            artifact.MatchGroups![0].MatchTier.Should().Be(shape == "tolerance" ? "Tolerance" : "Exact");
        }
        else
        {
            result.Breaks.Should().NotBeEmpty("equal numeric amounts do not establish compatible currencies");
            artifact!.MatchGroups.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task CreateAsync_EvaluatesLaterCashBasisPointRuleAndRetainsFirstAdmissibleRule()
    {
        var path = await WriteStatementAsync("bps.csv", "EXT-1,,0,0,1000.50,cash,2026-05-31,,USD,,");
        var profile = StatementToleranceProfile.Default with
        {
            CashRules = [new("too-tight", 0.01m, null, TimeSpan.Zero), new("bps-wins", 0.01m, 10m, TimeSpan.Zero), new("later-rule", 2m, null, TimeSpan.Zero)]
        };
        var store = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [new("cash", "EXT-1", "USD", 1000m, "internal:cash", new(2026, 5, 31))], []), profile, store);

        var result = await workflow.CreateAsync(Request(path));
        var retained = await store.GetAsync(result.Import.ImportId);

        result.Breaks.Should().BeEmpty();
        var group = retained!.MatchGroups.Should().ContainSingle().Subject;
        group.RuleIds.Should().Equal("bps-wins");
        group.Confidence.Should().Be(0.85m, "the applied 1.00 basis-point allowance determines confidence");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task CreateAsync_EvaluatesCashDateWindowWithoutAdmittingStaleStatementBalances(int days, bool matches)
    {
        var path = await WriteStatementAsync("cash-window.csv", "EXT-1,,0,0,1000.50,cash,2026-05-31,,USD,,");
        var profile = StatementToleranceProfile.Default with { CashRules = [new("cash-window", 1m, null, TimeSpan.FromDays(days))] };
        var store = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [new("cash", "EXT-1", "USD", 1000m, "internal:cash", new(2026, 5, 30))], []), profile, store);

        var result = await workflow.CreateAsync(Request(path));
        var retained = await store.GetAsync(result.Import.ImportId);

        retained!.MatchGroups!.Count.Should().Be(matches ? 1 : 0);
        if (matches)
            retained.MatchGroups[0].RuleIds.Should().Equal("cash-window");
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, true)]
    public async Task CreateAsync_EvaluatesAllTransactionRuleConditionsAndRetainsAppliedRule(int settlementDays, int priceTolerance, bool matches)
    {
        var path = await WriteStatementAsync("transaction-policy.csv", "EXT-1,SPY,10,10.5,-100.50,trade,2026-05-28,2026-05-31,USD,,");
        var profile = StatementToleranceProfile.Default with
        {
            TransactionRules = [new("too-tight", 0.01m, TimeSpan.FromDays(5), 5m), new("transaction-policy", 1m, TimeSpan.FromDays(settlementDays), priceTolerance)]
        };
        var store = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [], [PolicyTransaction("a", -100m, 10m) with { Price = 10m }]), profile, store);

        var result = await workflow.CreateAsync(Request(path));
        var retained = await store.GetAsync(result.Import.ImportId);

        retained!.MatchGroups!.Count.Should().Be(matches ? 1 : 0);
        if (matches)
            retained.MatchGroups[0].RuleIds.Should().Equal("transaction-policy");
        else
            result.Breaks.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0, 1, false, false)]
    [InlineData(1, 0, false, false)]
    [InlineData(1, 1, true, false)]
    [InlineData(0, 1, false, true)]
    [InlineData(1, 0, false, true)]
    [InlineData(1, 1, true, true)]
    public async Task CreateAsync_SplitMatchingCannotBypassTransactionPriceOrSettlementRules(int settlementDays, int priceTolerance, bool matches, bool internalAnchor)
    {
        var path = internalAnchor
            ? await WriteStatementAsync("split-policy.csv",
                "EXT-1,SPY,6,10.5,-60.30,trade,2026-05-28,2026-05-31,USD,,",
                "EXT-1,SPY,4,10.5,-40.20,trade,2026-05-28,2026-05-31,USD,,")
            : await WriteStatementAsync("split-policy.csv", "EXT-1,SPY,10,10.5,-100.50,trade,2026-05-28,2026-05-31,USD,,");
        var profile = StatementToleranceProfile.Default with
        {
            TransactionRules = [new("split-policy", 1m, TimeSpan.FromDays(settlementDays), priceTolerance)]
        };
        var store = new InMemoryStatementRunMatchArtifactStore();
        var transactions = internalAnchor
            ? new[] { PolicyTransaction("a", -100m, 10m) with { Price = 10m } }
            : new[] { PolicyTransaction("a", -60m, 6m) with { Price = 10m }, PolicyTransaction("b", -40m, 4m) with { Price = 10m } };
        var workflow = CreatePolicyWorkflow(new([], [], transactions), profile, store);

        var result = await workflow.CreateAsync(Request(path));
        var retained = await store.GetAsync(result.Import.ImportId);

        retained!.MatchGroups!.Count.Should().Be(matches ? 1 : 0);
        if (matches)
        {
            retained.MatchGroups[0].RuleIds.Should().Equal("split-policy", "statement-transaction-split-v1");
            retained.MatchGroups[0].MatchTier.Should().Be("Tolerance");
        }
        else
            result.Breaks.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task CreateAsync_EvaluatesPositionPriceAndLaterRules(int priceTolerance, bool matches)
    {
        var path = await WriteStatementAsync("position-policy.csv", "EXT-1,SPY,10,10.5,105,position,2026-05-28,,USD,,");
        var profile = StatementToleranceProfile.Default with
        {
            PositionRules = [new("too-tight", 0m, 0m, 0m), new("position-policy", 0m, 10m, priceTolerance)]
        };
        var store = new InMemoryStatementRunMatchArtifactStore();
        var position = new InternalPortfolioPosition("pos", "EXT-1", "SPY", new(2026, 5, 28), 10m, 100m, "internal:position") { Price = 10m };
        var workflow = CreatePolicyWorkflow(new([position], [], []), profile, store);

        var result = await workflow.CreateAsync(Request(path));
        var retained = await store.GetAsync(result.Import.ImportId);

        retained!.MatchGroups!.Count.Should().Be(matches ? 1 : 0);
        if (matches)
            retained.MatchGroups[0].RuleIds.Should().Equal("position-policy");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task CreateAsync_EmptyRuleListAllowsOnlyExactMatches(bool split, bool exact)
    {
        var amount = exact ? "-100" : "-100.005";
        var path = await WriteStatementAsync("no-rules.csv", $"EXT-1,SPY,10,10,{amount},trade,2026-05-28,2026-05-30,USD,,");
        var profile = StatementToleranceProfile.Default with { TransactionRules = [] };
        var store = new InMemoryStatementRunMatchArtifactStore();
        var transactions = split ? new[] { PolicyTransaction("a", -60m, 6m), PolicyTransaction("b", -40m, 4m) } : [PolicyTransaction("a", -100m, 10m)];
        var workflow = CreatePolicyWorkflow(new([], [], transactions), profile, store);

        var result = await workflow.CreateAsync(Request(path));
        var retained = await store.GetAsync(result.Import.ImportId);

        if (exact)
        {
            result.Breaks.Should().BeEmpty();
            retained!.MatchGroups.Should().ContainSingle().Which.MatchTier.Should().Be("Exact");
        }
        else
        {
            result.Breaks.Should().NotBeEmpty();
            retained!.MatchGroups.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task CreateAsync_ExactAmountCannotBypassRetainedPriceCondition()
    {
        var path = await WriteStatementAsync("exact-price.csv", "EXT-1,SPY,10,10.5,-100,trade,2026-05-28,2026-05-30,USD,,EXT-9");
        var store = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [], [PolicyTransaction("a", -100m, 10m) with { Price = 10m, ExternalTransactionId = "EXT-9" }]),
            StatementToleranceProfile.Default, store);

        var result = await workflow.CreateAsync(Request(path));
        var retained = await store.GetAsync(result.Import.ImportId);

        result.Breaks.Should().NotBeEmpty();
        retained!.MatchGroups.Should().BeEmpty("neither external identity nor equal net cash proves equal retained prices");
    }

    [Fact]
    public async Task CreateAsync_PartitionsSplitSettlementWindowBeforeBoundedSearch()
    {
        var path = await WriteStatementAsync("split-window-pool.csv", "EXT-1,SPY,10,10,-100,trade,2026-05-28,2026-05-30,USD,,");
        var noise = Enumerable.Range(0, 30).Select(i => PolicyTransaction($"noise-{i:D2}", -1000m, 0m) with { SettlementDate = new(2026, 6, 15) });
        var store = new InMemoryStatementRunMatchArtifactStore();
        var workflow = CreatePolicyWorkflow(new([], [], [.. noise, PolicyTransaction("a", -60m, 6m), PolicyTransaction("b", -40m, 4m)]),
            StatementToleranceProfile.Default, store);

        var result = await workflow.CreateAsync(Request(path));
        var retained = await store.GetAsync(result.Import.ImportId);

        var group = retained!.MatchGroups.Should().ContainSingle().Subject;
        group.InternalEvidenceReferences.Should().Equal("internal:a", "internal:b");
        group.MatchTier.Should().Be("Exact");
        result.Breaks.Should().HaveCount(30, "out-of-window ledger movements remain separate breaks");
    }

    private static InternalLedgerTransaction PolicyTransaction(string id, decimal amount, decimal quantity) =>
        new(id, null, "EXT-1", "SPY", "USD", new(2026, 5, 28), new(2026, 5, 30), "trade", quantity, amount, $"internal:{id}");

    private StatementRunWorkflowService CreatePolicyWorkflow(InternalReconciliationPopulations populations,
        StatementToleranceProfile profile, IStatementRunMatchArtifactStore artifactStore, IReconciliationFxRateProvider? fx = null)
    {
        var importStore = new JsonCanonicalStatementStore(_root, new AtomicFileWriterAdapter());
        return new StatementRunWorkflowService(importStore,
            new JsonReconciliationCaseStore(_root, new AtomicFileWriterAdapter()),
            new JsonReconciliationBreakStore(_root, new AtomicFileWriterAdapter()),
            new CsvBrokerStatementService(importStore),
            new StatementReconciliationContextAdapter(new StatementReconciliationService()),
            Populations(populations), fx, new InMemoryStatementToleranceProfileProvider([profile]),
            new InMemoryStatementRunRecoveryRepository(), artifactStore,
            caseworkCommitStore: EmptyEphemeralStatementCaseworkCommitStore.Instance);
    }
}
