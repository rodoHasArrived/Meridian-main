using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Storage;
using Meridian.Storage.Archival;

namespace Meridian.FinancialOperations.AccountingClose;

public interface IAccountingReportPackageService
{
    Task<AccountingReportPackageBundleDto> BuildPackageAsync(
        AccountingReportPackageRequestDto request,
        CancellationToken ct = default);

    Task<IReadOnlyList<AccountingReportPackageBundleDto>> ListPackagesAsync(
        string? fundProfileId = null,
        string? periodId = null,
        CancellationToken ct = default);
}

public sealed class AccountingReportPackageService : IAccountingReportPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IAccountingCloseManagementService? _closeManagementService;
    private readonly object _readGate = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly string? _persistencePath;
    private IReadOnlyList<AccountingReportPackageBundleDto> _inMemoryPackages = [];

    public AccountingReportPackageService(IAccountingCloseManagementService? closeManagementService = null)
    {
        _closeManagementService = closeManagementService;
    }

    public AccountingReportPackageService(
        IAccountingCloseManagementService? closeManagementService,
        StorageOptions storageOptions)
        : this(closeManagementService)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _persistencePath = Path.Combine(storageOptions.RootPath, "accounting", "accounting-report-packages.json");
    }

    public async Task<AccountingReportPackageBundleDto> BuildPackageAsync(
        AccountingReportPackageRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = RequireText(request.Actor, "Actor");
        var fundProfileId = RequireText(request.FundProfileId, "FundProfileId");
        var periodId = RequireText(request.PeriodId, "PeriodId");
        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant();
        var evidenceLinks = NormalizeEvidenceLinks(request.EvidenceLinks);
        var validationIssues = new List<AccountingConfigurationValidationIssueDto>();
        var closePlan = request.CloseWorkflowId.HasValue && _closeManagementService is not null
            ? await _closeManagementService.GetPeriodPlanAsync(request.CloseWorkflowId.Value, ct).ConfigureAwait(false)
            : null;

        if (request.CloseWorkflowId.HasValue && closePlan is null)
        {
            validationIssues.Add(new AccountingConfigurationValidationIssueDto(
                "ClosePlanMissing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Close workflow '{request.CloseWorkflowId.Value:D}' was not found.",
                request.CloseWorkflowId.Value.ToString("D"),
                "Create or select a close workflow before certifying the accounting report package."));
        }

        if (closePlan is not null)
        {
            validationIssues.AddRange(closePlan.ValidationIssues);
            if (!closePlan.IsPeriodLocked)
            {
                validationIssues.Add(new AccountingConfigurationValidationIssueDto(
                    "PeriodNotLocked",
                    AccountingConfigurationValidationSeverityDto.Warning,
                    "The close period is not locked; report package certification remains ready-for-review only.",
                    closePlan.ClosePlanId,
                    "Lock the period after close approvals before final report certification."));
            }
        }

        if (evidenceLinks.Count == 0)
        {
            validationIssues.Add(new AccountingConfigurationValidationIssueDto(
                "ReportEvidenceMissing",
                AccountingConfigurationValidationSeverityDto.Warning,
                "No retained report evidence links were supplied for the package.",
                periodId,
                "Attach close package, ledger, reconciliation, and report-render evidence before certification."));
        }

        var hasCritical = validationIssues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var state = hasCritical ? AccountingCertificationStateDto.Draft : AccountingCertificationStateDto.ReadyForReview;
        var packageId = $"accounting-report-package-{Sanitize(fundProfileId)}-{Sanitize(periodId)}";
        var certification = new ReportCertificationDto(
            $"report-certification-{Sanitize(fundProfileId)}-{Sanitize(periodId)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            state,
            actor,
            DateTimeOffset.UtcNow,
            hasCritical
                ? "Accounting report package has blocking validation issues and cannot be certified."
                : "Accounting report package is assembled and ready for human certification review.",
            evidenceLinks);
        var restatement = BuildRestatement(request, actor, evidenceLinks);
        var endingCapital = request.BeginningCapital + request.Contributions - request.Distributions + request.RealizedGainLoss;

        var financialStatements = new FinancialStatementPackageDto(
            packageId,
            fundProfileId,
            request.LedgerBookId,
            periodId,
            state,
            ["balance-sheet", "income-statement", "trial-balance", "statement-of-changes-in-capital"],
            evidenceLinks,
            certification,
            restatement);
        var investorStatement = new InvestorCapitalStatementDto(
            $"investor-capital-statement-{Sanitize(fundProfileId)}-{Sanitize(periodId)}-{Sanitize(request.CapitalAccountId ?? "aggregate")}",
            fundProfileId,
            string.IsNullOrWhiteSpace(request.CapitalAccountId) ? "capital-account:aggregate" : request.CapitalAccountId.Trim(),
            string.IsNullOrWhiteSpace(request.InvestorId) ? null : request.InvestorId.Trim(),
            periodId,
            request.BeginningCapital,
            request.Contributions,
            request.Distributions,
            request.RealizedGainLoss,
            endingCapital,
            currency,
            state,
            evidenceLinks);
        var realizedGainLoss = new RealizedGainLossReportDto(
            $"realized-gain-loss-{Sanitize(fundProfileId)}-{Sanitize(periodId)}",
            fundProfileId,
            request.LedgerBookId,
            periodId,
            new LedgerDimensionSetDto(FundId: fundProfileId, InvestorId: request.InvestorId, CapitalAccountId: request.CapitalAccountId),
            request.RealizedGainLoss,
            currency,
            state,
            evidenceLinks);
        var navPackage = new NavPackageDto(
            $"nav-package-{Sanitize(fundProfileId)}-{Sanitize(periodId)}",
            fundProfileId,
            request.LedgerBookId,
            periodId,
            request.Nav,
            currency,
            state,
            evidenceLinks,
            certification,
            restatement);

        var bundle = new AccountingReportPackageBundleDto(
            financialStatements,
            [investorStatement],
            realizedGainLoss,
            navPackage,
            certification,
            validationIssues);
        await SavePackageAsync(bundle, ct).ConfigureAwait(false);
        return bundle;
    }

    public Task<IReadOnlyList<AccountingReportPackageBundleDto>> ListPackagesAsync(
        string? fundProfileId = null,
        string? periodId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedFundProfileId = string.IsNullOrWhiteSpace(fundProfileId) ? null : fundProfileId.Trim();
        var normalizedPeriodId = string.IsNullOrWhiteSpace(periodId) ? null : periodId.Trim();
        var rows = ReadPackages()
            .Where(package => normalizedFundProfileId is null || string.Equals(package.FinancialStatements.FundProfileId, normalizedFundProfileId, StringComparison.OrdinalIgnoreCase))
            .Where(package => normalizedPeriodId is null || string.Equals(package.FinancialStatements.PeriodId, normalizedPeriodId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static package => package.Certification.RecordedAtUtc)
            .ToArray();

        return Task.FromResult<IReadOnlyList<AccountingReportPackageBundleDto>>(rows);
    }

    private async Task SavePackageAsync(AccountingReportPackageBundleDto bundle, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rows = ReadPackages()
                .Where(package => !string.Equals(package.FinancialStatements.PackageId, bundle.FinancialStatements.PackageId, StringComparison.OrdinalIgnoreCase))
                .Append(bundle)
                .ToArray();
            await SavePackagesAsync(rows, ct).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private IReadOnlyList<AccountingReportPackageBundleDto> ReadPackages()
    {
        if (string.IsNullOrWhiteSpace(_persistencePath) || !File.Exists(_persistencePath))
        {
            lock (_readGate)
            {
                return _inMemoryPackages;
            }
        }

        lock (_readGate)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<AccountingReportPackageSnapshot>(
                    File.ReadAllText(_persistencePath),
                    JsonOptions);
                return snapshot?.Packages ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }

    private async Task SavePackagesAsync(
        IReadOnlyList<AccountingReportPackageBundleDto> rows,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_persistencePath))
        {
            lock (_readGate)
            {
                _inMemoryPackages = rows.ToArray();
            }
            return;
        }

        var snapshot = new AccountingReportPackageSnapshot(
            rows
                .OrderBy(static package => package.FinancialStatements.FundProfileId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static package => package.FinancialStatements.PeriodId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static package => package.FinancialStatements.PackageId, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await AtomicFileWriter.WriteAsync(_persistencePath, json, ct).ConfigureAwait(false);
    }

    private static RestatementWorkflowDto? BuildRestatement(
        AccountingReportPackageRequestDto request,
        string actor,
        IReadOnlyList<string> evidenceLinks)
    {
        if (string.IsNullOrWhiteSpace(request.RestatementReasonCode))
        {
            return null;
        }

        return new RestatementWorkflowDto(
            $"restatement-{Sanitize(request.FundProfileId)}-{Sanitize(request.PeriodId)}-{Guid.NewGuid():N}",
            string.IsNullOrWhiteSpace(request.PriorPackageId) ? "prior-package:unresolved" : request.PriorPackageId.Trim(),
            request.RestatementReasonCode.Trim(),
            ManualJournalEntryStatusDto.Submitted,
            actor,
            DateTimeOffset.UtcNow,
            evidenceLinks);
    }

    private static IReadOnlyList<string> NormalizeEvidenceLinks(IEnumerable<string?> evidenceLinks)
        => evidenceLinks
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string RequireText(string? value, string label)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{label} is required.")
            : value.Trim();

    private static string Sanitize(string value)
        => string.Concat(value.Select(static ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'));

    private sealed record AccountingReportPackageSnapshot(
        IReadOnlyList<AccountingReportPackageBundleDto> Packages);
}
