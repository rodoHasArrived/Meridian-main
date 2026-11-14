using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed partial class AutomatedPostingGrantTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SyntheticFillWorkflow_PostsAtomicLotsFeesAndFIFOWithRestartAndRevocationRecovery()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        var scope = await CreateScopeAsync(database);
        var securityId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var authority = await database.SeedLotReferenceAuthorityAsync(scope.Reference.LedgerBookId, securityId, positionId,
            financialAccountId: AccountId);
        var chart = new[]
        {
            new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
            new ChartOfAccountsNodeDto("securities", "Assets:Securities", "Securities", "Asset"),
            new ChartOfAccountsNodeDto("gains", "Revenue:Gains", "Realized Gain", "Revenue"),
            new ChartOfAccountsNodeDto("losses", "Expenses:Losses", "Realized Loss", "Expense"),
            new ChartOfAccountsNodeDto("commissions", "Expenses:Commissions", "Commissions", "Expense"),
            new ChartOfAccountsNodeDto("fees", "Expenses:Fees", "Fees", "Expense")
        };
        var workspace = scope.Workspace with { ChartOfAccounts = chart };
        await database.AccountingConfigurationStore.SaveAsync(workspace);
        var carrying = new LedgerAccount("Securities", LedgerAccountType.Asset, "AAPL", AccountId);
        await database.JournalStore.SaveTaxLotPolicyAsync(new LedgerAccountTaxLotPolicyRecord(Guid.NewGuid(),
            scope.Reference.LedgerBookId, carrying, LedgerTaxLotReliefMethod.Fifo, "fifo-v1", new(2026, 5, 1), Timestamp, Timestamp));
        var config = Substitute.For<IAccountingConfigurationService>();
        config.GetWorkspaceAsync(Arg.Any<string>(), scope.Reference.LedgerBookId, Arg.Any<CancellationToken>(), TenantId, null)
            .Returns(workspace);
        var policies = new AccountingPolicyService();
        var policySnapshot = await policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(AccountingBasisKindDto.Primary,
            scope.Reference.PolicyId, scope.Reference.PolicyVersion, "Synthetic fill policy", new(2026, 5, 1),
            RulePack: new AccountingPolicyRulePackDto("synthetic", "v1",
            [
                new("execution.trade-fill", AccountingTreatmentKindDto.General, SourceEventType: "execution.trade-fill", RequiresApproval: false, AllowsAutoPosting: true),
                new("execution.trade-commission", AccountingTreatmentKindDto.Expense, SourceEventType: "execution.trade-commission", RequiresApproval: false, AllowsAutoPosting: true),
                new("execution.trade-fee", AccountingTreatmentKindDto.Expense, SourceEventType: "execution.trade-fee", RequiresApproval: false, AllowsAutoPosting: true)
            ])));
        await database.JournalStore.AppendAsync(BuildSyntheticFillOpening(scope.Reference, scope.Period.Version));
        var grant = await database.JournalStore.ActivateWithPolicyAsync(Activation(scope.Reference), policySnapshot,
            "synthetic-operator", TenantId);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        var json = JsonSerializer.SerializeToElement(new { });
        securities.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(new SecurityDetailDto(securityId,
            "Equity", SecurityStatusDto.Active, "AAPL synthetic", "USD", json, json, [], [], authority.Security.Version,
            authority.Security.EffectiveFrom, null));
        var positions = Substitute.For<IAssetOperationsQueryService>();
        positions.GetOperationsAsync(securityId, Arg.Any<CancellationToken>()).Returns(new AssetOperationsDetailDto(
            new(securityId, "Equity", "AAPL synthetic", "AAPL", []), [], [], [], [], [], [], [], [],
            new(securityId, "Ready", [], [], [], [], Timestamp, "Synthetic", null), [])
        { BookPositions = [authority.Position] });
        var options = new AutomatedTradeFillAccountingOptions
        {
            Grant = grant.Reference,
            CashAccountPath = chart[0].Path,
            SecuritiesAccountPath = chart[1].Path,
            RealizedGainAccountPath = chart[2].Path,
            RealizedLossAccountPath = chart[3].Path,
            CommissionAccountPath = chart[4].Path,
            FeeAccountPath = chart[5].Path
        };
        var root = Path.Combine(Path.GetTempPath(), "meridian-synthetic-fill-pg", Guid.NewGuid().ToString("N"));
        var context = new TradeFillLedgerPostingContext("synthetic-pg/v2", grant.Reference.LedgerBookId,
            grant.Reference.PeriodId, grant.Reference.LedgerBookId, grant.Reference.PolicyId, grant.Reference.PolicyVersion,
            scope.Period.Version, 2);
        using var target = new DurableLedgerPostingTarget(database.JournalStore);
        AutomatedTradeFillAccountingService Create() => new(options, root, database.JournalStore, database.JournalStore,
            target, new ConfiguredLedgerAccountResolver(config), new AccountingAutomaticPostingPolicyValidator(new AccountingPolicyService()), securities, positions);
        var buy = new TradeExecutedEvent(Guid.NewGuid(), "synthetic-buy", "AAPL", OrderSide.Buy,
            100m, 100m, 1m, 0m, 0m, Timestamp, AccountId)
        { SourceMode = "Synthetic", SettlementCurrency = "USD", Fees = [new("fee-1", "Exchange", 0.25m, "USD")] };
        try
        {
            var first = await Create().PostAsync(buy, context, securityId);
            first.Should().HaveCount(3);
            var lots = await database.JournalStore.ListOpenTaxLotsAsync(context.LedgerBookId, carrying);
            lots.Should().ContainSingle();
            lots[0].OpenQuantity.Should().Be(100m);
            lots[0].Acquisition!.FunctionalCostBasis.Should().Be(10_000m);
            var sale = buy with
            {
                FillId = Guid.NewGuid(),
                OrderId = "synthetic-sell",
                Side = OrderSide.Sell,
                FilledQuantity = 40m,
                FillPrice = 110m,
                RealizedPnl = 123456m,
                Fees = null
            };
            var sold = await Create().PostAsync(sale, context, securityId);
            sold[0].Lines.Single(line => line.Account.Name == "Realized Gain").Credit.Should().Be(400m);
            lots = await database.JournalStore.ListOpenTaxLotsAsync(context.LedgerBookId, carrying);
            lots.Single().OpenQuantity.Should().Be(60m);
            lots.Single().ToOpenLot().OpenFunctionalCostBasis.Should().Be(6000m);
            await database.JournalStore.RevokeAsync(grant.Reference.GrantId, grant.Reference.Version, "operator", TenantId);
            var replay = await Create().PostAsync(buy, context, securityId);
            replay.Select(journal => journal.JournalEntryId).Should().Equal(first.Select(journal => journal.JournalEntryId));
            var entries = await database.JournalStore.GetByPeriodAsync(context.PeriodId);
            entries.Should().HaveCount(6);
            entries.Sum(record => record.Entry.Lines.Where(line => line.Account.Name == "Cash").Sum(line => line.Debit - line.Credit))
                .Should().Be(94397.75m);
            entries.Should().OnlyContain(record => record.Entry.IsBalanced);
            var forbidden = () => Create().PostAsync(buy with { FillId = Guid.NewGuid() }, context, securityId);
            await forbidden.Should().ThrowAsync<InvalidOperationException>().WithMessage("*inactive, changed, or missing*");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static LedgerJournalEntryWrite BuildSyntheticFillOpening(AutomatedPostingGrantReferenceDto scope, long periodVersion)
    {
        var id = Guid.NewGuid();
        var commandId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        const string description = "Reviewed synthetic opening cash";
        var entry = new JournalEntry(id, at, description,
        [
            new LedgerEntry(Guid.NewGuid(), id, at, new LedgerAccount("Cash", LedgerAccountType.Asset,
                FinancialAccountId: scope.FinancialAccountId), 100000m, 0m, description,
                currency: new LedgerEntryCurrency("USD", "USD", 100000m, 0m, 1m)),
            new LedgerEntry(Guid.NewGuid(), id, at, new LedgerAccount("Capital", LedgerAccountType.Equity,
                FinancialAccountId: scope.FinancialAccountId), 0m, 100000m, description,
                currency: new LedgerEntryCurrency("USD", "USD", 0m, 100000m, 1m))
        ], new JournalEntryMetadata(ActivityType: "opening-balance", FinancialAccountId: scope.FinancialAccountId,
            LedgerBook: scope.LedgerBookId.ToString("D"), EffectiveDate: DateOnly.FromDateTime(at.UtcDateTime), IdempotencyKey: "opening:" + id));
        var command = new AccountingPostingCommandDto(commandId, scope.LedgerBookId, scope.PeriodId,
            DateOnly.FromDateTime(at.UtcDateTime), at, entry.Metadata.IdempotencyKey!,
            SourceEventId: id, ExpectedVersion: periodVersion, SourceEventType: "synthetic.reviewed-opening",
            ApprovalState: AccountingPostingApprovalStateDto.Approved, ApprovalId: "opening-review",
            Evidence: [new AccountingPostingEvidenceReferenceDto("opening-review", "evidence://synthetic/opening",
                AccountingPostingEvidenceKindDto.Source, "Synthetic", at, "opening-preparer")], LedgerBookId: scope.LedgerBookId)
        { Actor = "opening-poster", Provenance = Meridian.Contracts.Operations.DataProvenance.Simulated };
        return new LedgerJournalEntryWrite(entry, scope.LedgerBookId, scope.PeriodId, commandId,
            AccountingPolicyId: scope.PolicyId, AccountingPolicyVersion: scope.PolicyVersion,
            SourceEventId: id, PostingCommand: command, LedgerBookId: scope.LedgerBookId);
    }

}
