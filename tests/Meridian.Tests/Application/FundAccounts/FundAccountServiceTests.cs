using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;

namespace Meridian.Tests.Application.FundAccounts;

public sealed class FundAccountServiceTests
{
    private static InMemoryFundAccountService CreateService() => new();

    private static CreateAccountRequest MakeCustodyRequest(
        Guid? fundId = null,
        CustodianAccountDetailsDto? details = null) =>
        new(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Custody,
            AccountCode: $"CUST-{Guid.NewGuid():N}",
            DisplayName: "JPM Custody",
            BaseCurrency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow,
            CreatedBy: "test",
            FundId: fundId,
            CustodianDetails: details);

    private static CreateAccountRequest MakeBankRequest(
        Guid? fundId = null,
        BankAccountDetailsDto? details = null) =>
        new(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Bank,
            AccountCode: $"BANK-{Guid.NewGuid():N}",
            DisplayName: "JPM USD Cash",
            BaseCurrency: "USD",
            EffectiveFrom: DateTimeOffset.UtcNow,
            CreatedBy: "test",
            FundId: fundId,
            BankDetails: details);

    // ── CreateAccount ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAccount_WithCustodyType_StoresAndReturnsCustodianDetails()
    {
        var svc = CreateService();
        var details = new CustodianAccountDetailsDto(
            SubAccountNumber: "SUB-001",
            DtcParticipantCode: "0352",
            CrestMemberCode: null, EuroclearAccountNumber: null,
            ClearstreamAccountNumber: null, PrimebrokerGiveupCode: null,
            SafekeepingLocation: "DTC", ServiceAgreementReference: "AGR-2024");

        var result = await svc.CreateAccountAsync(MakeCustodyRequest(details: details));

        Assert.Equal(AccountTypeDto.Custody, result.AccountType);
        Assert.NotNull(result.CustodianDetails);
        Assert.Equal("SUB-001", result.CustodianDetails!.SubAccountNumber);
        Assert.Equal("0352", result.CustodianDetails.DtcParticipantCode);
        Assert.Equal("DTC", result.CustodianDetails.SafekeepingLocation);
    }

    [Fact]
    public async Task CreateAccount_WithBankType_StoresBankAccountDetails()
    {
        var svc = CreateService();
        var details = new BankAccountDetailsDto(
            AccountNumber: "00112233",
            BankName: "JPMorgan Chase",
            BranchName: null,
            Iban: "GB29NWBK60161331926819",
            BicSwift: "CHASUS33",
            RoutingNumber: "021000021",
            SortCode: null,
            IntermediaryBankBic: null, IntermediaryBankName: null,
            BeneficiaryName: null, BeneficiaryAddress: null);

        var result = await svc.CreateAccountAsync(MakeBankRequest(details: details));

        Assert.Equal(AccountTypeDto.Bank, result.AccountType);
        Assert.NotNull(result.BankDetails);
        Assert.Equal("00112233", result.BankDetails!.AccountNumber);
        Assert.Equal("CHASUS33", result.BankDetails.BicSwift);
        Assert.Equal("GB29NWBK60161331926819", result.BankDetails.Iban);
    }

    [Fact]
    public async Task CreateAccount_Duplicate_ThrowsInvalidOperation()
    {
        var svc = CreateService();
        var request = MakeCustodyRequest();
        await svc.CreateAccountAsync(request);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAccountAsync(request));
    }

    // ── GetFundAccounts ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetFundAccounts_WithMultipleAccounts_ReturnsSeparatedByType()
    {
        var svc = CreateService();
        var fundId = Guid.NewGuid();

        await svc.CreateAccountAsync(MakeCustodyRequest(fundId: fundId));
        await svc.CreateAccountAsync(MakeCustodyRequest(fundId: fundId));
        await svc.CreateAccountAsync(MakeBankRequest(fundId: fundId));
        await svc.CreateAccountAsync(MakeBankRequest(fundId: fundId));
        await svc.CreateAccountAsync(MakeBankRequest(fundId: fundId));
        // account for a different fund — should not appear
        await svc.CreateAccountAsync(MakeBankRequest(fundId: Guid.NewGuid()));

        var result = await svc.GetFundAccountsAsync(fundId);

        Assert.Equal(fundId, result.FundId);
        Assert.Equal(2, result.CustodianAccounts.Count);
        Assert.Equal(3, result.BankAccounts.Count);
        Assert.Empty(result.BrokerageAccounts);
    }

    // ── UpdateCustodianDetails ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCustodianDetails_ReplacesDetails()
    {
        var svc = CreateService();
        var req = MakeCustodyRequest();
        var acct = await svc.CreateAccountAsync(req);

        var newDetails = new CustodianAccountDetailsDto(
            SubAccountNumber: "SUB-999", DtcParticipantCode: "9999",
            CrestMemberCode: null, EuroclearAccountNumber: null,
            ClearstreamAccountNumber: null, PrimebrokerGiveupCode: null,
            SafekeepingLocation: "CREST", ServiceAgreementReference: null);

        var updated = await svc.UpdateCustodianDetailsAsync(
            acct.AccountId,
            new UpdateCustodianAccountDetailsRequest(newDetails, "test"));

        Assert.NotNull(updated);
        Assert.Equal("SUB-999", updated!.CustodianDetails!.SubAccountNumber);
        Assert.Equal("CREST", updated.CustodianDetails.SafekeepingLocation);
    }

    [Fact]
    public async Task UpdateCustodianDetails_UnknownAccount_ReturnsNull()
    {
        var svc = CreateService();
        var result = await svc.UpdateCustodianDetailsAsync(
            Guid.NewGuid(),
            new UpdateCustodianAccountDetailsRequest(
                new CustodianAccountDetailsDto(null, null, null, null, null, null, null, null),
                "test"));

        Assert.Null(result);
    }

    // ── Balance snapshots ─────────────────────────────────────────────────────

    [Fact]
    public async Task RecordBalanceSnapshot_StoresAndReturnsLatest()
    {
        var svc = CreateService();
        var acct = await svc.CreateAccountAsync(MakeBankRequest());
        var today = DateOnly.FromDateTime(DateTime.Today);

        await svc.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            acct.AccountId, today, "USD", 1_000_000m, "BankStatement", "test"));

        var latest = await svc.GetLatestBalanceSnapshotAsync(acct.AccountId);

        Assert.NotNull(latest);
        Assert.Equal(1_000_000m, latest!.CashBalance);
        Assert.Equal("USD", latest.Currency);
    }

    [Fact]
    public async Task GetBalanceHistory_FiltersByDateRange()
    {
        var svc = CreateService();
        var acct = await svc.CreateAccountAsync(MakeBankRequest());

        var d1 = new DateOnly(2025, 1, 1);
        var d2 = new DateOnly(2025, 2, 1);
        var d3 = new DateOnly(2025, 3, 1);

        foreach (var d in new[] { d1, d2, d3 })
            await svc.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
                acct.AccountId, d, "USD", 100m, "Manual", "test"));

        var results = await svc.GetBalanceHistoryAsync(acct.AccountId, fromDate: d2, toDate: d2);

        Assert.Single(results);
        Assert.Equal(d2, results[0].AsOfDate);
    }

    // ── Statement ingestion ───────────────────────────────────────────────────

    [Fact]
    public async Task IngestCustodianStatement_StoresPositionLines()
    {
        var svc = CreateService();
        var acct = await svc.CreateAccountAsync(MakeCustodyRequest());
        var today = DateOnly.FromDateTime(DateTime.Today);

        var lines = new List<CustodianPositionLineDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), acct.AccountId, today,
                "US0378331005", "ISIN", 100m, 17_000m, "USD", null, null, false),
            new(Guid.NewGuid(), Guid.NewGuid(), acct.AccountId, today,
                "US5949181045", "ISIN", 50m, 11_000m, "USD", null, null, false)
        };

        var batch = await svc.IngestCustodianStatementAsync(new IngestCustodianStatementRequest(
            Guid.NewGuid(), acct.AccountId, today, "JPMorgan", "JSON", null, lines, "loader"));

        Assert.Equal(2, batch.LineCount);

        var stored = await svc.GetCustodianPositionsAsync(acct.AccountId, today);
        Assert.Equal(2, stored.Count);
    }

    [Fact]
    public async Task IngestBankStatement_StoresBankLines()
    {
        var svc = CreateService();
        var acct = await svc.CreateAccountAsync(MakeBankRequest());
        var today = DateOnly.FromDateTime(DateTime.Today);

        var lines = new List<BankStatementLineDto>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), acct.AccountId, today, today,
                -50_000m, "USD", "Wire", "Payment to broker", null, 950_000m)
        };

        var batch = await svc.IngestBankStatementAsync(new IngestBankStatementRequest(
            Guid.NewGuid(), acct.AccountId, today, "JPMorgan", null, lines, "loader"));

        Assert.Equal(1, batch.LineCount);

        var stored = await svc.GetBankStatementLinesAsync(acct.AccountId);
        Assert.Single(stored);
        Assert.Equal(-50_000m, stored[0].Amount);
    }

    // ── Reconciliation ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReconcileAccount_WithBalanceSnapshot_ReturnsMatchedRun()
    {
        var svc = CreateService();
        var acct = await svc.CreateAccountAsync(MakeBankRequest());
        var today = DateOnly.FromDateTime(DateTime.Today);

        await svc.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            acct.AccountId, today, "USD", 500_000m, "BankStatement", "test"));

        var run = await svc.ReconcileAccountAsync(
            new ReconcileAccountRequest(acct.AccountId, today, "test-user"));

        Assert.NotNull(run);
        Assert.Equal("Matched", run.Status);
        Assert.Equal(0, run.TotalBreaks);
        Assert.True(run.TotalChecks > 0);
    }

    [Fact]
    public async Task ReconcileAccount_UnknownAccount_ThrowsInvalidOperation()
    {
        var svc = CreateService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ReconcileAccountAsync(
                new ReconcileAccountRequest(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), "test")));
    }

    [Fact]
    public async Task GetReconciliationRuns_ReturnsAllRunsForAccount()
    {
        var svc = CreateService();
        var acct = await svc.CreateAccountAsync(MakeBankRequest());
        var today = DateOnly.FromDateTime(DateTime.Today);

        await svc.ReconcileAccountAsync(new ReconcileAccountRequest(acct.AccountId, today, "user-a"));
        await svc.ReconcileAccountAsync(new ReconcileAccountRequest(acct.AccountId, today, "user-b"));

        var runs = await svc.GetReconciliationRunsAsync(acct.AccountId);
        Assert.Equal(2, runs.Count);
    }

    // ── Deactivation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAccount_RemovesFromActiveQuery()
    {
        var svc = CreateService();
        var fundId = Guid.NewGuid();
        var acct = await svc.CreateAccountAsync(MakeBankRequest(fundId: fundId));

        await svc.DeactivateAccountAsync(acct.AccountId, "test");

        var query = await svc.QueryAccountsAsync(
            new AccountStructureQuery(FundId: fundId, ActiveOnly: true));

        Assert.DoesNotContain(query, a => a.AccountId == acct.AccountId);
    }

    [Fact]
    public async Task DeactivateAccount_SetsIsActiveToFalse()
    {
        var svc = CreateService();
        var acct = await svc.CreateAccountAsync(MakeBankRequest());

        var deactivated = await svc.DeactivateAccountAsync(acct.AccountId, "test");

        Assert.NotNull(deactivated);
        Assert.False(deactivated!.IsActive);
        Assert.NotNull(deactivated.EffectiveTo);
    }
}
