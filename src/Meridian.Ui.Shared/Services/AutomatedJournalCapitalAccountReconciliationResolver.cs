using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Exact server-owned scope used to resolve the capital-account tie-out that supports one
/// fee-accrual execution. The evaluation timestamp is the actual execution/review time, not
/// the schedule's original due timestamp.
/// </summary>
public sealed record AutomatedJournalCapitalAccountReconciliationScope(
    string TenantId,
    string CompanyId,
    string FundProfileId,
    Guid LedgerBookId,
    string EntityId,
    string PeriodId,
    string Currency,
    DateTimeOffset EvaluatedAtUtc);

/// <summary>
/// Server-side source for reviewed capital-account balances, confidence, source version, and
/// retained evidence. Implementations must resolve from authoritative state; request payloads
/// and persisted schedule assertions are never an implementation fallback.
/// </summary>
public interface IAutomatedJournalCapitalAccountReconciliationResolver
{
    Task<AutomatedJournalCapitalAccountReconciliationDto?> ResolveAsync(
        AutomatedJournalCapitalAccountReconciliationScope scope,
        CancellationToken ct = default);
}

/// <summary>
/// Resolves fee-accrual tie-outs from certified accounting report packages. The current package
/// supplies NAV and investor-capital balances; prior certified NAV packages supply the durable
/// high-water history. Schedule payload values are deliberately not an input to this resolver.
/// </summary>
public sealed partial class AccountingReportPackageCapitalAccountReconciliationResolver
    : IAutomatedJournalCapitalAccountReconciliationResolver
{
    private const decimal TieOutTolerance = 0.01m;
    private readonly Func<IAccountingReportPackageService> _reportPackages;

    public AccountingReportPackageCapitalAccountReconciliationResolver(
        IAccountingReportPackageService reportPackages)
        : this(() => reportPackages ?? throw new ArgumentNullException(nameof(reportPackages)))
    {
    }

    public AccountingReportPackageCapitalAccountReconciliationResolver(
        Func<IAccountingReportPackageService> reportPackages)
    {
        _reportPackages = reportPackages ?? throw new ArgumentNullException(nameof(reportPackages));
    }

    public async Task<AutomatedJournalCapitalAccountReconciliationDto?> ResolveAsync(
        AutomatedJournalCapitalAccountReconciliationScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ct.ThrowIfCancellationRequested();

        if (!TryPeriodKey(scope.PeriodId, out var currentPeriod))
            return null;

        var dimensions = new LedgerDimensionSetDto(
            FundId: scope.FundProfileId,
            EntityId: scope.EntityId,
            BookId: scope.LedgerBookId.ToString("D"));
        var reportPackages = _reportPackages();
        var currentRows = await reportPackages.ListPackagesAsync(
                scope.FundProfileId,
                scope.PeriodId,
                scope.LedgerBookId,
                dimensions,
                scope.TenantId,
                scope.CompanyId,
                ct)
            .ConfigureAwait(false);
        var current = currentRows
            .Where(package => IsCertifiedAggregatePackage(package, scope, scope.PeriodId))
            .Where(package => package.Certification.RecordedAtUtc.ToUniversalTime() <= scope.EvaluatedAtUtc.ToUniversalTime())
            .OrderByDescending(static package => package.Certification.RecordedAtUtc)
            .FirstOrDefault();
        if (current is null || current.InvestorCapitalStatements.Count == 0)
            return null;

        var allRows = await reportPackages.ListPackagesAsync(
                scope.FundProfileId,
                periodId: null,
                scope.LedgerBookId,
                dimensions,
                scope.TenantId,
                scope.CompanyId,
                ct)
            .ConfigureAwait(false);
        var prior = allRows
            .Where(package => IsCertifiedAggregatePackage(package, scope, package.FinancialStatements.PeriodId))
            .Where(package => package.Certification.RecordedAtUtc.ToUniversalTime() <= scope.EvaluatedAtUtc.ToUniversalTime())
            .Select(package => new { Package = package, HasKey = TryPeriodKey(package.FinancialStatements.PeriodId, out var key), Key = key })
            .Where(static item => item.HasKey)
            .Where(item => item.Key < currentPeriod)
            .GroupBy(static item => item.Key)
            .Select(static group => group.OrderByDescending(item => item.Package.Certification.RecordedAtUtc).First().Package)
            .OrderBy(static package => package.FinancialStatements.PeriodId, StringComparer.Ordinal)
            .ToArray();

        var normalizedCurrency = RequireText(scope.Currency).ToUpperInvariant();
        if (!string.Equals(current.NavPackage.Currency, normalizedCurrency, StringComparison.OrdinalIgnoreCase) ||
            current.InvestorCapitalStatements.Any(statement =>
                !string.Equals(statement.Currency, normalizedCurrency, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var capitalOpening = current.InvestorCapitalStatements.Sum(static statement => statement.BeginningCapital);
        var capitalEnding = current.InvestorCapitalStatements.Sum(static statement => statement.EndingCapital);
        if (capitalOpening < 0m || capitalEnding < 0m || current.NavPackage.Nav < 0m)
            return null;

        var beginningNav = prior.LastOrDefault()?.NavPackage.Nav ?? capitalOpening;
        var highWaterMark = prior.Length == 0
            ? capitalOpening
            : Math.Max(capitalOpening, prior.Max(static package => package.NavPackage.Nav));
        var endingNavBeforeFees = current.NavPackage.Nav;
        var maximumVariance = new[]
        {
            decimal.Abs(beginningNav - capitalOpening),
            decimal.Abs(endingNavBeforeFees - capitalEnding)
        }.Max();
        var isReconciled = maximumVariance <= TieOutTolerance;

        var evidenceRoutes = current.Certification.EvidenceLinks
            .Concat(current.NavPackage.EvidenceLinks)
            .Concat(current.InvestorCapitalStatements.SelectMany(static statement => statement.EvidenceLinks))
            .Concat(prior.SelectMany(static package => package.NavPackage.EvidenceLinks))
            .Where(static route => !string.IsNullOrWhiteSpace(route))
            .Select(static route => route.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (evidenceRoutes.Length == 0)
            return null;

        var sourceVersion = BuildSourceVersion(current, prior);
        var evidenceLinks = evidenceRoutes
            .Select((route, index) => new OperationsEvidenceLinkDto(
                $"automated-journal-capital-tie-out:{sourceVersion}:{index + 1}",
                index == 0 ? "Certified capital-account and NAV reconciliation" : "Certified capital-account support",
                route,
                "accounting-report-package",
                current.Certification.RecordedAtUtc))
            .ToArray();

        return new AutomatedJournalCapitalAccountReconciliationDto(
            ReconciliationId: $"accounting-report-package:{current.FinancialStatements.PackageId}:{sourceVersion}",
            PeriodId: scope.PeriodId.Trim(),
            Currency: normalizedCurrency,
            ReconciledBeginningNav: beginningNav,
            ReconciledEndingNavBeforeFees: endingNavBeforeFees,
            ReconciledHighWaterMark: highWaterMark,
            CapitalAccountOpeningBalance: capitalOpening,
            CapitalAccountEndingBalanceBeforeFees: capitalEnding,
            CapitalAccountHighWaterMark: highWaterMark,
            MaximumVarianceTolerance: TieOutTolerance,
            ConfidenceScore: isReconciled ? 1m : 0.50m,
            IsReconciled: isReconciled,
            SourceVersion: sourceVersion,
            ReviewedBy: current.Certification.Actor,
            ReviewedAtUtc: current.Certification.RecordedAtUtc,
            EvidenceLinks: evidenceLinks);
    }

    private static bool IsCertifiedAggregatePackage(
        AccountingReportPackageBundleDto package,
        AutomatedJournalCapitalAccountReconciliationScope scope,
        string periodId)
    {
        var dimensions = package.FinancialStatements.Dimensions;
        return package.Certification.State == AccountingCertificationStateDto.Certified &&
               package.FinancialStatements.CertificationState == AccountingCertificationStateDto.Certified &&
               package.NavPackage.CertificationState == AccountingCertificationStateDto.Certified &&
               string.Equals(package.TenantId, scope.TenantId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(package.CompanyId, scope.CompanyId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(package.FinancialStatements.FundProfileId, scope.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
               package.FinancialStatements.LedgerBookId == scope.LedgerBookId &&
               string.Equals(package.FinancialStatements.PeriodId, periodId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(dimensions.FundId, scope.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(dimensions.EntityId, scope.EntityId, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(dimensions.BookId, scope.LedgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrWhiteSpace(dimensions.InvestorId) &&
               string.IsNullOrWhiteSpace(dimensions.CapitalAccountId);
    }

    private static string BuildSourceVersion(
        AccountingReportPackageBundleDto current,
        IReadOnlyList<AccountingReportPackageBundleDto> prior)
    {
        var canonical = string.Join(
            '|',
            current.FinancialStatements.PackageId,
            current.Certification.CertificationId,
            current.Certification.RecordedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            string.Join(',', prior.Select(static package =>
                $"{package.FinancialStatements.PackageId}:{package.Certification.CertificationId}:{package.NavPackage.Nav.ToString(CultureInfo.InvariantCulture)}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static bool TryPeriodKey(string? value, out int periodKey)
    {
        periodKey = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = PeriodTokenRegex().Match(value);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var month))
        {
            return false;
        }

        periodKey = checked((year * 12) + month);
        return true;
    }

    private static string RequireText(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty scope value is required.")
            : value.Trim();

    [GeneratedRegex(@"(?<!\d)(\d{4})-(0[1-9]|1[0-2])(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PeriodTokenRegex();
}
