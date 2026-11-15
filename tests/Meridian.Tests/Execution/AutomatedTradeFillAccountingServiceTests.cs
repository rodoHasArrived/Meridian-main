using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Events;
using Meridian.Execution.Sdk;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.Execution;

public sealed class AutomatedTradeFillAccountingServiceTests
{
    [Fact]
    public async Task Buy_retains_currency_lot_and_fees_and_restart_reuses_prepared_intent()
    {
        using var fixture = new Fixture();
        var fill = fixture.Fill() with { Commission = 1m, Fees = [new("regulatory-1", "Regulatory", 0.25m, "USD")] };
        var first = await fixture.Service.PostAsync(fill, fixture.Context, fixture.SecurityId);
        first.Should().HaveCount(3);
        var trade = fixture.Commands.Single();
        trade.AcquisitionLot!.Acquisition!.FunctionalCostBasis.Should().Be(1000m);
        trade.Journal.Entry.Lines.Sum(line => line.Debit).Should().Be(1000m);
        trade.Journal.PostingCommand!.ActionOrigin.Should().Be(OperationsActionOriginDto.AuthorizedSystem);
        trade.Journal.PostingCommand.Provenance.Should().Be(DataProvenance.Simulated);
        trade.Journal.PostingCommand.AutomationGrant.Should().Be(fixture.Grant);
        first.Should().OnlyContain(entry => entry.Metadata.Tags!["securityMasterProvenance"].Contains(
            $"security-master:{fixture.SecurityId:N};server-resolved:true;status:Active;version:1"));
        trade.Journal.Entry.Metadata.Tags!["securityMasterLineage"].Should()
            .Contain($"ledger-map:execution:{fill.Symbol}:{fixture.SecurityId:N}")
            .And.Contain($"sm-approval:security-master-active:{fixture.SecurityId:N}")
            .And.Contain("security-status:Active");
        trade.Journal.Entry.Lines.Should().OnlyContain(line => line.Currency!.TransactionCurrency == "USD"
            && line.Currency.FxRateToFunctional == 1m && line.Account.FinancialAccountId == "account-1");
        trade.RetainedEvidence.Should().OnlyContain(e => RetainedEvidenceIdentityValidator.IsComplete(e));
        first.Skip(1).Should().OnlyContain(entry => !entry.Metadata.Tags!.ContainsKey("quantity"));

        // A restart and policy/grant revocation cannot cause fresh selection or erase committed receipts.
        ((IAutomatedPostingGrantStore)fixture.Journals).GetAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AutomatedPostingGrantDto(fixture.Grant with { Version = 2 }, false, "operator", fixture.Time));
        var restarted = fixture.CreateService();
        var second = await restarted.PostAsync(fill with { Fees = fill.Fees!.ToArray() }, fixture.Context, fixture.SecurityId);
        second.Select(entry => entry.JournalEntryId).Should().Equal(first.Select(entry => entry.JournalEntryId));
        await fixture.Policies.Received(4).AcquireRetainedAuthorityAsync(Arg.Any<AccountingPolicyDto>(),
            Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Changed_fill_cannot_reuse_retained_lot_selection()
    {
        using var fixture = new Fixture();
        var fill = fixture.Fill();
        await fixture.Service.PostAsync(fill, fixture.Context, fixture.SecurityId);
        var replay = () => fixture.CreateService().PostAsync(fill with { FillPrice = 101m }, fixture.Context, fixture.SecurityId);
        await replay.Should().ThrowAsync<InvalidDataException>().WithMessage("*conflicts*");
        fixture.Commands.Should().HaveCount(1);
    }

    [Fact]
    public async Task Disposal_uses_canonical_FIFO_basis_instead_of_OMS_profit()
    {
        using var fixture = new Fixture();
        var buy = fixture.Fill();
        await fixture.Service.PostAsync(buy, fixture.Context, fixture.SecurityId);
        var acquisition = fixture.Commands.Single().AcquisitionLot! with { Version = 1 };
        fixture.Journals.ListOpenTaxLotsAsync(fixture.Context.LedgerBookId, Arg.Any<LedgerAccount>(), Arg.Any<CancellationToken>())
            .Returns(new[] { acquisition });
        fixture.Journals.ListTaxLotPoliciesAsync(fixture.Context.LedgerBookId, Arg.Any<CancellationToken>())
            .Returns(new[] { new LedgerAccountTaxLotPolicyRecord(Guid.NewGuid(), fixture.Context.LedgerBookId,
                acquisition.Account, LedgerTaxLotReliefMethod.Fifo, "fifo-v1", DateOnly.FromDateTime(fixture.Time.UtcDateTime), fixture.Time, fixture.Time) });
        var sale = fixture.Fill() with { Side = OrderSide.Sell, FilledQuantity = 4m, FillPrice = 110m, RealizedPnl = 999m };
        await fixture.Service.PostAsync(sale, fixture.Context, fixture.SecurityId);
        var command = fixture.Commands.Last();
        var selection = command.DisposalSelections.Should().ContainSingle().Which;
        selection.ExpectedCostBasis.Should().Be(400m);
        selection.SelectionOrdinal.Should().Be(0);
        command.Journal.Entry.Lines.Single(line => line.Account.AccountType == LedgerAccountType.Revenue).Credit.Should().Be(40m);
        command.ReferenceVersions.Should().Be(new AtomicLotReferenceVersions(1, 1));
    }

    [Fact]
    public async Task Missing_basis_does_not_fall_back_to_zero_realized_profit()
    {
        using var fixture = new Fixture();
        fixture.Journals.ListTaxLotPoliciesAsync(fixture.Context.LedgerBookId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LedgerAccountTaxLotPolicyRecord>());
        var sale = () => fixture.Service.PostAsync(fixture.Fill() with { Side = OrderSide.Sell, RealizedPnl = 0m }, fixture.Context, fixture.SecurityId);
        await sale.Should().ThrowAsync<InvalidOperationException>().WithMessage("*FIFO*");
        fixture.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Live")]
    [InlineData(null)]
    public async Task Unknown_or_live_source_is_never_granted_synthetic_authority(string? source)
    {
        using var fixture = new Fixture();
        var attempt = () => fixture.Service.PostAsync(fixture.Fill() with { SourceMode = source }, fixture.Context, fixture.SecurityId);
        await attempt.Should().ThrowAsync<InvalidOperationException>();
        fixture.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_fee_receipt_keeps_fill_incomplete_and_retry_recovers_same_lot_intent()
    {
        using var fixture = new Fixture();
        var fill = fixture.Fill() with { Commission = 1m };
        fixture.FeeFailure = true;
        var failed = () => fixture.Service.PostAsync(fill, fixture.Context, fixture.SecurityId);
        await failed.Should().ThrowAsync<IOException>();
        var batch = fixture.Commands.Single().MutationBatchId;
        fixture.FeeFailure = false;
        var completed = await fixture.CreateService().PostAsync(fill, fixture.Context, fixture.SecurityId);
        completed.Should().HaveCount(2);
        fixture.Commands.Should().OnlyContain(command => command.MutationBatchId == batch);
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public async Task V2_refuses_native_multiplier_and_variation_margin_shapes(int multiplier, bool variationMargin)
    {
        using var fixture = new Fixture();
        var attempt = () => fixture.Service.PostAsync(fixture.Fill() with
        { ContractMultiplier = multiplier, SettlesVariationMargin = variationMargin }, fixture.Context, fixture.SecurityId);
        await attempt.Should().ThrowAsync<InvalidOperationException>();
        fixture.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task V2_refuses_financed_native_accounting_evidence()
    {
        using var fixture = new Fixture();
        var fill = fixture.Fill() with
        {
            AccountingEvidenceVersion = 1,
            AccountingEvidence = new TradeFillAccountingEvidence(Opening: new SecurityTradePosting(
                SecurityTradePostingKind.OpenLong, 10m, 1000m, BorrowedAmount: 1m))
        };
        var attempt = () => fixture.Service.PostAsync(fill, fixture.Context, fixture.SecurityId);
        await attempt.Should().ThrowAsync<InvalidOperationException>().WithMessage("*financing*");
        fixture.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task FinancialAccountGuidSpellingMatchesLedgerIdentityWithoutChangingGrantPayload()
    {
        using var fixture = new Fixture(financialAccountId: Guid.NewGuid().ToString("D").ToUpperInvariant());
        var fill = fixture.Fill() with { FinancialAccountId = fixture.Grant.FinancialAccountId.ToLowerInvariant() };
        var journals = await fixture.Service.PostAsync(fill, fixture.Context, fixture.SecurityId);
        journals.Should().ContainSingle();
        fixture.Commands.Should().ContainSingle().Which.Journal.PostingCommand!.AutomationGrant.Should().Be(fixture.Grant);
        journals[0].Metadata.FinancialAccountId.Should().Be(fill.FinancialAccountId);
    }

    [Fact]
    public async Task Missing_opening_cash_cannot_be_inferred_as_available_funding()
    {
        using var fixture = new Fixture();
        fixture.Journals.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<LedgerJournalEntryRecord>());
        var attempt = () => fixture.Service.PostAsync(fixture.Fill(), fixture.Context, fixture.SecurityId);
        await attempt.Should().ThrowAsync<InvalidOperationException>().WithMessage("*opening cash evidence*");
        fixture.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Buy_reserves_required_fees_in_proven_cash_check_and_never_infers_margin()
    {
        using var fixture = new Fixture(openingCash: 1000m);
        var attempt = () => fixture.Service.PostAsync(fixture.Fill() with { Commission = 1m }, fixture.Context, fixture.SecurityId);
        await attempt.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Insufficient proven cash*");
        fixture.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_buys_cannot_spend_the_same_proven_cash_in_one_accounting_host()
    {
        using var fixture = new Fixture(openingCash: 1500m);
        var first = fixture.Service.PostAsync(fixture.Fill(), fixture.Context, fixture.SecurityId);
        var second = fixture.Service.PostAsync(fixture.Fill(), fixture.Context, fixture.SecurityId);
        var both = () => Task.WhenAll(first, second);
        await both.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Insufficient proven cash*");
        fixture.Commands.Should().ContainSingle();
        (first.IsCompletedSuccessfully || second.IsCompletedSuccessfully).Should().BeTrue();
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-fill-v2", Guid.NewGuid().ToString("N"));
        private readonly Dictionary<Guid, AtomicTaxLotJournalResult> _receipts = [];
        private readonly Dictionary<Guid, LedgerJournalEntryRecord> _journals = [];
        private readonly IAtomicTaxLotJournalStore _atomic = Substitute.For<IAtomicTaxLotJournalStore>();
        private readonly IGovernedLedgerPostingTarget _target = Substitute.For<IGovernedLedgerPostingTarget>();
        private readonly IConfiguredLedgerAccountResolver _accounts = Substitute.For<IConfiguredLedgerAccountResolver>();
        private readonly ISecurityMasterQueryService _securities = Substitute.For<ISecurityMasterQueryService>();
        private readonly IAssetOperationsQueryService _positions = Substitute.For<IAssetOperationsQueryService>();
        private readonly AutomatedTradeFillAccountingOptions _options;
        internal readonly ILedgerJournalStore Journals = Substitute.For<ILedgerJournalStore, IAutomatedPostingGrantStore>();
        internal readonly IAccountingAutomaticPostingPolicyValidator Policies = Substitute.For<IAccountingAutomaticPostingPolicyValidator>();
        internal readonly Guid SecurityId = Guid.NewGuid();
        internal readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-01-05T12:00:00Z");
        internal readonly TradeFillLedgerPostingContext Context;
        internal readonly AutomatedPostingGrantReferenceDto Grant;
        internal readonly List<AtomicTaxLotJournalCommand> Commands = [];
        internal bool FeeFailure;
        internal AutomatedTradeFillAccountingService Service { get; }

        internal Fixture(decimal openingCash = 100_000m, string financialAccountId = "account-1")
        {
            Policies.AcquireRetainedAuthorityAsync(Arg.Any<AccountingPolicyDto>(), Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(new NoopLease());
            Policies.ResolveRuleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<AccountingBasisKindDto>(),
                Arg.Any<DateOnly>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
                    new AccountingPolicyRuleDto(call.ArgAt<string>(4), AccountingTreatmentKindDto.General, "v1",
                        call.ArgAt<string>(4), RequiresApproval: false, AllowsAutoPosting: true));
            Policies.AcquireAuthorityAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<AccountingBasisKindDto>(),
                Arg.Any<DateOnly>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>()).Returns(new NoopLease());
            var bookId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            Context = new("fixture/v2", bookId, periodId, bookId, "auto-v1", "1", 0, 2);
            Grant = new(Guid.NewGuid(), 1, "tenant-1", financialAccountId, bookId, periodId, "auto-v1", "1", "config-v1", "Synthetic");
            var book = new LedgerBookRecord(bookId, "fund-1", Guid.NewGuid(), FundStructureNodeKindDto.Fund,
                "Synthetic book", "USD", Time, Time, AccountingPolicyId: "auto-v1", AccountingPolicyVersion: "1");
            Journals.GetLedgerBookAsync(bookId, Arg.Any<CancellationToken>()).Returns(book);
            const string openingDescription = "Reviewed synthetic opening cash";
            var openingId = Guid.NewGuid();
            var openingEntry = new JournalEntry(openingId, Time.AddDays(-1), openingDescription,
            [
                new LedgerEntry(Guid.NewGuid(), openingId, Time.AddDays(-1), new LedgerAccount("Cash", LedgerAccountType.Asset,
                    FinancialAccountId: financialAccountId), openingCash, 0m, openingDescription,
                    currency: new LedgerEntryCurrency("USD", "USD", openingCash, 0m, 1m)),
                new LedgerEntry(Guid.NewGuid(), openingId, Time.AddDays(-1), new LedgerAccount("Capital", LedgerAccountType.Equity,
                    FinancialAccountId: financialAccountId), 0m, openingCash, openingDescription,
                    currency: new LedgerEntryCurrency("USD", "USD", 0m, openingCash, 1m))
            ], new JournalEntryMetadata(ActivityType: "opening-balance", FinancialAccountId: financialAccountId,
                LedgerBook: bookId.ToString("D"), EffectiveDate: DateOnly.FromDateTime(Time.AddDays(-1).UtcDateTime)));
            var opening = Record(new LedgerJournalEntryWrite(openingEntry, bookId, periodId));
            Journals.QueryAsync(Arg.Any<LedgerJournalEntryQuery>(), Arg.Any<CancellationToken>())
                .Returns(call => new[] { opening }.Concat(_receipts.Values.Select(receipt => receipt.Journal)).Concat(_journals.Values).ToArray());
            ((IAutomatedPostingGrantStore)Journals).GetAsync(Grant.GrantId, Grant.TenantId, Arg.Any<CancellationToken>())
                .Returns(new AutomatedPostingGrantDto(Grant, true, "operator", Time)
                {
                    PolicySnapshot = new AccountingPolicyDto(Grant.PolicyId, AccountingBasisKindDto.Primary,
                    Grant.PolicyVersion, "Synthetic", DateOnly.FromDateTime(Time.UtcDateTime), null, false, "{}", Time,
                    RulePack: new("synthetic", "v1", new[] { "execution.trade-fill", "execution.trade-commission", "execution.trade-fee" }
                        .Select(source => new AccountingPolicyRuleDto(source, AccountingTreatmentKindDto.General, "v1", source,
                            RequiresApproval: false, AllowsAutoPosting: true)).ToArray()))
                });
            var emptyJson = JsonSerializer.SerializeToElement(new { });
            _securities.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityDetailDto(SecurityId,
                "Equity", SecurityStatusDto.Active, "Test", "USD", emptyJson, emptyJson, [], [], 1, Time.AddYears(-1), null));
            var position = new BookPositionDto(Guid.NewGuid(), SecurityId, Guid.NewGuid(), new AccountingBookContextDto(bookId,
                book.FundProfileId, book.FundStructureNodeId, book.FundStructureNodeKind, book.DisplayName, "USD",
                book.AccountingBasis, "auto-v1", "1", periodId), "Long", "Active", DateOnly.FromDateTime(Time.UtcDateTime),
                PrimaryAccountId: financialAccountId);
            _positions.GetOperationsAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new AssetOperationsDetailDto(
                new(SecurityId, "Equity", "Test", "TEST", []), [], [], [], [], [], [], [], [],
                new(SecurityId, "Ready", [], [], [], [], Time, "Synthetic", null), [])
            { BookPositions = [position] });
            _accounts.ResolveAsync(Arg.Any<ConfiguredLedgerAccountRequestDto>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var request = call.Arg<ConfiguredLedgerAccountRequestDto>();
                return new ChartOfAccountsNodeDto(request.AccountPath, request.AccountPath, request.AccountPath, request.ExpectedAccountType);
            });
            _options = new()
            {
                Grant = Grant,
                CashAccountPath = "Cash",
                SecuritiesAccountPath = "Securities",
                RealizedGainAccountPath = "Realized Gain",
                RealizedLossAccountPath = "Realized Loss",
                CommissionAccountPath = "Commissions",
                FeeAccountPath = "Fees"
            };
            _atomic.AppendAssetPostingAsync(Arg.Any<AtomicTaxLotJournalCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var command = call.Arg<AtomicTaxLotJournalCommand>();
                Commands.Add(command);
                if (!_receipts.TryGetValue(command.MutationBatchId, out var result))
                {
                    result = new(command.MutationBatchId, command.MutationKind, command.CanonicalFingerprint, false,
                        Record(command.Journal), command.AcquisitionLot is { } lot ? [lot] : [], [], command.RetainedEvidence);
                    _receipts.Add(command.MutationBatchId, result);
                }
                return result;
            });
            Journals.GetAtomicTaxLotPostingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => _receipts.GetValueOrDefault(call.Arg<Guid>()));
            _target.PostAsync(Arg.Any<LedgerJournalEntryWrite>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (FeeFailure)
                    throw new IOException("synthetic interrupted fee append");
                var write = call.Arg<LedgerJournalEntryWrite>();
                _journals[write.Entry.JournalEntryId] = Record(write);
                return new GovernedLedgerPostingResult(write.Entry.JournalEntryId, true);
            });
            Journals.GetByAggregateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => _journals.Values.ToArray());
            Service = CreateService();
        }
        internal AutomatedTradeFillAccountingService CreateService() => new(_options, _root, Journals, _atomic, _target,
            _accounts, Policies, _securities, _positions);
        internal TradeExecutedEvent Fill() => new(Guid.NewGuid(), "order-1", "TEST", OrderSide.Buy, 10m, 100m, 0m, 0m,
            0m, Time, Grant.FinancialAccountId)
        { SourceMode = "Synthetic", SettlementCurrency = "USD" };
        private LedgerJournalEntryRecord Record(LedgerJournalEntryWrite write)
        {
            write = AccountingPostingCommandValidator.NormalizeAndValidate(write);
            return new(write.Entry, write.AggregateId, write.PeriodId, write.CommandId, write.CorrelationId,
                1, Time, write.AccountingBasis, write.AccountingPolicyId, write.AccountingPolicyVersion,
                write.RuleId, write.RuleVersion, write.SourceEventId);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        private sealed class NoopLease : IAsyncDisposable { public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
}
