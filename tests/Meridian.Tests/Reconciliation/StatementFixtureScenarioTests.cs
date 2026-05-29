using Meridian.Application.Reconciliation;
using Meridian.Domain.Reconciliation;

namespace Meridian.Tests.Reconciliation;

/// <summary>
/// Guards against month-end broker statement intake failures where duplicates, invalid rows,
/// match tolerance, candidate review, and case queue creation must remain operator-visible.
/// </summary>
public sealed class StatementFixtureScenarioTests
{
    [Fact]
    public async Task Scenario_MonthEndCleanStatement_ServiceFullyReconcilesFixture()
    {
        var path = FixturePath("statement-clean-reconciles.csv");
        var service = new StatementReconciliationService();

        var validation = await service.ValidateAsync("broker", path, CancellationToken.None);
        var result = await service.ReconcileAsync("broker", path, CancellationToken.None);
        var intake = await service.CreateExternalStatementCasesAsync("broker", path, CancellationToken.None);

        Assert.Contains("canonical-csv-v1", validation);
        Assert.Equal(1, result.MatchCount);
        Assert.Equal(0, result.UnresolvedCount);
        Assert.Equal(1, intake.RowCount);
        Assert.Empty(intake.Cases);
    }

    [Fact]
    public async Task Scenario_MonthEndShadowedStatementHeader_ReconciliationRejectsDuplicateCanonicalColumns()
    {
        var source = Path.Combine(Path.GetTempPath(), $"meridian-shadowed-statement-{Guid.NewGuid():N}.csv");
        await File.WriteAllLinesAsync(source,
        [
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate,quantity",
            "A1,SPY,10,500,0,position,2026-05-29,0"
        ]);
        try
        {
            var service = new StatementReconciliationService();

            var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await service.ReconcileAsync("broker", source, CancellationToken.None));

            Assert.Contains("duplicate columns", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("quantity", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public async Task Scenario_MonthEndDuplicateStatement_ValidationCreatesDuplicateBlocker()
    {
        var path = FixturePath("statement-clean-reconciles.csv");
        var fingerprint = await ComputeFingerprintAsync(path);
        var service = new StatementValidationService(new FakeStatementValidationReferenceData
        {
            DuplicateImports = new HashSet<string>([fingerprint], StringComparer.OrdinalIgnoreCase)
        });

        var result = await service.ValidateAsync(CreateValidationRequest(path));

        Assert.True(result.IsBlocked);
        Assert.Equal(fingerprint, result.ImportFingerprint);
        Assert.Contains(result.Issues, issue =>
            issue.Code == StatementValidationIssueCode.DuplicateImport
            && issue.Severity == StatementValidationIssueSeverity.Blocker);
    }

    [Fact]
    public async Task Scenario_MonthEndBrokerFormat_MappingProfileParsesSampleBrokerRows()
    {
        var source = Path.Combine(Path.GetTempPath(), $"meridian-sample-broker-{Guid.NewGuid():N}.csv");
        await File.WriteAllLinesAsync(source,
        [
            "BrokerAccount,Ticker,Units,ExecutionPrice,NetCash,TxnCode,TradeDate,SettleDate,CCY,Commission,BrokerTransactionId",
            "BRK-1,MSFT,12,410,0,POS,2026-05-27,2026-05-29,EUR,0,EXT-1",
            "BRK-1,MSFT,0,0,18.25,DIV,2026-05-28,2026-05-30,EUR,0,EXT-2"
        ]);
        try
        {
            var service = new StatementReconciliationService();

            var result = await service.CreateExternalStatementCasesAsync(
                "broker",
                source,
                StatementMappingProfileRegistry.SampleBrokerCsvV1ProfileId,
                CancellationToken.None);

            Assert.Equal(2, result.RowCount);
            Assert.Equal(1, result.MatchCount);
            Assert.Single(result.Cases);
            Assert.All(result.Cases, reconciliationCase => Assert.Equal("fund-ops", reconciliationCase.Owner));
        }
        finally
        {
            File.Delete(source);
        }
    }

    [Fact]
    public async Task Scenario_MonthEndInvalidStatement_ValidationCreatesRowAndRunBlockers()
    {
        var path = FixturePath("statement-invalid-blockers.csv");
        var service = new StatementValidationService(new FakeStatementValidationReferenceData());

        var result = await service.ValidateAsync(CreateValidationRequest(path) with
        {
            Policy = new StatementValidationPolicyDto(
                UnsupportedCurrencyIsBlocker: true,
                UnknownActivityTypeIsBlocker: true,
                UnresolvedSecurityIsBlocker: true,
                OutOfPeriodRowsAreBlockers: true)
        });

        Assert.True(result.IsBlocked);
        Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.MalformedDecimal && issue.RowNumber == 2);
        Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.UnknownActivityType && issue.Severity == StatementValidationIssueSeverity.Blocker);
        Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.UnsupportedCurrency && issue.Severity == StatementValidationIssueSeverity.Blocker);
        Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.OutOfPeriodRow && issue.Severity == StatementValidationIssueSeverity.Blocker);
        Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.MissingRequiredField && issue.RowNumber == 3);
    }

    [Fact]
    public void Scenario_MonthEndStatementMatching_ClassifiesExactToleranceCandidatesAndUnmatched()
    {
        var engine = new StatementMatchingEngine();
        var result = engine.Run(new StatementMatchingRequest(
            StatementPositions:
            [
                new("stmt-pos-exact", "A1", "SPY", new DateOnly(2026, 5, 29), 10m, 5000m, "statement:1"),
                new("stmt-pos-tolerance", "A1", "MSFT", new DateOnly(2026, 5, 29), 12.01m, 4925m, "statement:2"),
                new("stmt-pos-candidate", "A1", "QQQ", new DateOnly(2026, 5, 29), 7m, 3500m, "statement:3"),
                new("stmt-pos-unmatched", "A1", "TSLA", new DateOnly(2026, 5, 29), 3m, 600m, "statement:4")
            ],
            StatementCashBalances: [],
            StatementTransactions: [],
            InternalPositions:
            [
                new("book-pos-exact", "A1", "SPY", new DateOnly(2026, 5, 29), 10m, 5000m, "book:1"),
                new("book-pos-tolerance", "A1", "MSFT", new DateOnly(2026, 5, 29), 12m, 4920m, "book:2"),
                new("book-pos-candidate", "A1", "QQQ", new DateOnly(2026, 5, 28), 6m, 3100m, "book:3"),
                new("book-pos-unmatched", "A1", "NVDA", new DateOnly(2026, 5, 29), 2m, 2200m, "book:4")
            ],
            InternalCashBalances: [],
            InternalLedgerTransactions: [],
            ToleranceProfile: new StatementMatchingToleranceProfile(
                PositionQuantity: 0.05m,
                PositionMarketValue: 10m,
                CashBalance: 1m,
                TransactionQuantity: 0.01m,
                TransactionNetAmount: 1m)));

        Assert.Contains(result.Results, item => item.BrokerEvidenceReference == "statement:1" && item.MatchTier == StatementMatchTier.Exact);
        Assert.Contains(result.Results, item => item.BrokerEvidenceReference == "statement:2" && item.MatchTier == StatementMatchTier.Tolerance);
        Assert.Contains(result.Results, item => item.BrokerEvidenceReference == "statement:3" && item.MatchTier == StatementMatchTier.Candidate);
        Assert.Contains(result.Results, item => item.BrokerEvidenceReference == "statement:4" && item.MatchTier == StatementMatchTier.Unmatched);
        Assert.Contains(result.Results, item => item.InternalEvidenceReference == "book:4" && item.MatchTier == StatementMatchTier.Unmatched);
    }

    [Fact]
    public void Scenario_MonthEndTransactionTolerance_MatchesInsideConfiguredTolerance()
    {
        var engine = new StatementMatchingEngine();
        var result = engine.Run(new StatementMatchingRequest(
            StatementPositions: [],
            StatementCashBalances: [],
            StatementTransactions:
            [
                new("stmt-txn-1", null, "A1", "SPY", null, new DateOnly(2026, 5, 28), new DateOnly(2026, 5, 30), "BUY", 10m, 5000.40m, "statement:txn:1")
            ],
            InternalPositions: [],
            InternalCashBalances: [],
            InternalLedgerTransactions:
            [
                new("book-txn-1", null, "A1", "SPY", null, new DateOnly(2026, 5, 28), new DateOnly(2026, 5, 30), "BUY", 10m, 5000m, "book:txn:1")
            ],
            ToleranceProfile: new StatementMatchingToleranceProfile(0.01m, 1m, 1m, 0.01m, 1m)));

        var match = Assert.Single(result.Results);
        Assert.Equal(StatementMatchTier.Tolerance, match.MatchTier);
        Assert.Contains("statement-transaction-tolerance-v1", match.RuleIds);
    }

    [Fact]
    public async Task Scenario_MonthEndUnresolvedBreaks_CreateCaseQueueItems()
    {
        var path = FixturePath("statement-unresolved-breaks.csv");
        var service = new StatementReconciliationService();

        var result = await service.CreateExternalStatementCasesAsync("broker", path, CancellationToken.None);

        Assert.Equal(3, result.RowCount);
        Assert.Equal(1, result.MatchCount);
        Assert.Equal(2, result.Cases.Count);
        Assert.All(result.Cases, reconciliationCase =>
        {
            Assert.Equal("Open", reconciliationCase.Status);
            Assert.Equal("fund-ops", reconciliationCase.Owner);
            Assert.NotEmpty(reconciliationCase.AuditEvents);
            Assert.NotEmpty(reconciliationCase.CommentThreads);
        });
    }

    private static StatementValidationRequestDto CreateValidationRequest(string sourcePath) =>
        new(
            SourcePath: sourcePath,
            AccountId: "A1",
            StatementPeriodStart: new DateOnly(2026, 5, 1),
            StatementPeriodEnd: new DateOnly(2026, 5, 31),
            MappingProfileId: "default",
            ToleranceProfileId: "standard");

    private static string FixturePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "Meridian.Tests", "Reconciliation", "Fixtures", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate reconciliation fixture '{fileName}'.", fileName);
    }

    private static async Task<string> ComputeFingerprintAsync(string sourcePath)
    {
        await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 64 * 1024, useAsync: true);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class FakeStatementValidationReferenceData : IStatementValidationReferenceData
    {
        public IReadOnlySet<string> KnownAccounts { get; init; } = new HashSet<string>(["A1"], StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> MappingProfiles { get; init; } = new HashSet<string>(["default"], StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> ToleranceProfiles { get; init; } = new HashSet<string>(["standard"], StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> DuplicateImports { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> SupportedCurrencies { get; init; } = new HashSet<string>(["USD", "EUR", "GBP"], StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> ActivityCodes { get; init; } = new HashSet<string>(["BUY", "SELL", "DIV", "FEE", "CASH", "position", "cash", "trade"], StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> Securities { get; init; } = new HashSet<string>(["SPY", "MSFT", "AAPL"], StringComparer.OrdinalIgnoreCase);

        public Task<bool> AccountExistsAsync(string accountId, CancellationToken ct = default) =>
            Task.FromResult(KnownAccounts.Contains(accountId));

        public Task<bool> MappingProfileExistsAsync(string mappingProfileId, CancellationToken ct = default) =>
            Task.FromResult(MappingProfiles.Contains(mappingProfileId));

        public Task<bool> ToleranceProfileExistsAsync(string toleranceProfileId, CancellationToken ct = default) =>
            Task.FromResult(ToleranceProfiles.Contains(toleranceProfileId));

        public Task<bool> ImportExistsAsync(string importFingerprint, CancellationToken ct = default) =>
            Task.FromResult(DuplicateImports.Contains(importFingerprint));

        public Task<bool> CurrencySupportedAsync(string currency, CancellationToken ct = default) =>
            Task.FromResult(SupportedCurrencies.Contains(currency));

        public Task<bool> TryMapActivityTypeAsync(string transactionCode, CancellationToken ct = default) =>
            Task.FromResult(ActivityCodes.Contains(transactionCode));

        public Task<bool> TryResolveSecurityAsync(string securityIdentifier, CancellationToken ct = default) =>
            Task.FromResult(Securities.Contains(securityIdentifier));
    }
}
