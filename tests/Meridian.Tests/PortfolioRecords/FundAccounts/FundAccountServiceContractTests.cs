using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.PortfolioRecords.FundAccounts;

namespace Meridian.Tests.PortfolioRecords.FundAccounts;

/// <summary>
/// Behavioral contract shared by <see cref="InMemoryFundAccountService"/> and
/// PostgresFundAccountService. The failure mode under guard: the in-memory and SQL
/// fund-account persistence paths drifting apart on money round-trips, ingestion
/// idempotency, account status policy, reconciliation, and readiness semantics — the
/// in-memory twin is heavily tested while production runs on SQL, so any divergence
/// ships silently. Test bodies live here as *_Core methods; each implementation
/// subclass exposes them through one-line facts so the Postgres lane keeps its
/// discovery-time Docker skip.
/// </summary>
public abstract class FundAccountServiceContractTests
{
    protected abstract IFundAccountService CreateService();

    // Postgres timestamptz stores microseconds and normalizes offsets, so
    // round-tripped timestamps are compared with a small tolerance.
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMilliseconds(2);

    // ── Account CRUD ──────────────────────────────────────────────────────────

    protected async Task CreateAccountAsync_NewAccount_ReturnsActiveSummaryAndIsReadable_Core()
    {
        var service = CreateService();
        var details = new CustodianAccountDetailsDto(
            SubAccountNumber: "SUB-001", DtcParticipantCode: "0352", CrestMemberCode: null,
            EuroclearAccountNumber: null, ClearstreamAccountNumber: null,
            PrimebrokerGiveupCode: null, SafekeepingLocation: "DTC", ServiceAgreementReference: "AGR-2026");
        var request = MakeCustodyRequest(institution: "JPM", ledgerReference: "LED-1", details: details);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var created = await service.CreateAccountAsync(request, cts.Token);

        created.IsActive.Should().BeTrue();
        created.EffectiveTo.Should().BeNull();
        created.AccountType.Should().Be(AccountTypeDto.Custody);
        created.AccountCode.Should().Be(request.AccountCode);
        created.DisplayName.Should().Be(request.DisplayName);
        created.BaseCurrency.Should().Be("USD");
        created.Institution.Should().Be("JPM");
        created.LedgerReference.Should().Be("LED-1");
        created.CustodianDetails.Should().BeEquivalentTo(details);

        var fetched = await service.GetAccountAsync(request.AccountId, cts.Token);
        fetched.Should().NotBeNull();
        fetched!.AccountId.Should().Be(request.AccountId);
        fetched.AccountCode.Should().Be(request.AccountCode);
        fetched.CustodianDetails.Should().BeEquivalentTo(details);
        fetched.EffectiveFrom.Should().BeCloseTo(request.EffectiveFrom, TimestampTolerance);
    }

    protected async Task CreateAccountAsync_DuplicateAccountId_ThrowsInvalidOperationException_Core()
    {
        var service = CreateService();
        var request = MakeCustodyRequest();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.CreateAccountAsync(request, cts.Token);

        // Different code (the active-code unique index would also fire), same id.
        var duplicate = request with { AccountCode = $"CUST-{Guid.NewGuid():N}" };
        var act = () => service.CreateAccountAsync(duplicate, cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    protected async Task CreateAccountAsync_NullRequest_ThrowsArgumentNullException_Core()
    {
        var service = CreateService();

        var act = () => service.CreateAccountAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    protected async Task Getters_UnknownAccount_ReturnNullOrEmpty_Core()
    {
        var service = CreateService();
        var unknown = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        (await service.GetAccountAsync(unknown, cts.Token)).Should().BeNull();
        (await service.GetLatestBalanceSnapshotAsync(unknown, cts.Token)).Should().BeNull();
        (await service.GetLatestSyncHistoryAsync(unknown, ct: cts.Token)).Should().BeNull();
        (await service.GetLatestMarginSnapshotAsync(unknown, cts.Token)).Should().BeNull();
        (await service.GetReadinessAsync(unknown, cts.Token)).Should().BeNull();
        (await service.GetBalanceHistoryAsync(unknown, ct: cts.Token)).Should().BeEmpty();
        (await service.GetCustodianPositionsAsync(unknown, Today(), cts.Token)).Should().BeEmpty();
        (await service.GetBankStatementLinesAsync(unknown, ct: cts.Token)).Should().BeEmpty();
        (await service.GetReconciliationRunsAsync(unknown, cts.Token)).Should().BeEmpty();
        (await service.GetReconciliationResultsAsync(Guid.NewGuid(), cts.Token)).Should().BeEmpty();
        (await service.GetSyncHistoryAsync(unknown, ct: cts.Token)).Should().BeEmpty();
        (await service.GetMarginSnapshotsAsync(unknown, cts.Token)).Should().BeEmpty();
        (await service.GetOpenBreaksAsync(unknown, cts.Token)).Should().BeEmpty();
        (await service.GetOpenPositionBreaksAsync(unknown, cts.Token)).Should().BeEmpty();
        (await service.GetOpenCashBreaksAsync(unknown, cts.Token)).Should().BeEmpty();
    }

    protected async Task QueryAccountsAsync_ByFundIdActiveOnly_ExcludesDeactivatedAndOtherFunds_Core()
    {
        var service = CreateService();
        var fundId = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var keep = await service.CreateAccountAsync(MakeCustodyRequest(fundId: fundId), cts.Token);
        var deactivated = await service.CreateAccountAsync(MakeCustodyRequest(fundId: fundId), cts.Token);
        await service.CreateAccountAsync(MakeCustodyRequest(fundId: Guid.NewGuid()), cts.Token);
        await service.DeactivateAccountAsync(deactivated.AccountId, "ops", cts.Token);

        var results = await service.QueryAccountsAsync(
            new AccountStructureQuery(FundId: fundId, ActiveOnly: true), cts.Token);

        // The Postgres query has no ORDER BY — always assert as a set.
        results.Select(a => a.AccountId).Should().BeEquivalentTo(new[] { keep.AccountId });
    }

    protected async Task QueryAccountsAsync_ByStrategyIdAndRunId_ReturnsExactMatches_Core()
    {
        var service = CreateService();
        var strategyId = $"strat-{Guid.NewGuid():N}";
        var runId = $"run-{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var match = await service.CreateAccountAsync(
            MakeCustodyRequest() with { StrategyId = strategyId, RunId = runId }, cts.Token);
        await service.CreateAccountAsync(
            MakeCustodyRequest() with { StrategyId = $"other-{Guid.NewGuid():N}" }, cts.Token);

        var byStrategy = await service.QueryAccountsAsync(new AccountStructureQuery(StrategyId: strategyId), cts.Token);
        var byRun = await service.QueryAccountsAsync(new AccountStructureQuery(RunId: runId), cts.Token);

        byStrategy.Select(a => a.AccountId).Should().BeEquivalentTo(new[] { match.AccountId });
        byRun.Select(a => a.AccountId).Should().BeEquivalentTo(new[] { match.AccountId });
    }

    protected async Task UpdateCustodianDetailsAsync_ActiveAccount_ReplacesAndPersistsDetails_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        var newDetails = new CustodianAccountDetailsDto(
            SubAccountNumber: "SUB-NEW", DtcParticipantCode: "0999", CrestMemberCode: null,
            EuroclearAccountNumber: null, ClearstreamAccountNumber: null,
            PrimebrokerGiveupCode: null, SafekeepingLocation: null, ServiceAgreementReference: null);

        var updated = await service.UpdateCustodianDetailsAsync(
            account.AccountId, new UpdateCustodianAccountDetailsRequest(newDetails, "ops"), cts.Token);

        updated.Should().NotBeNull();
        updated!.CustodianDetails.Should().BeEquivalentTo(newDetails);
        var fetched = await service.GetAccountAsync(account.AccountId, cts.Token);
        fetched!.CustodianDetails.Should().BeEquivalentTo(newDetails,
            "details are replaced wholesale and must persist through the JSONB path");
    }

    protected async Task UpdateCustodianDetailsAsync_UnknownAccount_ReturnsNull_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await service.UpdateCustodianDetailsAsync(
            Guid.NewGuid(),
            new UpdateCustodianAccountDetailsRequest(EmptyCustodianDetails(), "ops"),
            cts.Token);

        result.Should().BeNull();
    }

    protected async Task UpdateCustodianDetailsAsync_ClosedAccount_ThrowsAccountStatusPolicyException_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(
            MakeCustodyRequest() with { OperationalStatus = AccountOperationalStatusDto.Closed }, cts.Token);

        var act = () => service.UpdateCustodianDetailsAsync(
            account.AccountId, new UpdateCustodianAccountDetailsRequest(EmptyCustodianDetails(), "ops"), cts.Token);

        var exception = (await act.Should().ThrowAsync<AccountStatusPolicyException>()).Which;
        exception.Status.Should().Be(AccountOperationalStatusDto.Closed);
        exception.Operation.Should().Be("update-custodian-details");
        exception.BackfillAttempted.Should().BeFalse();
    }

    protected async Task UpdateBankDetailsAsync_SuspendedAccount_ThrowsAccountStatusPolicyException_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(
            MakeBankRequest() with { OperationalStatus = AccountOperationalStatusDto.Suspended }, cts.Token);

        var act = () => service.UpdateBankDetailsAsync(
            account.AccountId, new UpdateBankAccountDetailsRequest(EmptyBankDetails(), "ops"), cts.Token);

        (await act.Should().ThrowAsync<AccountStatusPolicyException>())
            .Which.Status.Should().Be(AccountOperationalStatusDto.Suspended);
    }

    protected async Task UpdateBankDetailsAsync_ActiveBankAccount_ReplacesAndPersistsDetails_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeBankRequest(), cts.Token);
        var newDetails = EmptyBankDetails() with
        {
            AccountNumber = "12345678",
            Iban = "GB29NWBK60161331926819",
            BicSwift = "NWBKGB2L"
        };

        var updated = await service.UpdateBankDetailsAsync(
            account.AccountId, new UpdateBankAccountDetailsRequest(newDetails, "ops"), cts.Token);

        updated!.BankDetails.Should().BeEquivalentTo(newDetails);
        (await service.GetAccountAsync(account.AccountId, cts.Token))!
            .BankDetails.Should().BeEquivalentTo(newDetails);
    }

    protected async Task DeactivateAccountAsync_ActiveAccount_SetsInactiveWithEffectiveTo_Core()
    {
        var service = CreateService();
        var fundId = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(fundId: fundId), cts.Token);

        var deactivated = await service.DeactivateAccountAsync(account.AccountId, "ops", cts.Token);

        deactivated!.IsActive.Should().BeFalse();
        deactivated.EffectiveTo.Should().NotBeNull();
        (await service.QueryAccountsAsync(new AccountStructureQuery(FundId: fundId, ActiveOnly: true), cts.Token))
            .Should().BeEmpty();
        var fundAccounts = await service.GetFundAccountsAsync(fundId, cts.Token);
        fundAccounts.CustodianAccounts.Should().BeEmpty(
            "a deactivated account must vanish from the fund's operational views");
    }

    protected async Task GetFundAccountsAsync_MixedAccountTypes_GroupsByTypeAndExcludesInactive_Core()
    {
        var service = CreateService();
        var fundId = Guid.NewGuid();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await service.CreateAccountAsync(MakeCustodyRequest(fundId: fundId), cts.Token);
        await service.CreateAccountAsync(MakeCustodyRequest(fundId: fundId), cts.Token);
        await service.CreateAccountAsync(MakeBankRequest(fundId: fundId), cts.Token);
        await service.CreateAccountAsync(MakeRequest(AccountTypeDto.Brokerage, fundId: fundId), cts.Token);
        await service.CreateAccountAsync(MakeRequest(AccountTypeDto.Margin, fundId: fundId), cts.Token);
        var inactive = await service.CreateAccountAsync(MakeCustodyRequest(fundId: fundId), cts.Token);
        await service.DeactivateAccountAsync(inactive.AccountId, "ops", cts.Token);
        await service.CreateAccountAsync(MakeCustodyRequest(fundId: Guid.NewGuid()), cts.Token);

        var result = await service.GetFundAccountsAsync(fundId, cts.Token);

        result.CustodianAccounts.Should().HaveCount(2);
        result.BankAccounts.Should().HaveCount(1);
        result.BrokerageAccounts.Should().HaveCount(1);
        result.OtherAccounts.Should().HaveCount(1, "Margin accounts land in the Other bucket");
    }

    // ── Balance snapshots ─────────────────────────────────────────────────────

    protected async Task RecordBalanceSnapshotAsync_ActiveAccount_PersistsMoneyFieldsExactly_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        var request = new RecordAccountBalanceSnapshotRequest(
            AccountId: account.AccountId,
            AsOfDate: Today(),
            Currency: "USD",
            CashBalance: 1234567.123456m,
            Source: "manual",
            SecuritiesMarketValue: 9876543.654321m,
            AccruedInterest: 152.375m,
            PendingSettlement: -25000.5m,
            UnrealizedPnl: 4321.0125m,
            RealizedPnl: -1234.56m);

        var recorded = await service.RecordBalanceSnapshotAsync(request, cts.Token);
        var history = await service.GetBalanceHistoryAsync(account.AccountId, ct: cts.Token);

        history.Should().ContainSingle();
        var snapshot = history[0];
        snapshot.SnapshotId.Should().Be(recorded.SnapshotId);
        snapshot.CashBalance.Should().Be(1234567.123456m, "money must round-trip numeric(24,6) exactly");
        snapshot.SecuritiesMarketValue.Should().Be(9876543.654321m);
        snapshot.AccruedInterest.Should().Be(152.375m);
        snapshot.PendingSettlement.Should().Be(-25000.5m);
        snapshot.UnrealizedPnl.Should().Be(4321.0125m);
        snapshot.RealizedPnl.Should().Be(-1234.56m);
    }

    protected async Task RecordBalanceSnapshotAsync_ClosedAccount_ThrowsAccountStatusPolicyException_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(
            MakeCustodyRequest() with { OperationalStatus = AccountOperationalStatusDto.Closed }, cts.Token);

        var act = () => service.RecordBalanceSnapshotAsync(
            MakeSnapshotRequest(account.AccountId, Today(), 100m), cts.Token);

        (await act.Should().ThrowAsync<AccountStatusPolicyException>())
            .Which.Status.Should().Be(AccountOperationalStatusDto.Closed);
    }

    protected async Task GetBalanceHistoryAsync_FromToRange_FiltersInclusivelyNewestFirst_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        var day0 = Today().AddDays(-2);
        var day1 = day0.AddDays(1);
        var day2 = day0.AddDays(2);
        await service.RecordBalanceSnapshotAsync(MakeSnapshotRequest(account.AccountId, day0, 100m), cts.Token);
        await service.RecordBalanceSnapshotAsync(MakeSnapshotRequest(account.AccountId, day1, 200m), cts.Token);
        await service.RecordBalanceSnapshotAsync(MakeSnapshotRequest(account.AccountId, day2, 300m), cts.Token);

        var ranged = await service.GetBalanceHistoryAsync(account.AccountId, day0, day1, cts.Token);
        var fromOnly = await service.GetBalanceHistoryAsync(account.AccountId, fromDate: day1, ct: cts.Token);
        var toOnly = await service.GetBalanceHistoryAsync(account.AccountId, toDate: day1, ct: cts.Token);

        ranged.Select(s => s.AsOfDate).Should().Equal(new[] { day1, day0 },
            "boundaries are inclusive, newest first");
        fromOnly.Select(s => s.AsOfDate).Should().Equal(day2, day1);
        toOnly.Select(s => s.AsOfDate).Should().Equal(day1, day0);
    }

    protected async Task GetLatestBalanceSnapshotAsync_MultipleDates_ReturnsLatestAsOfDate_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        await service.RecordBalanceSnapshotAsync(MakeSnapshotRequest(account.AccountId, Today().AddDays(-1), 100m), cts.Token);
        await service.RecordBalanceSnapshotAsync(MakeSnapshotRequest(account.AccountId, Today(), 200m), cts.Token);

        var latest = await service.GetLatestBalanceSnapshotAsync(account.AccountId, cts.Token);

        latest!.AsOfDate.Should().Be(Today());
        latest.CashBalance.Should().Be(200m);
    }

    // ── Statement ingestion ───────────────────────────────────────────────────

    protected async Task IngestCustodianStatementAsync_ActiveAccount_RoundTripsPositionLines_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        var asOf = Today();
        var request = MakeCustodianStatementRequest(account.AccountId, asOf, lineCount: 1);
        var line = request.Lines[0] with
        {
            Quantity = 1500.12345678m,
            MarketValue = 765432.123456m,
            SecurityName = "Apple Inc",
            AssetClass = "Equity",
            IsShort = true
        };
        request = request with { Lines = new[] { line } };

        var batch = await service.IngestCustodianStatementAsync(request, cts.Token);
        var positions = await service.GetCustodianPositionsAsync(account.AccountId, asOf, cts.Token);
        var otherDate = await service.GetCustodianPositionsAsync(account.AccountId, asOf.AddDays(-1), cts.Token);

        batch.BatchId.Should().Be(request.BatchId);
        batch.LineCount.Should().Be(1);
        batch.LoadedBy.Should().Be(request.LoadedBy);
        positions.Should().ContainSingle();
        positions[0].LineId.Should().Be(line.LineId);
        positions[0].Identifier.Should().Be(line.Identifier);
        positions[0].IdentifierType.Should().Be(line.IdentifierType);
        positions[0].Quantity.Should().Be(1500.12345678m, "quantities must round-trip numeric(24,8) exactly");
        positions[0].MarketValue.Should().Be(765432.123456m);
        positions[0].Currency.Should().Be(line.Currency);
        positions[0].SecurityName.Should().Be("Apple Inc", "descriptive fields ride the raw-payload JSONB on the SQL path");
        positions[0].AssetClass.Should().Be("Equity");
        positions[0].IsShort.Should().BeTrue();
        otherDate.Should().BeEmpty("the as-of date filter is exact");
    }

    protected async Task IngestCustodianStatementAsync_ReIngestingSameLines_DoesNotDuplicatePositions_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        var asOf = Today();
        // Identical BatchId AND LineIds: both the in-memory content-key dedup and the
        // Postgres position_line_id primary key must agree on the replay.
        var request = MakeCustodianStatementRequest(account.AccountId, asOf, lineCount: 2);

        await service.IngestCustodianStatementAsync(request, cts.Token);
        await service.IngestCustodianStatementAsync(request, cts.Token);

        (await service.GetCustodianPositionsAsync(account.AccountId, asOf, cts.Token))
            .Should().HaveCount(2, "a replayed statement must not duplicate positions");
    }

    protected async Task IngestCustodianStatementAsync_SuspendedAccount_IsAllowed_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(
            MakeCustodyRequest() with { OperationalStatus = AccountOperationalStatusDto.Suspended }, cts.Token);
        var asOf = Today();

        await service.IngestCustodianStatementAsync(
            MakeCustodianStatementRequest(account.AccountId, asOf, lineCount: 1), cts.Token);

        (await service.GetCustodianPositionsAsync(account.AccountId, asOf, cts.Token))
            .Should().ContainSingle("statement ingestion stays open for suspended accounts by design");
    }

    protected async Task IngestCustodianStatementAsync_ClosedAccount_ThrowsAccountStatusPolicyException_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(
            MakeCustodyRequest() with { OperationalStatus = AccountOperationalStatusDto.Closed }, cts.Token);

        var act = () => service.IngestCustodianStatementAsync(
            MakeCustodianStatementRequest(account.AccountId, Today(), lineCount: 1), cts.Token);

        (await act.Should().ThrowAsync<AccountStatusPolicyException>())
            .Which.Status.Should().Be(AccountOperationalStatusDto.Closed);
    }

    protected async Task IngestBankStatementAsync_ActiveAccount_RoundTripsStatementLines_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeBankRequest(), cts.Token);
        var statementDate = Today();
        var batchId = Guid.NewGuid();
        var populated = MakeBankLine(batchId, account.AccountId, statementDate, amount: -1500.25m) with
        {
            Reference = "WIRE-42",
            ClosingBalance = 98500.75m
        };
        var nullable = MakeBankLine(batchId, account.AccountId, statementDate, amount: 250m) with
        {
            Reference = null,
            ClosingBalance = null
        };

        var batch = await service.IngestBankStatementAsync(new IngestBankStatementRequest(
            batchId, account.AccountId, statementDate, "JPM", Notes: null,
            Lines: new[] { populated, nullable }, LoadedBy: "ops"), cts.Token);
        var lines = await service.GetBankStatementLinesAsync(account.AccountId, ct: cts.Token);

        batch.LineCount.Should().Be(2);
        lines.Should().HaveCount(2);
        var readPopulated = lines.Single(l => l.LineId == populated.LineId);
        readPopulated.Amount.Should().Be(-1500.25m);
        readPopulated.TransactionDate.Should().Be(populated.TransactionDate);
        readPopulated.ValueDate.Should().Be(populated.ValueDate);
        readPopulated.TransactionType.Should().Be(populated.TransactionType);
        readPopulated.Description.Should().Be(populated.Description);
        readPopulated.Reference.Should().Be("WIRE-42");
        readPopulated.ClosingBalance.Should().Be(98500.75m);
        var readNullable = lines.Single(l => l.LineId == nullable.LineId);
        readNullable.Reference.Should().BeNull("null optionals must round-trip as nulls, not empty strings");
        readNullable.ClosingBalance.Should().BeNull();
    }

    protected async Task GetBankStatementLinesAsync_FromToRange_FiltersByTransactionDate_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeBankRequest(), cts.Token);
        var day0 = Today().AddDays(-2);
        var batchId = Guid.NewGuid();
        var lines = new[]
        {
            MakeBankLine(batchId, account.AccountId, day0, amount: 1m),
            MakeBankLine(batchId, account.AccountId, day0.AddDays(1), amount: 2m),
            MakeBankLine(batchId, account.AccountId, day0.AddDays(2), amount: 3m)
        };
        await service.IngestBankStatementAsync(new IngestBankStatementRequest(
            batchId, account.AccountId, day0.AddDays(2), "JPM", Notes: null, Lines: lines, LoadedBy: "ops"), cts.Token);

        var ranged = await service.GetBankStatementLinesAsync(account.AccountId, day0, day0.AddDays(1), cts.Token);

        ranged.Select(l => l.TransactionDate).Should().Equal(new[] { day0.AddDays(1), day0 },
            "the range is inclusive on TransactionDate, newest first");
    }

    // ── Reconciliation ────────────────────────────────────────────────────────

    protected async Task ReconcileAccountAsync_SnapshotAndPositions_ProducesMatchedRun_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        var asOf = Today();
        await service.RecordBalanceSnapshotAsync(MakeSnapshotRequest(account.AccountId, asOf, 50000m), cts.Token);
        await service.IngestCustodianStatementAsync(
            MakeCustodianStatementRequest(account.AccountId, asOf, lineCount: 1), cts.Token);

        var run = await service.ReconcileAccountAsync(
            new ReconcileAccountRequest(account.AccountId, asOf, "ops"), cts.Token);

        run.Status.Should().Be("Matched");
        run.TotalChecks.Should().Be(2);
        run.TotalMatched.Should().Be(2);
        run.TotalBreaks.Should().Be(0);
        run.BreakAmountTotal.Should().Be(0m);
        (await service.GetReconciliationRunsAsync(account.AccountId, cts.Token))
            .Should().Contain(r => r.ReconciliationRunId == run.ReconciliationRunId);
        var results = await service.GetReconciliationResultsAsync(run.ReconciliationRunId, cts.Token);
        results.Should().HaveCount(2).And.OnlyContain(r => r.IsMatch);
        results.Select(r => r.Category).Should().BeEquivalentTo(new[] { "Cash", "Positions" });
    }

    protected async Task ReconcileAccountAsync_DivergentBrokerageSyncCash_ProducesContinuityBreak_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        var asOf = Today();
        await service.RecordBalanceSnapshotAsync(
            MakeSnapshotRequest(account.AccountId, asOf, 1000.00m) with { Source = "brokerage-sync:test" }, cts.Token);
        await service.RecordBalanceSnapshotAsync(
            MakeSnapshotRequest(account.AccountId, asOf, 900.00m) with { Source = "manual" }, cts.Token);

        var run = await service.ReconcileAccountAsync(
            new ReconcileAccountRequest(account.AccountId, asOf, "ops"), cts.Token);

        run.Status.Should().Be("Breaks");
        run.TotalBreaks.Should().Be(1);
        run.BreakAmountTotal.Should().Be(100.00m,
            "a $100 divergence between sync-derived and run-derived cash is a money-integrity break");
        var results = await service.GetReconciliationResultsAsync(run.ReconciliationRunId, cts.Token);
        var continuity = results.Single(r => r.CheckLabel == "RunVsAccountSyncCashContinuity");
        continuity.IsMatch.Should().BeFalse();
        continuity.Category.Should().Be("Continuity");
        continuity.ExpectedAmount.Should().Be(900.00m);
        continuity.ActualAmount.Should().Be(1000.00m);
        continuity.Variance.Should().Be(100.00m);
    }

    protected async Task GetOpenBreaksAsync_ContinuityOnlyBreak_ReturnsNoCashOrPositionBreaks_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        var asOf = Today();
        await service.RecordBalanceSnapshotAsync(
            MakeSnapshotRequest(account.AccountId, asOf, 1000m) with { Source = "brokerage-sync:test" }, cts.Token);
        await service.RecordBalanceSnapshotAsync(
            MakeSnapshotRequest(account.AccountId, asOf, 900m) with { Source = "manual" }, cts.Token);
        await service.ReconcileAccountAsync(new ReconcileAccountRequest(account.AccountId, asOf, "ops"), cts.Token);

        (await service.GetOpenBreaksAsync(account.AccountId, cts.Token)).Should().BeEmpty(
            "break views filter to Cash/Position categories, so continuity breaks stay off them");
        (await service.GetOpenCashBreaksAsync(account.AccountId, cts.Token)).Should().BeEmpty();
        (await service.GetOpenPositionBreaksAsync(account.AccountId, cts.Token)).Should().BeEmpty();
    }

    // ── Sync history ──────────────────────────────────────────────────────────

    protected async Task RecordSyncHistoryAsync_BlankCapability_ThrowsArgumentException_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);

        var act = () => service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, Capability: "   ", Status: AccountSyncStatusDto.Succeeded), cts.Token);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    protected async Task RecordSyncHistoryAsync_FailedWithoutFailureKind_NormalizesToUnknown_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);

        var entry = await service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, "balances", AccountSyncStatusDto.Failed,
            FailureKind: AccountSyncFailureKindDto.None), cts.Token);

        entry.FailureKind.Should().Be(AccountSyncFailureKindDto.Unknown,
            "a failure without a classified kind must never look like a clean state");
    }

    protected async Task RecordSyncHistoryAsync_SucceededWithFailureKind_ForcesFailureKindNone_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);

        var entry = await service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, "balances", AccountSyncStatusDto.Succeeded,
            FailureKind: AccountSyncFailureKindDto.RateLimited), cts.Token);

        entry.FailureKind.Should().Be(AccountSyncFailureKindDto.None);
    }

    protected async Task RecordSyncHistoryAsync_MessyWarnings_TrimsAndDeduplicates_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);

        var entry = await service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, "balances", AccountSyncStatusDto.Succeeded,
            SecurityMissingCount: -5,
            Warnings: new[] { " stale price ", "stale price", "", "  ", "MISSING FX" }), cts.Token);

        entry.Warnings.Should().BeEquivalentTo("stale price", "MISSING FX");
        entry.SecurityMissingCount.Should().Be(0, "negative missing counts clamp to zero");
    }

    protected async Task RecordSyncHistoryAsync_SameCapabilityAndCorrelation_ReusesEntryInsteadOfAppending_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        var correlationId = $"corr-{Guid.NewGuid():N}";
        var freshUntil = DateTimeOffset.UtcNow.AddHours(1);

        var pending = await service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, "balances", AccountSyncStatusDto.Pending,
            AttemptedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            CorrelationId: correlationId), cts.Token);
        var completed = await service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, "balances", AccountSyncStatusDto.Succeeded,
            AttemptedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow,
            FreshUntil: freshUntil,
            CorrelationId: correlationId), cts.Token);

        completed.SyncHistoryId.Should().Be(pending.SyncHistoryId,
            "the same correlation must update the attempt, not append a second one");
        var history = await service.GetSyncHistoryAsync(account.AccountId, ct: cts.Token);
        history.Should().ContainSingle();
        history[0].Status.Should().Be(AccountSyncStatusDto.Succeeded);
        history[0].FreshUntil.Should().BeCloseTo(freshUntil, TimestampTolerance);
    }

    protected async Task GetSyncHistoryAsync_CapabilityFilter_MatchesCaseInsensitively_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        await service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, "Balances", AccountSyncStatusDto.Succeeded), cts.Token);

        (await service.GetSyncHistoryAsync(account.AccountId, "balances", cts.Token)).Should().ContainSingle();
        (await service.GetSyncHistoryAsync(account.AccountId, "positions", cts.Token)).Should().BeEmpty();
        (await service.GetSyncHistoryAsync(account.AccountId, ct: cts.Token)).Should().ContainSingle();
    }

    protected async Task GetLatestSyncHistoryAsync_MultipleAttempts_ReturnsMostRecent_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), cts.Token);
        await service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, "balances", AccountSyncStatusDto.Failed,
            FailureKind: AccountSyncFailureKindDto.Timeout,
            AttemptedAt: DateTimeOffset.UtcNow.AddHours(-1),
            CorrelationId: $"corr-{Guid.NewGuid():N}"), cts.Token);
        await service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, "balances", AccountSyncStatusDto.Succeeded,
            AttemptedAt: DateTimeOffset.UtcNow,
            CorrelationId: $"corr-{Guid.NewGuid():N}"), cts.Token);

        var latest = await service.GetLatestSyncHistoryAsync(account.AccountId, ct: cts.Token);

        latest!.Status.Should().Be(AccountSyncStatusDto.Succeeded, "attempts order newest first");
    }

    // ── Readiness ─────────────────────────────────────────────────────────────

    protected async Task GetReadinessAsync_HealthyLinkedAccount_IsReadyWithNoIssues_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(
            MakeCustodyRequest(institution: "JPM", ledgerReference: "LED-1"), cts.Token);
        await service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, "balances", AccountSyncStatusDto.Succeeded,
            ProviderLinkStatus: AccountProviderLinkStatusDto.Linked,
            CompletedAt: DateTimeOffset.UtcNow,
            FreshUntil: DateTimeOffset.UtcNow.AddHours(1)), cts.Token);

        var readiness = await service.GetReadinessAsync(account.AccountId, cts.Token);

        readiness.Should().NotBeNull();
        readiness!.IsReady.Should().BeTrue();
        readiness.Issues.Should().BeEmpty();
        readiness.LatestSyncStatus.Should().Be(AccountSyncStatusDto.Succeeded);
    }

    protected async Task GetReadinessAsync_NoSyncNoLedgerReference_FlagsNeverRunAndLedgerMapping_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(
            MakeCustodyRequest(institution: "JPM"), cts.Token);

        var readiness = await service.GetReadinessAsync(account.AccountId, cts.Token);

        readiness!.IsReady.Should().BeFalse();
        readiness.Issues.Should().Contain(i =>
            i.Code == "account.sync.never_run" && i.Severity == AccountReadinessSeverityDto.Warning);
        readiness.Issues.Should().Contain(i =>
            i.Code == "account.ledger_mapping.missing" && i.Severity == AccountReadinessSeverityDto.Critical);
    }

    protected async Task GetReadinessAsync_MarginAccountWithoutSnapshot_FlagsCriticalMarginIssue_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(
            MakeRequest(AccountTypeDto.Margin, institution: "IB", ledgerReference: "LED-M"), cts.Token);

        var readiness = await service.GetReadinessAsync(account.AccountId, cts.Token);

        readiness!.Issues.Should().Contain(i =>
            i.Code == "account.margin.snapshot.missing" && i.Severity == AccountReadinessSeverityDto.Critical,
            "a margin account without a margin snapshot must never look production-ready");
    }

    // ── Margin snapshots ──────────────────────────────────────────────────────

    protected async Task RecordMarginSnapshotAsync_LowercaseCurrency_NormalizesToUppercase_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeRequest(AccountTypeDto.Margin), cts.Token);

        await service.RecordMarginSnapshotAsync(
            MakeMarginRequest(account.AccountId) with { Currency = "usd" }, cts.Token);

        (await service.GetMarginSnapshotsAsync(account.AccountId, cts.Token))
            .Should().ContainSingle().Which.Currency.Should().Be("USD");
    }

    protected async Task RecordMarginSnapshotAsync_BlankCurrency_ThrowsArgumentException_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeRequest(AccountTypeDto.Margin), cts.Token);

        var act = () => service.RecordMarginSnapshotAsync(
            MakeMarginRequest(account.AccountId) with { Currency = "  " }, cts.Token);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    protected async Task RecordMarginSnapshotAsync_SameCorrelationSameEffectiveAt_ReplacesSnapshot_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeRequest(AccountTypeDto.Margin), cts.Token);
        // EffectiveAt must stay constant: the Postgres upsert key is (account_id, effective_at).
        var effectiveAt = DateTimeOffset.UtcNow;
        var correlationId = $"corr-{Guid.NewGuid():N}";

        var first = await service.RecordMarginSnapshotAsync(MakeMarginRequest(account.AccountId) with
        {
            EffectiveAt = effectiveAt,
            CorrelationId = correlationId,
            ExcessLiquidity = 10000m
        }, cts.Token);
        var second = await service.RecordMarginSnapshotAsync(MakeMarginRequest(account.AccountId) with
        {
            EffectiveAt = effectiveAt,
            CorrelationId = correlationId,
            ExcessLiquidity = 12500m
        }, cts.Token);

        second.MarginSnapshotId.Should().Be(first.MarginSnapshotId,
            "the same correlation must update the snapshot, not append a duplicate");
        var snapshots = await service.GetMarginSnapshotsAsync(account.AccountId, cts.Token);
        snapshots.Should().ContainSingle().Which.ExcessLiquidity.Should().Be(12500m);
    }

    protected async Task GetLatestMarginSnapshotAsync_MultipleEffectiveTimes_ReturnsLatest_Core()
    {
        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeRequest(AccountTypeDto.Margin), cts.Token);
        var older = DateTimeOffset.UtcNow.AddHours(-1);
        var newer = DateTimeOffset.UtcNow;
        await service.RecordMarginSnapshotAsync(MakeMarginRequest(account.AccountId) with
        {
            EffectiveAt = older,
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            ExcessLiquidity = 1m
        }, cts.Token);
        await service.RecordMarginSnapshotAsync(MakeMarginRequest(account.AccountId) with
        {
            EffectiveAt = newer,
            CorrelationId = $"corr-{Guid.NewGuid():N}",
            ExcessLiquidity = 2m
        }, cts.Token);

        var latest = await service.GetLatestMarginSnapshotAsync(account.AccountId, cts.Token);

        latest!.ExcessLiquidity.Should().Be(2m);
        latest.EffectiveAt.Should().BeCloseTo(newer, TimestampTolerance);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    protected async Task RecordSyncHistoryAsync_PreCancelledToken_ThrowsOperationCanceled_Core()
    {
        var service = CreateService();
        using var setupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeCustodyRequest(), setupCts.Token);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cancelled.Cancel();

        var act = () => service.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            account.AccountId, "balances", AccountSyncStatusDto.Succeeded), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    protected async Task RecordMarginSnapshotAsync_PreCancelledToken_ThrowsOperationCanceled_Core()
    {
        var service = CreateService();
        using var setupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var account = await service.CreateAccountAsync(MakeRequest(AccountTypeDto.Margin), setupCts.Token);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cancelled.Cancel();

        var act = () => service.RecordMarginSnapshotAsync(MakeMarginRequest(account.AccountId), cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Request factories ─────────────────────────────────────────────────────

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    protected static CreateAccountRequest MakeCustodyRequest(
        Guid? fundId = null,
        string? institution = null,
        string? ledgerReference = null,
        CustodianAccountDetailsDto? details = null) =>
        MakeRequest(AccountTypeDto.Custody, fundId, institution, ledgerReference) with
        {
            CustodianDetails = details
        };

    protected static CreateAccountRequest MakeBankRequest(Guid? fundId = null) =>
        MakeRequest(AccountTypeDto.Bank, fundId);

    protected static CreateAccountRequest MakeRequest(
        AccountTypeDto accountType,
        Guid? fundId = null,
        string? institution = null,
        string? ledgerReference = null) =>
        new(
            AccountId: Guid.NewGuid(),
            AccountType: accountType,
            AccountCode: $"{accountType.ToString().ToUpperInvariant()}-{Guid.NewGuid():N}",
            DisplayName: $"{accountType} account",
            BaseCurrency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow,
            CreatedBy: "contract-tests",
            FundId: fundId,
            Institution: institution,
            LedgerReference: ledgerReference);

    private static RecordAccountBalanceSnapshotRequest MakeSnapshotRequest(
        Guid accountId, DateOnly asOfDate, decimal cashBalance) =>
        new(accountId, asOfDate, "USD", cashBalance, Source: "manual", RecordedBy: "contract-tests");

    private static IngestCustodianStatementRequest MakeCustodianStatementRequest(
        Guid accountId, DateOnly asOfDate, int lineCount)
    {
        var batchId = Guid.NewGuid();
        var lines = Enumerable.Range(0, lineCount)
            .Select(i => new CustodianPositionLineDto(
                LineId: Guid.NewGuid(),
                BatchId: batchId,
                AccountId: accountId,
                AsOfDate: asOfDate,
                Identifier: $"US03783310{i}",
                IdentifierType: "CUSIP",
                Quantity: 100m + i,
                MarketValue: 19_000.25m + i,
                Currency: "USD",
                SecurityName: $"Security {i}",
                AssetClass: "Equity",
                IsShort: false))
            .ToArray();
        return new IngestCustodianStatementRequest(
            batchId, accountId, asOfDate, CustodianName: "JPM", SourceFormat: "csv",
            Notes: null, Lines: lines, LoadedBy: "contract-tests");
    }

    private static BankStatementLineDto MakeBankLine(
        Guid batchId, Guid accountId, DateOnly transactionDate, decimal amount) =>
        new(
            LineId: Guid.NewGuid(),
            BatchId: batchId,
            AccountId: accountId,
            TransactionDate: transactionDate,
            ValueDate: transactionDate.AddDays(1),
            Amount: amount,
            Currency: "USD",
            TransactionType: amount < 0 ? "DEBIT" : "CREDIT",
            Description: "Wire transfer",
            Reference: null,
            ClosingBalance: null);

    private static RecordMarginSnapshotRequest MakeMarginRequest(Guid accountId) =>
        new(
            AccountId: accountId,
            EffectiveAt: DateTimeOffset.UtcNow,
            Currency: "USD",
            MarginType: MarginModelTypeDto.RegT);

    private static CustodianAccountDetailsDto EmptyCustodianDetails() =>
        new(null, null, null, null, null, null, null, null);

    private static BankAccountDetailsDto EmptyBankDetails() =>
        new(null, null, null, null, null, null, null, null, null, null, null);
}
