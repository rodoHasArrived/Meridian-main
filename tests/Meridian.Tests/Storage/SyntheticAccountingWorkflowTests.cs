using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.FinancialOperations.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Storage;

public sealed partial class AutomatedPostingGrantTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task SyntheticAccountingWorkflow_UsesOneAccountBookAndEquityIdentityAcrossPostingReportingAndReconciliation()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        const string company = "synthetic-company";
        var accountId = Guid.NewGuid();
        var account = accountId.ToString("D");
        var setup = await CreateScopeAsync(database, company);
        var reference = setup.Reference with { FinancialAccountId = account };
        var book = (await database.JournalStore.GetLedgerBookAsync(reference.LedgerBookId))! with
        {
            FundStructureNodeKind = FundStructureNodeKindDto.Account,
            FundStructureNodeId = accountId
        };
        await database.JournalStore.SaveLedgerBookAsync(book);
        var securityId = Guid.NewGuid();
        var authority = await database.SeedLotReferenceAuthorityAsync(book.LedgerBookId, securityId,
            Guid.NewGuid(), financialAccountId: account);
        var chart = new[]
        {
            new ChartOfAccountsNodeDto("cash", "Assets:Cash", "Cash", "Asset"),
            new ChartOfAccountsNodeDto("securities", "Assets:Securities", "Securities", "Asset"),
            new ChartOfAccountsNodeDto("gains", "Revenue:Gains", "Realized Gain", "Revenue"),
            new ChartOfAccountsNodeDto("losses", "Expenses:Losses", "Realized Loss", "Expense"),
            new ChartOfAccountsNodeDto("commissions", "Expenses:Commissions", "Commissions", "Expense"),
            new ChartOfAccountsNodeDto("fees", "Expenses:Fees", "Fees", "Expense")
        };
        var workspace = setup.Workspace with { ChartOfAccounts = chart };
        await database.AccountingConfigurationStore.SaveAsync(workspace);
        var carrying = new LedgerAccount("Securities", LedgerAccountType.Asset, "AAPL", account);
        await database.JournalStore.SaveTaxLotPolicyAsync(new LedgerAccountTaxLotPolicyRecord(Guid.NewGuid(),
            book.LedgerBookId, carrying, LedgerTaxLotReliefMethod.Fifo, "fifo-v1", setup.Period.StartDate,
            Timestamp, Timestamp));
        var config = Substitute.For<IAccountingConfigurationService>();
        config.GetWorkspaceAsync(Arg.Any<string>(), book.LedgerBookId, Arg.Any<CancellationToken>(), TenantId, company)
            .Returns(workspace);
        var policies = new AccountingPolicyService();
        var policySnapshot = await policies.CreatePolicyAsync(new CreateAccountingPolicyRequest(AccountingBasisKindDto.Primary,
            reference.PolicyId, reference.PolicyVersion, "Synthetic accounting integration", setup.Period.StartDate,
            RulePack: new AccountingPolicyRulePackDto("synthetic", "v1",
            [
                new("execution.trade-fill", AccountingTreatmentKindDto.General, SourceEventType: "execution.trade-fill", RequiresApproval: false, AllowsAutoPosting: true),
                new("execution.trade-commission", AccountingTreatmentKindDto.Expense, SourceEventType: "execution.trade-commission", RequiresApproval: false, AllowsAutoPosting: true),
                new("execution.trade-fee", AccountingTreatmentKindDto.Expense, SourceEventType: "execution.trade-fee", RequiresApproval: false, AllowsAutoPosting: true)
            ])));
        var grant = await database.JournalStore.ActivateWithPolicyAsync(Activation(reference), policySnapshot,
            "synthetic-operator", TenantId);
        var detail = new SecurityDetailDto(securityId, "Equity", SecurityStatusDto.Active, "Synthetic AAPL", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new { }), [], [],
            authority.Security.Version, authority.Security.EffectiveFrom, null);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetByIdAsync(securityId, Arg.Any<CancellationToken>()).Returns(detail);
        var positions = Substitute.For<IAssetOperationsQueryService>();
        positions.GetOperationsAsync(securityId, Arg.Any<CancellationToken>()).Returns(new AssetOperationsDetailDto(
            new(securityId, "Equity", "Synthetic AAPL", "AAPL", []), [], [], [], [], [], [], [], [],
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
        var context = new TradeFillLedgerPostingContext("synthetic-accounting-integration/v2", book.LedgerBookId,
            setup.Period.PeriodId, book.LedgerBookId, reference.PolicyId, reference.PolicyVersion,
            setup.Period.Version, 2);
        await database.JournalStore.AppendAsync(IntegrationOpening(book, setup.Period, account));
        var root = Path.Combine(Path.GetTempPath(), "meridian-accounting-workflow", Guid.NewGuid().ToString("N"));
        using var target = new DurableLedgerPostingTarget(database.JournalStore);
        var service = new AutomatedTradeFillAccountingService(options, root, database.JournalStore, database.JournalStore,
            target, new ConfiguredLedgerAccountResolver(config), new AccountingAutomaticPostingPolicyValidator(policies), securities, positions);
        var buy = new TradeExecutedEvent(Guid.NewGuid(), "synthetic-buy", "AAPL", OrderSide.Buy,
            100m, 100m, 1m, 0m, 0m, Timestamp, account)
        { SourceMode = "Synthetic", SettlementCurrency = "USD", Fees = [new("exchange-fee", "Exchange", 0.25m, "USD")] };
        try
        {
            await service.PostAsync(buy, context, securityId);
            await service.PostAsync(buy with
            {
                FillId = Guid.NewGuid(),
                OrderId = "synthetic-sale",
                Side = OrderSide.Sell,
                FilledQuantity = 40m,
                FillPrice = 110m,
                RealizedPnl = 987654m,
                Fees = null
            }, context, securityId);

            // Supported instrument receivables and a separate financial account's loan are not
            // equity lot carrying cost. They share this book without changing brokerage cash.
            await database.JournalStore.AppendAsync(IntegrationNonLotJournal(book, setup.Period, account,
                "AAPL", securityId, "DividendDeclared", "Dividend Receivable", "Dividend Income", 100m));
            var loanAccount = Guid.NewGuid().ToString("D");
            await database.JournalStore.AppendAsync(IntegrationOpening(book, setup.Period, loanAccount, 5000m));
            await database.JournalStore.AppendAsync(IntegrationNonLotJournal(book, setup.Period, loanAccount,
                "LOAN-SYNTHETIC", Guid.NewGuid(), "loan.drawdown-booked", "LoanPrincipal", "Cash", 1000m));

            var snapshot = await database.JournalStore.CaptureOpenPositionReportingSnapshotAsync(
                new(LedgerBookId: book.LedgerBookId, PeriodId: setup.Period.PeriodId), setup.Period.PeriodId);
            snapshot.OpenPositions!.Blockers.Should().BeEmpty();
            snapshot.OpenPositions.CoverageBlockers.Should().BeEmpty();
            var lot = snapshot.OpenPositions.Lots.Should().ContainSingle().Which;
            lot.OpenQuantity.Should().Be(60m);
            lot.ToOpenLot().OpenFunctionalCostBasis.Should().Be(6000m);
            var prices = IntegrationCloseMarks(book, securityId, setup.Period.EndDate);
            var inputs = new GeneralLedgerUnrealizedInputs(prices, [detail]);
            var pnlRows = GeneralLedgerSymbolPnlBuilder.Build(book, snapshot, inputs);
            var pnl = pnlRows.Single(row => row.Symbol == "AAPL" && row.FinancialAccountId == account.ToUpperInvariant());
            pnl.RealizedAvailability.Should().Be("AvailableForRetainedDisposals");
            decimal.Parse(pnl.BookRealizedGainLoss!, CultureInfo.InvariantCulture).Should().Be(400m);
            pnl.UnrealizedAvailability.Should().Be("Available");
            decimal.Parse(pnl.UnrealizedGainLoss!, CultureInfo.InvariantCulture).Should().Be(600m);
            pnlRows.Single(row => row.Symbol == "LOAN-SYNTHETIC").UnrealizedAvailability.Should().Be("Unavailable");

            var cash = snapshot.Journals.SelectMany(record => record.Entry.Lines)
                .Where(line => line.Account.FinancialAccountId == account && line.Account.Name == "Cash")
                .Sum(line => line.Debit - line.Credit);
            cash.Should().Be(94397.75m); // 100000 - 10000 - 1 - .25 + 4400 - 1.
            var cutoff = new DateTimeOffset(setup.Period.EndDate, TimeOnly.MaxValue, TimeSpan.Zero);
            var ledgerSource = new ThreeWaySourceSnapshotDto("ledger", cutoff, "synthetic:posted-ledger", cash,
                [new(securityId, "AAPL", lot.OpenQuantity, lot.ToOpenLot().OpenFunctionalCostBasis)],
                ThreeWayReconciliationOptions.SupportedBasisConvention, [], "simulated");
            // Independent broker and OMS fixture operands are calculated from accepted activity,
            // not copied from the ledger output; the provider evidence remains synthetic.
            var omsSource = new ThreeWaySourceSnapshotDto("oms", cutoff, "synthetic:oms-activity", 94397.75m,
                [new(securityId, "AAPL", 60m, 6000m)], ThreeWayReconciliationOptions.SupportedBasisConvention, [], "simulated");
            var brokerSource = new ThreeWaySourceSnapshotDto("broker", cutoff, "synthetic:broker-statement", 94397.75m,
                [new(securityId, "AAPL", 60m, 6000m)], ThreeWayReconciliationOptions.SupportedBasisConvention, [], "simulated");
            var checks = ThreeWayReconciliationComparer.Compare([ledgerSource, omsSource, brokerSource], cutoff,
                TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5));
            checks.Should().OnlyContain(check => check.Status == "Matched");
            var export = await GeneralLedgerExportBuilder.BuildAsync(book, snapshot, unrealizedInputs: inputs);
            export.RowCount.Should().Be(snapshot.Journals.Sum(record => record.Entry.Lines.Count));
            decimal.Parse(GeneralLedgerExportBuilder.BuildDataset(book, snapshot, inputs).Variance,
                CultureInfo.InvariantCulture).Should().Be(0m);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static LedgerJournalEntryWrite IntegrationOpening(LedgerBookRecord book, LedgerAccountingPeriod period, string account, decimal amount = 100000m)
    {
        var id = Guid.NewGuid();
        var at = Timestamp.AddHours(-1);
        var key = "synthetic-approved-opening:" + id.ToString("D");
        var entry = new JournalEntry(id, at, "Reviewed synthetic opening cash",
        [
            new(Guid.NewGuid(), id, at, new LedgerAccount("Cash", LedgerAccountType.Asset, FinancialAccountId: account),
                amount, 0m, "Reviewed synthetic opening cash", currency: new("USD", "USD", amount, 0m, 1m)),
            new(Guid.NewGuid(), id, at, new LedgerAccount("Capital Account", LedgerAccountType.Equity, FinancialAccountId: account),
                0m, amount, "Reviewed synthetic opening cash", currency: new("USD", "USD", 0m, amount, 1m))
        ], new JournalEntryMetadata(ActivityType: "opening-balance", FinancialAccountId: account,
            EffectiveDate: DateOnly.FromDateTime(at.UtcDateTime), IdempotencyKey: key));
        var command = new AccountingPostingCommandDto(id, book.LedgerBookId, period.PeriodId,
            DateOnly.FromDateTime(at.UtcDateTime), at, key, ExpectedVersion: period.Version,
            SourceEventType: "synthetic.reviewed-opening", ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "synthetic-opening-review", Evidence: [new("opening-evidence", "synthetic://opening",
                AccountingPostingEvidenceKindDto.Source, "Synthetic", at, "opening-preparer")], LedgerBookId: book.LedgerBookId)
        { Actor = "opening-poster", Provenance = DataProvenance.Simulated };
        return new(entry, book.LedgerBookId, period.PeriodId, CommandId: id,
            AccountingPolicyId: book.AccountingPolicyId, AccountingPolicyVersion: book.AccountingPolicyVersion,
            PostingCommand: command, LedgerBookId: book.LedgerBookId);
    }

    private static LedgerJournalEntryWrite IntegrationNonLotJournal(LedgerBookRecord book, LedgerAccountingPeriod period,
        string account, string symbol, Guid securityId, string activity, string debitAccount, string creditAccount, decimal amount)
    {
        var id = Guid.NewGuid();
        var at = Timestamp.AddMinutes(10);
        var loan = activity.StartsWith("loan.", StringComparison.Ordinal);
        var key = "synthetic-source:" + id.ToString("D");
        var source = new JournalEvidenceReference(key,
            (loan ? "direct-lending://events/" : "synthetic://accounting/events/") + id.ToString("D"),
            "Source", loan ? "DirectLending" : "SecurityMaster", at, "source-preparer",
            SubjectId: loan ? account : id.ToString("D"), Description: activity);
        var provenance = $"security-master:{securityId:N};snapshot:synthetic-approved-source;approved:true";
        var tags = new Dictionary<string, string>
        {
            ["securityMasterProvenance"] = provenance,
            ["securityMasterLineage"] = $"{symbol}:{securityId:N}:ledger-map:{securityId:N}:sm-approval:synthetic-controller:security-status:active:{provenance}"
        };
        if (loan)
        {
            tags["loanId"] = account;
            tags["sourceEventType"] = activity;
        }
        var entry = new JournalEntry(id, at, activity,
        [
            new(Guid.NewGuid(), id, at, new LedgerAccount(debitAccount, LedgerAccountType.Asset, symbol, account),
                amount, 0m, activity, currency: new("USD", "USD", amount, 0m, 1m)),
            new(Guid.NewGuid(), id, at, new LedgerAccount(creditAccount, loan ? LedgerAccountType.Asset : LedgerAccountType.Revenue,
                loan ? null : symbol, account), 0m, amount, activity, currency: new("USD", "USD", 0m, amount, 1m))
        ], new JournalEntryMetadata(ActivityType: activity, Symbol: symbol, SecurityId: securityId,
            FinancialAccountId: account, Institution: loan ? "DirectLending" : "SecurityMaster",
            EffectiveDate: DateOnly.FromDateTime(at.UtcDateTime), IdempotencyKey: key,
            Tags: tags,
            EvidenceReferences: [source]));
        var command = new AccountingPostingCommandDto(id, book.LedgerBookId, period.PeriodId,
            DateOnly.FromDateTime(at.UtcDateTime), at, key, SourceEventId: id, ExpectedVersion: period.Version,
            SourceEventType: activity, ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "synthetic-journal-review", Evidence: [new(source.EvidenceId, source.Uri,
                AccountingPostingEvidenceKindDto.Source, source.SourceSystem, at, "source-preparer")], LedgerBookId: book.LedgerBookId)
        { Actor = "journal-poster", Provenance = DataProvenance.Simulated };
        return new(entry, book.LedgerBookId, period.PeriodId, CommandId: id, SourceEventId: id,
            AccountingPolicyId: book.AccountingPolicyId, AccountingPolicyVersion: book.AccountingPolicyVersion,
            PostingCommand: command, LedgerBookId: book.LedgerBookId);
    }

    private static SecurityClosePriceSetDto IntegrationCloseMarks(LedgerBookRecord book, Guid securityId, DateOnly date)
    {
        var evaluated = new DateTimeOffset(date, new TimeOnly(20, 0), TimeSpan.Zero);
        var account = book.FundStructureNodeId.ToString("D");
        var hierarchy = new SecurityPricingHierarchyDto(securityId, account,
            [new(1, "synthetic-close", "Synthetic close", 1)], evaluated.AddDays(-1), "pricing-reviewer");
        var selection = new SecurityPriceGoldenCopyDto(securityId, 110m, SecurityPriceKind.MarketGoldenCopy,
            "synthetic-close", evaluated, false, 0, [], SecurityPriceUnit.CurrencyPerUnit,
            evaluated, hierarchy.AsOf, evaluated, Guid.NewGuid(), hierarchy);
        var close = new SecurityClosePriceSnapshotDto(Guid.NewGuid(), account, book.LedgerBookId.ToString("D"),
            date, 1, null, null, evaluated, evaluated, 1m, "price-preparer", evaluated,
            [new(securityId, selection, [new("synthetic-close", 110m, evaluated, SecurityPriceUnit.CurrencyPerUnit)],
                null, 110m, SecurityPriceUnit.CurrencyPerUnit)], []);
        return new(close, new(close.SnapshotId, SecurityClosePriceEvidence.Digest(close),
            "independent-pricing-reviewer", "Reviewed synthetic close", evaluated.AddHours(1)));
    }
}
