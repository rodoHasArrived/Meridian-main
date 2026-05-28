using Meridian.Application.Reconciliation;

namespace Meridian.Tests.Application.Reconciliation;

public sealed class StatementValidationServiceTests
{
    [Fact]
    public async Task ValidateAsync_Returns_Blockers_For_Run_Level_Failures()
    {
        var referenceData = new FakeStatementValidationReferenceData
        {
            KnownAccounts = [],
            MappingProfiles = [],
            ToleranceProfiles = [],
            DuplicateImports = ["placeholder"]
        };
        var service = new StatementValidationService(referenceData);

        var result = await service.ValidateAsync(new StatementValidationRequestDto(
            SourcePath: Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv"),
            AccountId: "missing-account",
            StatementPeriodStart: null,
            StatementPeriodEnd: null,
            MappingProfileId: "missing-map",
            ToleranceProfileId: "missing-tolerance"));

        Assert.True(result.IsBlocked);
        Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.SourceFileUnreadable && issue.Severity == StatementValidationIssueSeverity.Blocker);
        Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.MissingAccount && issue.Severity == StatementValidationIssueSeverity.Blocker);
        Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.MissingStatementPeriod && issue.Severity == StatementValidationIssueSeverity.Blocker);
        Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.MissingMappingProfile && issue.Severity == StatementValidationIssueSeverity.Blocker);
        Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.MissingToleranceProfile && issue.Severity == StatementValidationIssueSeverity.Blocker);
    }

    [Fact]
    public async Task ValidateAsync_Detects_Duplicate_File_As_Blocker()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "account,securityIdentifier,quantity,price,cashAmount,activityCode,tradeDate,currency",
                "A1,AAPL,10,150.25,1502.50,BUY,2026-05-15,USD"
            ]);
            var fingerprint = await ComputeFingerprintAsync(filePath);
            var service = new StatementValidationService(new FakeStatementValidationReferenceData
            {
                DuplicateImports = [fingerprint]
            });

            var result = await service.ValidateAsync(CreateRequest(filePath));

            Assert.True(result.IsBlocked);
            Assert.Equal(fingerprint, result.ImportFingerprint);
            Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.DuplicateImport && issue.Severity == StatementValidationIssueSeverity.Blocker);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ValidateAsync_Returns_Structured_Row_Level_Blockers_And_Soft_Issues()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "account,securityIdentifier,quantity,price,cashAmount,activityCode,tradeDate,currency,extraFee,customNote",
                "A1,UNKNOWN,not-a-decimal,150.25,1502.50,BUY,2026-05-15,USD,1.00,note",
                "A1,AAPL,10,150.25,1502.50,BUY,2026-06-01,USD,1.00,note",
                "A1,AAPL,10,150.25,1502.50,BOGUS,2026-05-15,ZZZ,1.00,note",
                "A1,AAPL,10,150.25,1502.50,BUY,05/16/2026,USD,1.00,note"
            ]);
            var service = new StatementValidationService(new FakeStatementValidationReferenceData());

            var result = await service.ValidateAsync(CreateRequest(filePath));

            Assert.True(result.IsBlocked);
            Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.MalformedDecimal && issue.Severity == StatementValidationIssueSeverity.Blocker && issue.RowNumber == 2);
            Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.UnresolvedSecurity && issue.Severity == StatementValidationIssueSeverity.Soft && issue.RowNumber == 2);
            Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.OutOfPeriodRow && issue.Severity == StatementValidationIssueSeverity.Soft && issue.RowNumber == 3);
            Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.UnknownActivityType && issue.Severity == StatementValidationIssueSeverity.Soft && issue.RowNumber == 4);
            Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.UnsupportedCurrency && issue.Severity == StatementValidationIssueSeverity.Soft && issue.RowNumber == 4);
            Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.UnknownOptionalFeeColumn && issue.Severity == StatementValidationIssueSeverity.Soft);
            Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.UnsupportedOptionalField && issue.Severity == StatementValidationIssueSeverity.Soft);
            Assert.DoesNotContain(result.Issues, issue => issue.Code == StatementValidationIssueCode.MalformedDate && issue.RowNumber == 5);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task ValidateAsync_Can_Promote_Policy_Controlled_Soft_Issues_To_Blockers()
    {
        var filePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(filePath,
            [
                "account,securityIdentifier,quantity,price,cashAmount,activityCode,tradeDate,currency",
                "A1,UNKNOWN,10,150.25,1502.50,BUY,2026-06-01,USD"
            ]);
            var service = new StatementValidationService(new FakeStatementValidationReferenceData());

            var result = await service.ValidateAsync(CreateRequest(filePath) with
            {
                Policy = new StatementValidationPolicyDto(
                    UnresolvedSecurityIsBlocker: true,
                    OutOfPeriodRowsAreBlockers: true)
            });

            Assert.True(result.IsBlocked);
            Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.UnresolvedSecurity && issue.Severity == StatementValidationIssueSeverity.Blocker);
            Assert.Contains(result.Issues, issue => issue.Code == StatementValidationIssueCode.OutOfPeriodRow && issue.Severity == StatementValidationIssueSeverity.Blocker);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static StatementValidationRequestDto CreateRequest(string sourcePath) =>
        new(
            SourcePath: sourcePath,
            AccountId: "A1",
            StatementPeriodStart: new DateOnly(2026, 5, 1),
            StatementPeriodEnd: new DateOnly(2026, 5, 31),
            MappingProfileId: "default",
            ToleranceProfileId: "standard");

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
        public IReadOnlySet<string> ActivityCodes { get; init; } = new HashSet<string>(["BUY", "SELL", "DIV", "FEE", "CASH"], StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> Securities { get; init; } = new HashSet<string>(["AAPL", "MSFT", "SPY"], StringComparer.OrdinalIgnoreCase);

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
