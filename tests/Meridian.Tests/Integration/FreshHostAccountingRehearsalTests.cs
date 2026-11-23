using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;

namespace Meridian.Tests.Integration;

/// <summary>
/// Production classes over PostgreSQL and file stores, using the supported canonical Synthetic
/// cash-equity posting profile. Launch uses the existing native rehearsal's legacy null-token
/// provenance scaffolding; it does not establish admission of explicitly Simulated promotion
/// evidence. That marked source is separately refused before launch. Scoped Designer Simulated
/// sessions remain outside canonical posting, and no live brokerage authorization is established.
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class FreshHostAccountingRehearsalTests
{
    [LedgerDatabaseFact]
    public Task SyntheticEquityRise_AfterDurableHandoff_FreshHostRecoversExactlyOnce()
        => RunAsync(Interruption.AfterHandoff);

    [LedgerDatabaseFact]
    public Task SyntheticEquityRise_AfterJournalCommitBeforeCompletion_FreshHostConfirmsExactlyOnce()
        => RunAsync(Interruption.AfterJournalCommit);

    private static async Task RunAsync(Interruption interruption)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        var root = Path.Combine(Path.GetTempPath(), "meridian-fresh-host-accounting", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
            var scope = await SeedScopeAsync(database, root, ct);
            ReviewedRun reviewed;
            TradeExecutedEvent fill;
            await using (var first = new Host(scope, interruption))
            {
                await first.StartAsync(ct);
                first.Portfolio.Cash.Should().Be(100000m);
                reviewed = await RetainReviewedRunAsync(first, scope, ct);
                // Actual promotion launches the retained artifact in LiveTradingEngine, whose
                // session submits the strategy intent with its typed account grant to the OMS.
                await PublishPriceAsync(first, 102m, ct);
                fill = await first.Fault!.Reached.Task.WaitAsync(ct);
                fill.FilledQuantity.Should().Be(10m);
                fill.FillPrice.Should().Be(102m);
                fill.Commission.Should().Be(1m);
                fill.NewCash.Should().Be(98979m);
                fill.SourceMode.Should().Be("Synthetic");
                fill.RunId.Should().Be(reviewed.RunId);
                fill.StrategyId.Should().Be(reviewed.StrategyId);
                fill.AccountingEvidenceVersion.Should().Be(1);
                first.Oms.GetCompletedOrders().Should().ContainSingle(order => order.RunId == reviewed.RunId);
            }

            // Inspect via fresh stores after all first-host owners have been disposed. Only
            // durable identity/evidence and configuration are carried into the next host.
            await using (var pending = new WalTradeFillPostingStore(new TradeFillPostingStoreOptions(
                Path.Combine(root, "handoffs"), scope.Context), Microsoft.Extensions.Logging.Abstractions.NullLogger<WalTradeFillPostingStore>.Instance))
            {
                var retained = (await pending.LoadPendingAsync(ct)).Should().ContainSingle().Which;
                retained.TradeEvent.Should().BeEquivalentTo(fill);
                retained.LastFailure.Should().Contain(interruption.ToString());
            }
            var interruptedJournals = await CreateJournals(scope).GetByPeriodAsync(scope.Period.PeriodId, ct);
            interruptedJournals.Should().HaveCount(interruption == Interruption.AfterHandoff ? 1 : 3);
            var committedBeforeRestart = interruptedJournals.Select(x => x.Entry.JournalEntryId).ToArray();

            await using (var recovered = new Host(scope))
            {
                // Initial consumer replay must finish before the canonical portfolio can start.
                await recovered.StartAsync(ct);
                (await recovered.Handoffs.LoadPendingAsync(ct)).Should().BeEmpty();
                var run = (await recovered.Runs.GetRunByIdAsync(reviewed.RunId, ct))!;
                run.StrategyArtifactId.Should().Be(reviewed.ArtifactId);
                run.FundAccountId.Should().Be(scope.AccountId);
                (await recovered.Artifacts.GetAsync(reviewed.ArtifactId, ct))!.ComputeId().Should().Be(reviewed.ArtifactId);
                (await recovered.Promotions.LoadAllAsync(ct)).Should().ContainSingle(record =>
                    record.TargetRunId == reviewed.RunId && record.StrategyArtifactId == reviewed.ArtifactId && record.Decision == "Approved");
                recovered.Oms.GetOrder(fill.OrderId)!.FilledQuantity.Should().Be(10m);
                recovered.Engine.ActiveRunIds.Should().Contain(reviewed.RunId);
                await PublishPriceAsync(recovered, 104m, ct);
                recovered.Oms.GetCompletedOrders().Should().ContainSingle(order => order.RunId == reviewed.RunId);
                (await recovered.Oms.SubmitAsync(Command(scope, reviewed, fill.OrderId), ct)).Success.Should().BeFalse(
                    "the retained dispatch identity must not become available after recreating the OMS");
                await AssertEconomicsAsync(recovered, scope, fill, ct);

                // Exact report and fill replay traverses the production idempotency boundaries.
                var report = new ExecutionReport
                {
                    OrderId = fill.OrderId,
                    ClientOrderId = fill.OrderId,
                    Symbol = Symbol,
                    Side = OrderSide.Buy,
                    ReportType = ExecutionReportType.Fill,
                    OrderStatus = OrderStatus.Filled,
                    OrderQuantity = 10m,
                    FilledQuantity = 10m,
                    FillPrice = 102m,
                    Commission = 1m,
                    Timestamp = fill.OccurredAt
                };
                await recovered.Oms.ReconcileRecoveryOrderAsync(report, ct);
                await recovered.Consumer.PublishAsync(fill);
                var stale = () => recovered.Oms.ReconcileRecoveryOrderAsync(report with
                {
                    ReportType = ExecutionReportType.PartialFill,
                    OrderStatus = OrderStatus.PartiallyFilled,
                    FilledQuantity = 4m,
                    Commission = 0.4m,
                    Timestamp = fill.OccurredAt.AddSeconds(-1)
                }, ct);
                await stale.Should().ThrowAsync<InvalidDataException>();
                await recovered.Oms.ReconcileRecoveryOrderAsync(report, ct);
                await recovered.Consumer.PublishAsync(fill);
                var changedEconomics = () => recovered.Consumer.PublishAsync(fill with { FillPrice = 103m });
                await changedEconomics.Should().ThrowExactlyAsync<InvalidOperationException>()
                    .WithMessage($"Fill '{fill.FillId:D}' was replayed with different economic content.");
                var wrongAccount = () => recovered.Accounting.PostAsync(fill with
                {
                    FillId = Guid.NewGuid(),
                    FinancialAccountId = Guid.NewGuid().ToString("D")
                }, scope.Context, scope.SecurityId, ct);
                await wrongAccount.Should().ThrowAsync<InvalidOperationException>();
                await AssertEconomicsAsync(recovered, scope, fill, ct);
                var after = await recovered.Journals.GetByPeriodAsync(scope.Period.PeriodId, ct);
                after.Select(x => x.Entry.JournalEntryId).Should().Contain(committedBeforeRestart);
            }

            // A second full restart also reconstructs the exact lot basis and cash, without
            // needing the old portfolio, a projection ledger, or a remembered posted-id cache.
            await using (var verified = new Host(scope))
            {
                await verified.StartAsync(ct);
                await PublishPriceAsync(verified, 104m, ct);
                await verified.Consumer.PublishAsync(fill);
                await AssertEconomicsAsync(verified, scope, fill, ct);
                (await verified.Handoffs.LoadPendingAsync(ct)).Should().BeEmpty();
                await verified.Journals.RevokeAsync(scope.Options.Grant!.GrantId, scope.Options.Grant.Version,
                    "synthetic-operator", Tenant, ct);
                var refused = await verified.Oms.SubmitAsync(Command(scope, reviewed, "after-grant-revocation"), ct);
                refused.Success.Should().BeFalse("canonical admission rechecks the actual retained posting grant");
                (await verified.Handoffs.LoadPendingAsync(ct)).Should().BeEmpty();
                await AssertEconomicsAsync(verified, scope, fill, ct);
            }
            await using (var revoked = new Host(scope))
            {
                var start = () => revoked.StartAsync(ct);
                await start.Should().ThrowAsync<InvalidOperationException>()
                    .WithMessage("*current retained operator-authorized grant*");
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static async Task AssertEconomicsAsync(Host host, Scope scope, TradeExecutedEvent fill, CancellationToken ct)
    {
        var records = await host.Journals.GetByPeriodAsync(scope.Period.PeriodId, ct);
        records.Should().HaveCount(3, "one approved opening, one atomic trade, and one commission are the entire financial trace");
        records.Should().OnlyContain(record => record.Entry.IsBalanced);
        var trade = records.Single(record => record.Entry.Metadata.ActivityType == "execution.trade-fill");
        var commission = records.Single(record => record.Entry.Metadata.ActivityType == "execution.trade-commission");
        trade.Entry.Lines.Select(line => (line.Account.Name, line.Debit, line.Credit)).Should().BeEquivalentTo(
            new[] { ("Securities", 1020m, 0m), ("Cash", 0m, 1020m) });
        commission.Entry.Lines.Select(line => (line.Account.Name, line.Debit, line.Credit)).Should().BeEquivalentTo(
            new[] { ("Commissions", 1m, 0m), ("Cash", 0m, 1m) });
        foreach (var posted in new[] { trade, commission })
        {
            posted.Entry.Metadata.FillId.Should().Be(fill.FillId, "the durable fill links journal → OMS order → promoted run → artifact");
            posted.Entry.Metadata.FinancialAccountId.Should().Be(scope.AccountId.ToString("D"));
            posted.AccountingPolicyId.Should().Be(scope.Context.AccountingPolicyId);
            posted.AccountingPolicyVersion.Should().Be(scope.Context.AccountingPolicyVersion);
            posted.PeriodId.Should().Be(scope.Context.PeriodId);
            posted.Entry.Metadata.Tags!["dataProvenance"].Should().Be("SIMULATED");
            posted.Entry.Metadata.Tags["automation.actionOrigin"].Should().Be("AuthorizedSystem");
            posted.Entry.Metadata.Tags["approvalState"].Should().Be("NotRequired");
        }
        records.Sum(record => record.Entry.Lines.Where(line => line.Account.Name == "Cash")
            .Sum(line => line.Debit - line.Credit)).Should().Be(98979m);
        var lots = await host.Journals.ListOpenTaxLotsAsync(scope.Context.LedgerBookId,
            new LedgerAccount("Securities", LedgerAccountType.Asset, Symbol, scope.AccountId.ToString("D")), ct);
        lots.Should().ContainSingle();
        lots[0].OpenQuantity.Should().Be(10m);
        lots[0].ToOpenLot().OpenFunctionalCostBasis.Should().Be(1020m);
        host.Portfolio.Cash.Should().Be(98979m);
        var account = host.Portfolio.GetAccountSnapshot(scope.AccountId.ToString("D"))!;
        account.Positions.Should().ContainSingle().Which.Quantity.Should().Be(10);
        account.Positions[0].AverageCostBasis.Should().Be(102m);
        host.Portfolio.UpdateMarketPrice(Symbol, 104m);
        host.Portfolio.PortfolioValue.Should().Be(100019m);
        (host.Portfolio.PortfolioValue - 100000m).Should().Be(19m);
    }
}
