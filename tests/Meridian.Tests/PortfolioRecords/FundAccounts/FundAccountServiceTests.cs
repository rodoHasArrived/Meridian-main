using FluentAssertions;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Contracts.FundStructure;

namespace Meridian.Tests.PortfolioRecords.FundAccounts;

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
            acct.AccountId,
            today,
            "USD",
            1_000_000m,
            "BankStatement",
            "test",
            UnrealizedPnl: 12_500m,
            RealizedPnl: 2_250m));

        var latest = await svc.GetLatestBalanceSnapshotAsync(acct.AccountId);

        Assert.NotNull(latest);
        Assert.Equal(1_000_000m, latest!.CashBalance);
        Assert.Equal("USD", latest.Currency);
        Assert.Equal(12_500m, latest.UnrealizedPnl);
        Assert.Equal(2_250m, latest.RealizedPnl);
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

    [Fact]
    public async Task AccountQueryService_FiltersAndSortsReadModels()
    {
        var svc = CreateService();
        var query = (IAccountQueryService)svc;
        var zulu = await svc.CreateAccountAsync(MakeBankRequest() with { DisplayName = "Zulu Bank", BaseCurrency = "USD" });
        var alpha = await svc.CreateAccountAsync(MakeBankRequest() with { DisplayName = "Alpha Bank", BaseCurrency = "USD" });
        await svc.CreateAccountAsync(MakeBankRequest() with { DisplayName = "Euro Bank", BaseCurrency = "EUR" });
        await svc.DeactivateAccountAsync(zulu.AccountId, "test");

        var filtered = await query.ListAccountsAsync(AccountTypeDto.Bank, true, "USD");
        Assert.Single(filtered);
        Assert.Equal(alpha.AccountId, filtered[0].AccountId);

        var sorted = await query.ListAccountsAsync(AccountTypeDto.Bank, null, null);
        Assert.Equal(new[] { "Alpha Bank", "Euro Bank", "Zulu Bank" }, sorted.Select(static account => account.DisplayName).ToArray());
    }

    [Theory]
    [InlineData(AccountOperationalStatusDto.Active, false)]
    [InlineData(AccountOperationalStatusDto.Suspended, true)]
    [InlineData(AccountOperationalStatusDto.Closed, true)]
    public async Task UpdateCustodianDetails_EnforcesOperationalStatus(AccountOperationalStatusDto status, bool shouldFail)
    {
        var svc = CreateService();
        var account = await svc.CreateAccountAsync(MakeCustodyRequest() with { OperationalStatus = status });
        var action = () => svc.UpdateCustodianDetailsAsync(account.AccountId, new UpdateCustodianAccountDetailsRequest(
            new CustodianAccountDetailsDto("x", null, null, null, null, null, null, null), "tester"));

        if (shouldFail)
            await Assert.ThrowsAsync<AccountStatusPolicyException>(action);
        else
            Assert.NotNull(await action());
    }

    [Theory]
    [InlineData(AccountOperationalStatusDto.Active, false)]
    [InlineData(AccountOperationalStatusDto.Suspended, true)]
    [InlineData(AccountOperationalStatusDto.Closed, true)]
    public async Task RecordBalanceSnapshot_EnforcesOperationalStatus(AccountOperationalStatusDto status, bool shouldFail)
    {
        var svc = CreateService();
        var account = await svc.CreateAccountAsync(MakeBankRequest() with { OperationalStatus = status });
        var action = () => svc.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            account.AccountId, DateOnly.FromDateTime(DateTime.Today), "USD", 100m, "Manual"));

        if (shouldFail)
            await Assert.ThrowsAsync<AccountStatusPolicyException>(action);
        else
            Assert.NotNull(await action());
    }

    [Fact]
    public async Task SyncHistory_PersistsAndDedupesByAccountCapabilityAndCorrelation()
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-fund-account-tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var svc = new InMemoryFundAccountService(path);
            var account = await svc.CreateAccountAsync(MakeBankRequest() with
            {
                Institution = "Meridian Bank",
                LedgerReference = "CASH-OPS"
            });

            var first = await svc.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
                AccountId: account.AccountId,
                Capability: "bank-balances",
                Status: AccountSyncStatusDto.Failed,
                ProviderLinkStatus: AccountProviderLinkStatusDto.SyncFailed,
                ProviderId: "bank-provider",
                ExternalAccountId: "BA-1",
                CorrelationId: "sync-1",
                FailureKind: AccountSyncFailureKindDto.ProviderUnavailable,
                FailureMessage: "Provider unavailable."));
            var second = await svc.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
                AccountId: account.AccountId,
                Capability: "bank-balances",
                Status: AccountSyncStatusDto.Succeeded,
                ProviderLinkStatus: AccountProviderLinkStatusDto.Verified,
                ProviderId: "bank-provider",
                ExternalAccountId: "BA-1",
                CorrelationId: "sync-1",
                RawEvidencePath: "artifacts/account-sync/bank/raw.json"));

            second.SyncHistoryId.Should().Be(first.SyncHistoryId);

            var restored = new InMemoryFundAccountService(path);
            var history = await restored.GetSyncHistoryAsync(account.AccountId);

            history.Should().ContainSingle();
            history[0].Status.Should().Be(AccountSyncStatusDto.Succeeded);
            history[0].RawEvidencePath.Should().Be("artifacts/account-sync/bank/raw.json");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Readiness_FlagsStaleSyncMissingLedgerAndSecurityMasterCoverage()
    {
        var svc = CreateService();
        var account = await svc.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(),
            AccountTypeDto.Brokerage,
            "BRK-READY",
            "Readiness Brokerage",
            "USD",
            DateTimeOffset.UtcNow.AddDays(-1),
            "tests",
            Institution: "alpaca"));

        await svc.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            AccountId: account.AccountId,
            Capability: "brokerage-sync",
            Status: AccountSyncStatusDto.Succeeded,
            ProviderLinkStatus: AccountProviderLinkStatusDto.Verified,
            ProviderId: "alpaca",
            ExternalAccountId: "PA-READY",
            AttemptedAt: DateTimeOffset.UtcNow.AddHours(-2),
            CompletedAt: DateTimeOffset.UtcNow.AddHours(-2),
            FreshUntil: DateTimeOffset.UtcNow.AddMinutes(-5),
            SecurityMissingCount: 2,
            ProjectionEvidencePath: "artifacts/account-sync/projection.json"));

        var readiness = await svc.GetReadinessAsync(account.AccountId);

        readiness.Should().NotBeNull();
        readiness!.IsReady.Should().BeFalse();
        readiness.ProviderLinkStatus.Should().Be(AccountProviderLinkStatusDto.Verified);
        readiness.Issues.Select(static issue => issue.Code).Should().Contain([
            "account.sync.stale",
            "account.ledger_mapping.missing",
            "account.security_master.coverage_missing"
        ]);
        readiness.Issues.Single(issue => issue.Code == "account.security_master.coverage_missing")
            .EvidenceLink.Should().Be("artifacts/account-sync/projection.json");
    }

    [Fact]
    public async Task Readiness_FlagsFailedSyncWithStructuredFailure()
    {
        var svc = CreateService();
        var account = await svc.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(),
            AccountTypeDto.Brokerage,
            "BRK-FAILED",
            "Failed Brokerage",
            "USD",
            DateTimeOffset.UtcNow.AddDays(-1),
            "tests",
            Institution: "alpaca",
            LedgerReference: "BROKERAGE-CASH"));

        await svc.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            AccountId: account.AccountId,
            Capability: "brokerage-sync",
            Status: AccountSyncStatusDto.Failed,
            ProviderLinkStatus: AccountProviderLinkStatusDto.Expired,
            ProviderId: "alpaca",
            ExternalAccountId: "PA-FAILED",
            FailureKind: AccountSyncFailureKindDto.CredentialMissing,
            FailureMessage: "Credentials are missing.",
            RawEvidencePath: "artifacts/account-sync/failed.json"));

        var readiness = await svc.GetReadinessAsync(account.AccountId);

        readiness.Should().NotBeNull();
        readiness!.IsReady.Should().BeFalse();
        readiness.Issues.Should().Contain(issue =>
            issue.Code == "account.sync.failed"
            && issue.Severity == AccountReadinessSeverityDto.Critical
            && issue.EvidenceLink == "artifacts/account-sync/failed.json");
    }

    [Fact]
    public async Task Readiness_FlagsOpenReconciliationBreaks()
    {
        var svc = CreateService();
        var account = await svc.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(),
            AccountTypeDto.Brokerage,
            "BRK-RECON",
            "Reconciliation Brokerage",
            "USD",
            DateTimeOffset.UtcNow.AddDays(-1),
            "tests",
            Institution: "alpaca",
            LedgerReference: "BROKERAGE-CASH"));
        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);

        await svc.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            account.AccountId,
            asOf,
            "USD",
            CashBalance: 100m,
            Source: "manual",
            RecordedBy: "tests"));
        await svc.RecordBalanceSnapshotAsync(new RecordAccountBalanceSnapshotRequest(
            account.AccountId,
            asOf,
            "USD",
            CashBalance: 125m,
            Source: "brokerage-sync:alpaca",
            RecordedBy: "tests",
            ExternalReference: "PA-RECON"));
        await svc.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            AccountId: account.AccountId,
            Capability: "brokerage-sync",
            Status: AccountSyncStatusDto.Succeeded,
            ProviderLinkStatus: AccountProviderLinkStatusDto.Verified,
            ProviderId: "alpaca",
            ExternalAccountId: "PA-RECON",
            FreshUntil: DateTimeOffset.UtcNow.AddHours(1)));
        await svc.ReconcileAccountAsync(new ReconcileAccountRequest(account.AccountId, asOf, "tests"));

        var readiness = await svc.GetReadinessAsync(account.AccountId);

        readiness.Should().NotBeNull();
        readiness!.Issues.Should().Contain(issue =>
            issue.Code == "account.reconciliation.breaks_open"
            && issue.Severity == AccountReadinessSeverityDto.Warning);
    }

    [Fact]
    public async Task MarginSnapshots_PersistAndDedupesByCorrelation()
    {
        var path = Path.Combine(Path.GetTempPath(), "meridian-fund-account-tests", $"{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var svc = new InMemoryFundAccountService(path);
            var account = await svc.CreateAccountAsync(new CreateAccountRequest(
                Guid.NewGuid(),
                AccountTypeDto.Margin,
                "MRG-PERSIST",
                "Persistent Margin",
                "USD",
                DateTimeOffset.UtcNow.AddDays(-1),
                "tests",
                Institution: "alpaca",
                LedgerReference: "BROKERAGE-MARGIN"));

            var first = await svc.RecordMarginSnapshotAsync(new RecordMarginSnapshotRequest(
                AccountId: account.AccountId,
                EffectiveAt: DateTimeOffset.UtcNow.AddMinutes(-5),
                Currency: "usd",
                MarginType: MarginModelTypeDto.RegT,
                ExcessLiquidity: 25_000m,
                BuyingPower: 100_000m,
                CorrelationId: "margin-sync-1",
                SnapshotEvidencePath: "artifacts/account-sync/margin/current.json"));
            var second = await svc.RecordMarginSnapshotAsync(new RecordMarginSnapshotRequest(
                AccountId: account.AccountId,
                EffectiveAt: DateTimeOffset.UtcNow,
                Currency: "USD",
                MarginType: MarginModelTypeDto.RegT,
                ExcessLiquidity: 30_000m,
                BuyingPower: 120_000m,
                CorrelationId: "margin-sync-1",
                SnapshotEvidencePath: "artifacts/account-sync/margin/current.json"));

            second.MarginSnapshotId.Should().Be(first.MarginSnapshotId);

            var restored = new InMemoryFundAccountService(path);
            var snapshots = await restored.GetMarginSnapshotsAsync(account.AccountId);
            var latest = await restored.GetLatestMarginSnapshotAsync(account.AccountId);

            snapshots.Should().ContainSingle();
            latest.Should().NotBeNull();
            latest!.Currency.Should().Be("USD");
            latest.ExcessLiquidity.Should().Be(30_000m);
            latest.SnapshotEvidencePath.Should().Be("artifacts/account-sync/margin/current.json");
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Readiness_FlagsMissingMarginSnapshotForMarginAccount()
    {
        var svc = CreateService();
        var account = await svc.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(),
            AccountTypeDto.Margin,
            "MRG-MISSING",
            "Missing Margin",
            "USD",
            DateTimeOffset.UtcNow.AddDays(-1),
            "tests",
            Institution: "alpaca",
            LedgerReference: "BROKERAGE-MARGIN"));
        await svc.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            AccountId: account.AccountId,
            Capability: "margin-sync",
            Status: AccountSyncStatusDto.Succeeded,
            ProviderLinkStatus: AccountProviderLinkStatusDto.Verified,
            ProviderId: "alpaca",
            ExternalAccountId: "PA-MARGIN",
            FreshUntil: DateTimeOffset.UtcNow.AddHours(1)));

        var readiness = await svc.GetReadinessAsync(account.AccountId);

        readiness.Should().NotBeNull();
        readiness!.Issues.Should().Contain(issue =>
            issue.Code == "account.margin.snapshot.missing"
            && issue.Severity == AccountReadinessSeverityDto.Critical);
    }

    [Fact]
    public async Task Readiness_FlagsMarginSnapshotRiskBlockers()
    {
        var svc = CreateService();
        var account = await svc.CreateAccountAsync(new CreateAccountRequest(
            Guid.NewGuid(),
            AccountTypeDto.Margin,
            "MRG-RISK",
            "Risk Margin",
            "USD",
            DateTimeOffset.UtcNow.AddDays(-1),
            "tests",
            Institution: "alpaca",
            LedgerReference: "BROKERAGE-MARGIN"));
        await svc.RecordSyncHistoryAsync(new RecordAccountSyncHistoryRequest(
            AccountId: account.AccountId,
            Capability: "margin-sync",
            Status: AccountSyncStatusDto.Succeeded,
            ProviderLinkStatus: AccountProviderLinkStatusDto.Verified,
            ProviderId: "alpaca",
            ExternalAccountId: "PA-RISK",
            FreshUntil: DateTimeOffset.UtcNow.AddHours(1)));
        await svc.RecordMarginSnapshotAsync(new RecordMarginSnapshotRequest(
            AccountId: account.AccountId,
            EffectiveAt: DateTimeOffset.UtcNow.AddHours(-3),
            Currency: "USD",
            MarginType: MarginModelTypeDto.Unsupported,
            MarginCallStatus: MarginCallStatusDto.Active,
            MaintenanceMargin: 50_000m,
            ExcessLiquidity: -125m,
            MissingRequirementCount: 2,
            MissingCollateralClassificationCount: 1,
            ConcentrationLimitBreachCount: 1,
            IsLiveAccount: true,
            ApprovedForLiveMargin: false,
            ProviderId: "ibkr",
            ExternalAccountId: "IB-RISK",
            FreshUntil: DateTimeOffset.UtcNow.AddMinutes(-30),
            SnapshotEvidencePath: "artifacts/account-sync/margin/ib-risk.json"));

        var readiness = await svc.GetReadinessAsync(account.AccountId);

        readiness.Should().NotBeNull();
        readiness!.IsReady.Should().BeFalse();
        readiness.Issues.Select(static issue => issue.Code).Should().Contain([
            "account.margin.model.unsupported",
            "account.margin.snapshot.stale",
            "account.margin.requirements.missing",
            "account.margin.excess_liquidity.negative",
            "account.margin.call.active",
            "account.margin.collateral_classification.missing",
            "account.margin.concentration_limit.breached",
            "account.margin.live_approval.missing",
            "account.margin.provider_mismatch"
        ]);
        readiness.Issues.Single(issue => issue.Code == "account.margin.call.active")
            .EvidenceLink.Should().Be("artifacts/account-sync/margin/ib-risk.json");
    }
}
