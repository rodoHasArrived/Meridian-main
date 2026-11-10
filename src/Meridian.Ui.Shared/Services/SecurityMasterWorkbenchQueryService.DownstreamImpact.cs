using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

// Downstream impact: fund-scoped exposure, impact severity and impact summaries.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private async Task<SecurityMasterDownstreamImpactDto> BuildDownstreamImpactAsync(
        SecurityDetailDto detail,
        string? fundProfileId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            return new SecurityMasterDownstreamImpactDto(
                FundProfileId: null,
                IsScoped: false,
                Severity: SecurityMasterImpactSeverity.Unknown,
                Summary: "Not scoped to a fund profile. Downstream impact is unknown.",
                PortfolioExposureSummary: "Portfolio impact is not scoped.",
                LedgerExposureSummary: "Ledger impact is not scoped.",
                ReconciliationExposureSummary: "Reconciliation impact is not scoped.",
                ReportPackExposureSummary: "Report-pack impact is not scoped.",
                MatchedRunCount: 0,
                PortfolioExposureCount: 0,
                LedgerExposureCount: 0,
                ReconciliationExposureCount: 0,
                ReportPackExposureCount: 0,
                Links: []);
        }

        var normalizedFundProfileId = fundProfileId.Trim();

        // Tenant isolation (SEC-005): a fund the registry reports as owned by another tenant must surface as
        // withheld/unknown — NOT as an empty "no runs" (Severity.None) impact. Reporting None would let
        // downstream low-risk gates (e.g. bulk conflict resolution, which only proceeds on None/Low) treat
        // a foreign scope as safe instead of forbidden; Unknown keeps it non-eligible while still disclosing
        // zero runs/exposures. Unbound/legacy/own funds and uncertainty all pass through and resolve below.
        if (!await IsFundAccessibleToCurrentTenantAsync(normalizedFundProfileId, ct).ConfigureAwait(false))
        {
            return BuildWithheldFundImpact(normalizedFundProfileId);
        }

        var relatedRuns = await LoadFundRunsAsync(normalizedFundProfileId, ct).ConfigureAwait(false);
        if (relatedRuns.Count == 0)
        {
            return new SecurityMasterDownstreamImpactDto(
                FundProfileId: normalizedFundProfileId,
                IsScoped: true,
                Severity: SecurityMasterImpactSeverity.None,
                Summary: $"Fund profile {normalizedFundProfileId} has no recorded runs in the workstation store.",
                PortfolioExposureSummary: "No scoped portfolio exposure detected.",
                LedgerExposureSummary: "No scoped ledger exposure detected.",
                ReconciliationExposureSummary: "No scoped reconciliation exposure detected.",
                ReportPackExposureSummary: "No scoped report-pack exposure detected.",
                MatchedRunCount: 0,
                PortfolioExposureCount: 0,
                LedgerExposureCount: 0,
                ReconciliationExposureCount: 0,
                ReportPackExposureCount: 0,
                Links: []);
        }

        var portfolioTasks = relatedRuns
            .Select(run => _portfolioReadService.BuildSummaryAsync(run, ct))
            .ToArray();
        var ledgerTasks = relatedRuns
            .Select(run => _ledgerReadService.BuildSummaryAsync(run, ct))
            .ToArray();

        var combinedTasks = portfolioTasks
            .Cast<Task>()
            .Concat(ledgerTasks)
            .ToArray();

        await Task.WhenAll(combinedTasks).ConfigureAwait(false);

        var portfolios = new List<PortfolioSummary?>(portfolioTasks.Length);
        foreach (var task in portfolioTasks)
        {
            portfolios.Add(await task.ConfigureAwait(false));
        }

        var ledgers = new List<LedgerSummary?>(ledgerTasks.Length);
        foreach (var task in ledgerTasks)
        {
            ledgers.Add(await task.ConfigureAwait(false));
        }

        var normalizedIdentifiers = BuildNormalizedIdentifierSet(detail);
        var portfolioRunCount = 0;
        var portfolioExposureCount = 0;

        foreach (var portfolio in portfolios.Where(static portfolio => portfolio is not null))
        {
            var matches = portfolio!.Positions.Count(position => MatchesSecurity(position.Symbol, position.Security?.SecurityId, detail, normalizedIdentifiers));
            if (matches == 0)
            {
                continue;
            }

            portfolioRunCount++;
            portfolioExposureCount += matches;
        }

        var ledgerRunCount = 0;
        var ledgerExposureCount = 0;
        foreach (var ledger in ledgers.Where(static ledger => ledger is not null))
        {
            var matches = ledger!.TrialBalance.Count(line => MatchesSecurity(line.Symbol, line.Security?.SecurityId, detail, normalizedIdentifiers));
            if (matches == 0)
            {
                continue;
            }

            ledgerRunCount++;
            ledgerExposureCount += matches;
        }

        var reconciliationRunCount = 0;
        var reconciliationExposureCount = 0;
        var reconciliationUnavailableRunCount = 0;
        if (_reconciliationRunService is not null)
        {
            foreach (var run in relatedRuns)
            {
                ct.ThrowIfCancellationRequested();

                var detailResult = await _reconciliationRunService
                    .GetLatestForRunAsync(run.RunId, ct)
                    .ConfigureAwait(false);
                if (detailResult is null)
                {
                    reconciliationUnavailableRunCount++;
                    continue;
                }

                if (detailResult?.SecurityCoverageIssues is null)
                {
                    continue;
                }

                var issueMatches = detailResult.SecurityCoverageIssues.Count(issue =>
                    MatchesSecurity(issue.Symbol, securityId: null, detail, normalizedIdentifiers));
                if (issueMatches == 0)
                {
                    continue;
                }

                reconciliationRunCount++;
                reconciliationExposureCount += issueMatches;
            }
        }

        var reportPackExposureCount = 0;
        if (relatedRuns.Any(run => run.Metrics?.Ledger is not null))
        {
            var report = await _reportGenerationService
                .GenerateAsync(
                    new ReportRequest(
                        FundId: normalizedFundProfileId,
                        AsOf: DateTimeOffset.UtcNow,
                        FundLedger: BuildFundLedgerBook(normalizedFundProfileId, relatedRuns)),
                    ct)
                .ConfigureAwait(false);

            reportPackExposureCount = report.TrialBalance.Count(row =>
                MatchesSecurity(row.Symbol, securityId: null, detail, normalizedIdentifiers));
        }

        var severity = DetermineImpactSeverity(
            portfolioExposureCount,
            ledgerExposureCount,
            reconciliationExposureCount,
            reportPackExposureCount,
            reconciliationUnavailableRunCount);

        var overallSummary = BuildImpactSummary(
            relatedRuns.Count,
            portfolioExposureCount,
            ledgerExposureCount,
            reconciliationExposureCount,
            reportPackExposureCount,
            reconciliationUnavailableRunCount);
        var reconciliationExposureSummary = reconciliationExposureCount == 0
            ? reconciliationUnavailableRunCount == 0
                ? "No scoped reconciliation exposure detected."
                : $"Reconciliation impact has not been materialized for {reconciliationUnavailableRunCount} scoped run(s)."
            : reconciliationUnavailableRunCount == 0
                ? $"{reconciliationExposureCount} reconciliation issue(s) across {reconciliationRunCount} run(s) reference this security."
                : $"{reconciliationExposureCount} reconciliation issue(s) across {reconciliationRunCount} run(s) reference this security. Reconciliation impact is still unavailable for {reconciliationUnavailableRunCount} scoped run(s).";

        var links = new List<SecurityMasterImpactLinkDto>(4);
        if (portfolioExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "portfolio",
                Label: "Open Portfolio Impact",
                Summary: $"{portfolioExposureCount} position(s) across {portfolioRunCount} run(s) reference this security.",
                Severity: SecurityMasterImpactSeverity.Low,
                IsActive: true));
        }

        if (ledgerExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "ledger",
                Label: "Open Ledger Impact",
                Summary: $"{ledgerExposureCount} ledger line(s) across {ledgerRunCount} run(s) reference this security.",
                Severity: SecurityMasterImpactSeverity.Medium,
                IsActive: true));
        }

        if (reconciliationExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "reconciliation",
                Label: "Open Reconciliation Impact",
                Summary: $"{reconciliationExposureCount} reconciliation issue(s) across {reconciliationRunCount} run(s) reference this security.",
                Severity: SecurityMasterImpactSeverity.High,
                IsActive: true));
        }

        if (reportPackExposureCount > 0)
        {
            links.Add(new SecurityMasterImpactLinkDto(
                Target: "reportPack",
                Label: "Open Report Pack Impact",
                Summary: $"{reportPackExposureCount} report-pack row(s) reference this security in the current fund scope.",
                Severity: SecurityMasterImpactSeverity.High,
                IsActive: true));
        }

        return new SecurityMasterDownstreamImpactDto(
            FundProfileId: normalizedFundProfileId,
            IsScoped: true,
            Severity: severity,
            Summary: overallSummary,
            PortfolioExposureSummary: portfolioExposureCount == 0
                ? "No scoped portfolio exposure detected."
                : $"{portfolioExposureCount} position(s) across {portfolioRunCount} run(s) reference this security.",
            LedgerExposureSummary: ledgerExposureCount == 0
                ? "No scoped ledger exposure detected."
                : $"{ledgerExposureCount} ledger line(s) across {ledgerRunCount} run(s) reference this security.",
            ReconciliationExposureSummary: reconciliationExposureSummary,
            ReportPackExposureSummary: reportPackExposureCount == 0
                ? "No scoped report-pack exposure detected."
                : $"{reportPackExposureCount} report-pack row(s) reference this security.",
            MatchedRunCount: relatedRuns.Count,
            PortfolioExposureCount: portfolioExposureCount,
            LedgerExposureCount: ledgerExposureCount,
            ReconciliationExposureCount: reconciliationExposureCount,
            ReportPackExposureCount: reportPackExposureCount,
            Links: links);
    }

    /// <summary>
    /// Enumerates the process-wide run store for a single fund. Callers MUST first gate access with
    /// <see cref="IsFundAccessibleToCurrentTenantAsync"/> (tenant isolation, SEC-005): the run store carries
    /// no tenant key, so a foreign fund's runs are withheld at the impact/open-lot boundaries — a foreign
    /// scope yields a withheld/Unknown impact and empty lots, never a misleading empty "no runs" result.
    /// </summary>
    private async Task<IReadOnlyList<StrategyRunEntry>> LoadFundRunsAsync(string fundProfileId, CancellationToken ct)
    {
        var runs = new List<StrategyRunEntry>();
        await foreach (var run in _strategyRepository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            {
                runs.Add(run);
            }
        }

        return runs
            .OrderByDescending(static run => run.StartedAt)
            .ThenBy(static run => run.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Whether the fund profile may be resolved for the current request's tenant. Returns true (pass
    /// through) when there is no tenancy registry, a blank fund, no HTTP request context (background/test),
    /// no caller tenant scope, or the registry is unavailable — only a positive "owned by a different
    /// tenant" verdict withholds the fund's data.
    /// </summary>
    private async Task<bool> IsFundAccessibleToCurrentTenantAsync(string fundProfileId, CancellationToken ct)
    {
        if (_tenancyRegistry is null || string.IsNullOrWhiteSpace(fundProfileId))
        {
            return true;
        }

        var httpContext = _httpContextAccessor?.HttpContext;
        if (httpContext is null)
        {
            return true;
        }

        var tenant = HttpContextWorkstationTenantContextAccessor.Resolve(httpContext);
        if (!tenant.HasTenantScope)
        {
            return true;
        }

        try
        {
            return await _tenancyRegistry
                .IsAccessibleAsync(fundProfileId, tenant.TenantId!, tenant.CompanyId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Fail open to the deployment boundary rather than dropping restatement impact on uncertainty.
            return true;
        }
    }

    /// <summary>
    /// Sanitizes an operator-supplied fund scope for tenant isolation (SEC-005): returns the fund unchanged
    /// when it is blank, owned by the caller's tenant, unbound/legacy, or accessibility cannot be decided
    /// (fail open), and returns <c>null</c> — i.e. treat the request as unscoped — when the registry
    /// positively attributes the fund to a different tenant. Used at the snapshot/passport entry so a
    /// foreign fund never reaches any fund-scoped evidence path.
    /// </summary>
    private async Task<string?> SanitizeFundScopeAsync(string? fundProfileId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fundProfileId))
        {
            return fundProfileId;
        }

        return await IsFundAccessibleToCurrentTenantAsync(fundProfileId, ct).ConfigureAwait(false)
            ? fundProfileId
            : null;
    }

    /// <summary>
    /// Downstream-impact result for a fund the registry positively attributes to another tenant: scoped but
    /// withheld with <see cref="SecurityMasterImpactSeverity.Unknown"/> (SEC-005). Unknown — not None —
    /// keeps the foreign scope out of low-risk gates such as bulk conflict resolution while disclosing no
    /// runs or exposures.
    /// </summary>
    private static SecurityMasterDownstreamImpactDto BuildWithheldFundImpact(string fundProfileId)
        => new(
            FundProfileId: fundProfileId,
            IsScoped: true,
            Severity: SecurityMasterImpactSeverity.Unknown,
            Summary: $"Fund profile {fundProfileId} is owned by another tenant; downstream impact is withheld.",
            PortfolioExposureSummary: "Portfolio impact is withheld for a fund owned by another tenant.",
            LedgerExposureSummary: "Ledger impact is withheld for a fund owned by another tenant.",
            ReconciliationExposureSummary: "Reconciliation impact is withheld for a fund owned by another tenant.",
            ReportPackExposureSummary: "Report-pack impact is withheld for a fund owned by another tenant.",
            MatchedRunCount: 0,
            PortfolioExposureCount: 0,
            LedgerExposureCount: 0,
            ReconciliationExposureCount: 0,
            ReportPackExposureCount: 0,
            Links: []);

    private static FundLedgerBook BuildFundLedgerBook(string fundProfileId, IReadOnlyList<StrategyRunEntry> runs)
    {
        var ledgerBook = new FundLedgerBook(fundProfileId);
        foreach (var run in runs)
        {
            foreach (var journalEntry in run.Metrics?.Ledger?.Journal ?? [])
            {
                ledgerBook.FundLedger.Post(journalEntry);
            }
        }

        return ledgerBook;
    }

    private static int GetImpactLinkOrder(SecurityMasterImpactLinkDto link)
        => link.Target switch
        {
            "reconciliation" => 0,
            "ledger" => 1,
            "reportPack" => 2,
            "portfolio" => 3,
            _ => 4
        };

    private static SecurityMasterImpactSeverity DetermineImpactSeverity(
        int portfolioExposureCount,
        int ledgerExposureCount,
        int reconciliationExposureCount,
        int reportPackExposureCount,
        int reconciliationUnavailableRunCount)
    {
        if (reconciliationExposureCount > 0 || reportPackExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.High;
        }

        if (ledgerExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.Medium;
        }

        if (portfolioExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.Low;
        }

        if (reconciliationUnavailableRunCount > 0)
        {
            return SecurityMasterImpactSeverity.Unknown;
        }

        return SecurityMasterImpactSeverity.None;
    }

    private static SecurityMasterImpactSeverity DetermineConflictImpactSeverity(
        SecurityMasterConflict conflict,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        if (!downstreamImpact.IsScoped)
        {
            return SecurityMasterImpactSeverity.Unknown;
        }

        var normalizedFieldPath = conflict.FieldPath.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (normalizedFieldPath.Contains("CorporateAction", StringComparison.OrdinalIgnoreCase) &&
            downstreamImpact.ReportPackExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.High;
        }

        if (normalizedFieldPath.Contains("Identifier", StringComparison.OrdinalIgnoreCase) &&
            downstreamImpact.ReconciliationExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.High;
        }

        if (normalizedFieldPath.Contains("TradingParameters", StringComparison.OrdinalIgnoreCase) &&
            downstreamImpact.LedgerExposureCount > 0)
        {
            return SecurityMasterImpactSeverity.Medium;
        }

        return downstreamImpact.Severity;
    }

    private static string BuildImpactSummary(
        int matchedRunCount,
        int portfolioExposureCount,
        int ledgerExposureCount,
        int reconciliationExposureCount,
        int reportPackExposureCount,
        int reconciliationUnavailableRunCount)
    {
        if (portfolioExposureCount == 0 &&
            ledgerExposureCount == 0 &&
            reconciliationExposureCount == 0 &&
            reportPackExposureCount == 0)
        {
            if (reconciliationUnavailableRunCount > 0)
            {
                return $"Reconciliation impact has not been materialized for {reconciliationUnavailableRunCount} of {matchedRunCount} scoped run(s).";
            }

            return $"No downstream exposure detected across {matchedRunCount} scoped run(s).";
        }

        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{matchedRunCount} scoped run(s) checked • " +
            $"portfolio {portfolioExposureCount}, ledger {ledgerExposureCount}, reconciliation {reconciliationExposureCount}, report pack {reportPackExposureCount}.");

        if (reconciliationUnavailableRunCount > 0)
        {
            summary += string.Create(
                CultureInfo.InvariantCulture,
                $" Reconciliation impact still needs refresh for {reconciliationUnavailableRunCount} scoped run(s).");
        }

        return summary;
    }

    private static string BuildConflictImpactSummary(
        string fieldPath,
        SecurityMasterImpactSeverity severity,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        if (severity == SecurityMasterImpactSeverity.Unknown)
        {
            return "Downstream impact is not scoped.";
        }

        return severity switch
        {
            SecurityMasterImpactSeverity.High =>
                $"{FormatFieldLabel(fieldPath)} is high impact because downstream Accounting and Reporting workflows already reference this security.",
            SecurityMasterImpactSeverity.Medium =>
                $"{FormatFieldLabel(fieldPath)} already feeds ledger-facing workflows.",
            SecurityMasterImpactSeverity.Low =>
                $"{FormatFieldLabel(fieldPath)} only affects low-risk scoped portfolio exposure today.",
            _ =>
                $"{FormatFieldLabel(fieldPath)} has no detected scoped downstream exposure."
        };
    }

    private static string BuildConflictImpactDetail(
        string fieldPath,
        SecurityMasterImpactSeverity severity,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        if (severity == SecurityMasterImpactSeverity.Unknown)
        {
            return $"{FormatFieldLabel(fieldPath)} cannot be bulk-resolved because no fund scope is active.";
        }

        return severity switch
        {
            SecurityMasterImpactSeverity.High =>
                $"{FormatFieldLabel(fieldPath)} should be reviewed manually before reconciliation or report-pack consumers ingest the change.",
            SecurityMasterImpactSeverity.Medium =>
                $"{FormatFieldLabel(fieldPath)} reaches ledger-facing workflows. Keep the resolution explicit and operator-reviewed.",
            SecurityMasterImpactSeverity.Low =>
                $"{FormatFieldLabel(fieldPath)} is limited to scoped portfolio posture and can participate in low-risk bulk assist when the recommendation is deterministic.",
            _ =>
                $"{FormatFieldLabel(fieldPath)} has no detected scoped downstream exposure."
        };
    }
}
