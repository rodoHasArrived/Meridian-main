using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Tests.Storage;

public sealed partial class AtomicTaxLotJournalStoreTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task BookFxSettlement_RetainsPartialSaleProofAcrossGaapAndCashBooks()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        const string fund = "synthetic-fx-sale";
        const string tenant = "synthetic-fx-tenant";
        const string company = "synthetic-fx-company";
        const string controller = "synthetic-fx-controller";
        const string role = "Controller";
        const string symbol = "SYN";
        const string account = "synthetic-broker";
        const string receivableName = "Trade Settlement Receivable";
        const string economicEvidence = "evidence://synthetic/foreign-sale";
        var tradeDate = new DateOnly(2026, 5, 12);
        var settlementDate = new DateOnly(2026, 6, 2);
        var at = DateTimeOffset.Parse("2026-05-01T00:00:00Z");
        var entity = Guid.NewGuid();
        var gaapId = Guid.NewGuid();
        var cashId = Guid.NewGuid();

        await new PostgresFundProfileTenancyRegistry(database.Options).BindAsync(fund, tenant, company);
        var setup = OpenStore();
        await setup.SaveLedgerBookAsync(Book(gaapId, AccountingBasisKindDto.Gaap));
        await setup.SaveLedgerBookAsync(Book(cashId, AccountingBasisKindDto.Cash));
        var tradePeriod = await OpenPeriod(setup, gaapId, 5);
        var gaapSettlementPeriod = await OpenPeriod(setup, gaapId, 6);
        var cashSettlementPeriod = await OpenPeriod(setup, cashId, 6);
        var gaapRefs = await database.SeedLotReferenceAuthorityAsync(gaapId, TestSecurityId, Guid.NewGuid(), financialAccountId: account);
        var cashRefs = await database.SeedLotReferenceAuthorityAsync(cashId, TestSecurityId, Guid.NewGuid(), financialAccountId: account);
        var writer = OpenStore(gaapRefs);
        var security = LedgerAccounts.Securities(LedgerSecurityKey.Unresolved(symbol), account);
        var receivable = new LedgerAccount(receivableName, LedgerAccountType.Asset, FinancialAccountId: account);
        var cash = LedgerAccounts.CashFor(account);
        var gain = LedgerAccounts.RealizedGainOrDefault(account);
        var fxGain = LedgerAccounts.RealizedFxGainOrDefault(account);
        var gaapLot = await SeedLot(writer, gaapId, gaapRefs, 7m);
        var cashLot = await SeedLot(writer, cashId, cashRefs, 6.5m);
        var rates = new ConsolidationFxPostgresStore(database.Options);
        var rateSetId = Guid.NewGuid();
        var quoteDate = at.AddMonths(2);
        await rates.RetainRateSetAsync(new(rateSetId, 1, tenant, company,
            [new(Guid.NewGuid(), "EUR", "USD", tradeDate, 1.10m, ConsolidationFxRateKindDto.TransactionDate, economicEvidence),
             new(Guid.NewGuid(), "EUR", "USD", settlementDate, 1.20m, ConsolidationFxRateKindDto.TransactionDate, economicEvidence)],
            economicEvidence, "synthetic-fx-preparer", controller, quoteDate));
        var tradeId = Guid.NewGuid();
        var settlementId = Guid.NewGuid();
        var planInput = new RetainBookFxSettlementSetRequestDto(Guid.NewGuid(), gaapId, cashId, tradeId, settlementId,
            TestSecurityId, symbol, account, receivableName, "EUR", tradeDate, settlementDate, 10m, 4m, 100m, 40m, 70m, 26m,
            rateSetId, 1, economicEvidence, Journal(tradePeriod), Journal(gaapSettlementPeriod), Journal(cashSettlementPeriod));
        var retained = await writer.RetainBookFxSettlementSetAsync(planInput, controller, role, tenant, company);
        retained.MissingLegs.Should().HaveCount(3);
        retained.Plan.Amounts.Should().Be(new BookFxSettlementAmountsDto(110m, 40m, 44m, 48m, 66m, 18m, 4m));
        (await OpenStore().RetainBookFxSettlementSetAsync(planInput, controller, role, tenant, company))
            .Should().BeEquivalentTo(retained);
        (await OpenStore().GetBookFxSettlementSetAsync(planInput.SetId, tenant, "unrelated-company")).Should().BeNull();
        var premature = () => Capture(writer, Command(BookFxSettlementLegDto.GaapTrade));
        await premature.Should().ThrowAsync<LedgerValidationException>().WithMessage("*still missing*");

        var trade = await writer.AppendAssetPostingAsync(Dispose(BookFxSettlementLegDto.GaapTrade, gaapRefs, gaapLot, 10m,
            (receivable, 110m, 0m, "EUR", 100m, 0m, 1.1m),
            (security, 0m, 70m, "USD", 0m, 70m, 1m),
            (gain, 0m, 40m, "USD", 0m, 40m, 1m)));
        var tradeCommand = Command(BookFxSettlementLegDto.GaapTrade);
        var first = await Task.WhenAll(Capture(writer, tradeCommand), Capture(OpenStore(), tradeCommand));
        first[0].Should().Be(first[1]);
        first[0].LotMutationBatchId.Should().Be(trade.MutationBatchId);

        await writer.AppendAsync(Write(BookFxSettlementLegDto.GaapSettlement, gaapRefs.Position.PositionId,
            (cash, 48m, 0m, "EUR", 40m, 0m, 1.2m),
            (receivable, 0m, 44m, "EUR", 0m, 40m, 1.1m),
            (fxGain, 0m, 4m, "USD", 0m, 4m, 1m)));
        await Capture(OpenStore(), Command(BookFxSettlementLegDto.GaapSettlement));
        var partial = await OpenStore().GetBookFxSettlementSetAsync(planInput.SetId, tenant, company);
        partial!.IsComplete.Should().BeFalse();
        partial.MissingLegs.Should().Equal(BookFxSettlementLegDto.CashSettlement);

        var settled = await writer.AppendAssetPostingAsync(Dispose(BookFxSettlementLegDto.CashSettlement, cashRefs, cashLot, 4m,
            (cash, 48m, 0m, "EUR", 40m, 0m, 1.2m),
            (security, 0m, 26m, "USD", 0m, 26m, 1m),
            (gain, 0m, 22m, "USD", 0m, 22m, 1m)));
        var cashCommand = Command(BookFxSettlementLegDto.CashSettlement);
        var cashReceipt = await Capture(OpenStore(), cashCommand);
        cashReceipt.LotMutationBatchId.Should().Be(settled.MutationBatchId);
        (await Capture(OpenStore(), cashCommand)).Should().Be(cashReceipt);
        var complete = await OpenStore().GetBookFxSettlementSetAsync(planInput.SetId, tenant, company);
        complete!.IsComplete.Should().BeTrue();
        complete.Receipts.Select(receipt => receipt.LedgerBookId).Should().Equal(gaapId, gaapId, cashId);
        (await writer.ListOpenTaxLotsAsync(cashId, security)).Should().ContainSingle().Which.OpenQuantity.Should().Be(6m);
        (await writer.QueryAsync(new LedgerJournalEntryQuery(SourceEventId: settlementId))).Should().HaveCount(2);

        PostgresLedgerJournalStore OpenStore(LotReferenceAuthority? refs = null) => new(database.Options,
            new BookFxFixtureTenant(tenant), backfillSecurityMaster: () => refs?.Securities,
            backfillPositions: () => refs?.Positions, postingAuthorities: LedgerPostgresTestDatabase.SyntheticFinancialPostingAuthorities);

        LedgerBookRecord Book(Guid bookId, AccountingBasisKindDto basis)
            => new(bookId, fund, entity, FundStructureNodeKindDto.Fund, $"Synthetic {basis}", "USD", at, at, AccountingBasis: basis);

        Task<LedgerAccountingPeriod> OpenPeriod(PostgresLedgerJournalStore store, Guid bookId, int month)
            => store.SavePeriodAsync(new(Guid.NewGuid(), bookId, 2026, month, $"2026-{month:00}", new(2026, month, 1),
                new(2026, month, DateTime.DaysInMonth(2026, month)), "Open", at, null, 0), 0);

        BookFxSettlementJournalDto Journal(LedgerAccountingPeriod period)
            => new(Guid.NewGuid(), period.PeriodId, period.Version, "legacy-v1", "legacy-v1");

        async Task<LedgerTaxLotRecord> SeedLot(PostgresLedgerJournalStore store, Guid bookId, LotReferenceAuthority reference, decimal cost)
        {
            var acquired = DateOnly.FromDateTime(at.UtcDateTime);
            var lotId = Guid.NewGuid();
            var evidence = BuildEvidence($"foreign-lot-{lotId:N}", 'c') with
            {
                SubjectType = "OpenLotAcquisition",
                SubjectId = lotId.ToString("D"),
                EffectiveDate = acquired
            };
            await store.SaveTaxLotPolicyAsync(new(Guid.NewGuid(), bookId, security,
                LedgerTaxLotReliefMethod.Fifo, "fx-lot-policy-v1", acquired, at, at));
            return await store.SaveTaxLotAsync(new(lotId, bookId, security, $"foreign-lot-{bookId:N}", acquired,
                10m, 10m, cost, "USD", at, at, Version: 1, SecurityId: TestSecurityId, BookPositionId: reference.Position.PositionId,
                Acquisition: new(LotQuantityBasis.Units, "USD", "USD", 1m, 10m * cost, 10m * cost, acquired, null, [evidence])));
        }

        LedgerJournalEntryWrite Write(BookFxSettlementLegDto leg, Guid positionId,
            params (LedgerAccount Account, decimal Debit, decimal Credit, string Currency, decimal OriginalDebit, decimal OriginalCredit, decimal Rate)[] lines)
        {
            var target = BookFxSettlementProjection.Resolve(planInput, leg);
            var id = target.Journal.JournalEntryId;
            var stamp = new DateTimeOffset(target.Date.ToDateTime(new TimeOnly(14, 0)), TimeSpan.Zero);
            const string description = "Synthetic foreign securities sale";
            var tags = SecurityMasterLineageTags(TestSecurityId);
            tags["securityMasterLineage"] = $"{symbol}:{TestSecurityId:N}:ledger-map:{symbol}-investment-lots:"
                + $"sm-approval:lot-controller:security-status:active:{tags["securityMasterProvenance"]}";
            return new(new JournalEntry(id, stamp, description,
                lines.Select(line => new LedgerEntry(Guid.NewGuid(), id, stamp, line.Account,
                    line.Debit, line.Credit, description, new LedgerLineDimensionSet(InstrumentId: TestSecurityId) { PositionId = positionId },
                    new LedgerEntryCurrency(line.Currency, "USD", line.OriginalDebit, line.OriginalCredit, line.Rate))).ToArray(),
                new JournalEntryMetadata(Symbol: symbol, SecurityId: TestSecurityId, FinancialAccountId: account,
                    EffectiveDate: target.Date, IdempotencyKey: $"fx-set:{id:D}", Tags: tags,
                    EvidenceReferences: [new("source", economicEvidence, "Statement", "synthetic", stamp, controller)])),
                target.BookId, target.Journal.PeriodId, Guid.NewGuid(), AccountingBasis: target.Basis, SourceEventId: target.SourceEventId,
                LedgerBookId: target.BookId);
        }

        AtomicTaxLotJournalCommand Dispose(BookFxSettlementLegDto leg, LotReferenceAuthority authority, LedgerTaxLotRecord lot,
            decimal quantity, params (LedgerAccount Account, decimal Debit, decimal Credit, string Currency, decimal OriginalDebit, decimal OriginalCredit, decimal Rate)[] lines)
        {
            var journal = Write(leg, authority.Position.PositionId, lines);
            var target = BookFxSettlementProjection.Resolve(planInput, leg);
            var evidence = BuildEvidence($"fx-disposal-{leg}", 'd');
            return AtomicTaxLotJournalCommand.Create(Guid.NewGuid(), target.BookId, journal, target.SourceEventId,
                journal.Entry.Metadata.IdempotencyKey!, target.Journal.PeriodVersion, AtomicTaxLotMutationKind.Disposal, [evidence],
                disposalSelections: [new(lot.TaxLotRecordId, lot.LotId, lot.Version, lot.OpenQuantity, quantity,
                    0, evidence.EvidenceId, lot.UnitCost, quantity * lot.UnitCost)],
                reliefMethod: "Fifo", policyRevision: "fx-lot-policy-v1", referenceVersions: authority.Versions);
        }

        CaptureBookFxSettlementLegRequestDto Command(BookFxSettlementLegDto leg)
            => new(Guid.NewGuid(), retained.Plan.PlanHashSha256, leg);

        Task<BookFxSettlementLegReceiptDto> Capture(PostgresLedgerJournalStore store, CaptureBookFxSettlementLegRequestDto command)
            => store.CaptureBookFxSettlementLegAsync(planInput.SetId, command, controller, role, tenant, company);
    }

    private sealed class BookFxFixtureTenant(string tenant) : IFundScopeTenantAccessor
    {
        public string ResolveCallerTenant() => tenant;
    }
}
