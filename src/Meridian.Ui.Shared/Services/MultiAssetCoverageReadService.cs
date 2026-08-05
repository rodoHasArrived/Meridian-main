using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

public interface IMultiAssetCoverageReadService
{
    Task<MultiAssetCoverageSummaryDto> GetCoverageAsync(
        string? fundAccountId,
        string? entity,
        string? assetClass,
        ReconciliationBreakQueueScope scope,
        CancellationToken ct = default);
}

public sealed class MultiAssetCoverageReadService : IMultiAssetCoverageReadService
{
    private readonly ISecurityMasterOperationalReadinessService _readinessService;
    private readonly ProviderLedgerReconciliationService? _providerLedgerReconciliation;
    private readonly FundAccountCloseReadinessService? _closeReadiness;

    public MultiAssetCoverageReadService(
        ISecurityMasterOperationalReadinessService readinessService,
        ProviderLedgerReconciliationService? providerLedgerReconciliation = null,
        FundAccountCloseReadinessService? closeReadiness = null)
    {
        _readinessService = readinessService;
        _providerLedgerReconciliation = providerLedgerReconciliation;
        _closeReadiness = closeReadiness;
    }

    public async Task<MultiAssetCoverageSummaryDto> GetCoverageAsync(
        string? fundAccountId,
        string? entity,
        string? assetClass,
        ReconciliationBreakQueueScope scope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var evidenceSnapshot = await BuildEvidenceSnapshotAsync(fundAccountId, scope, ct).ConfigureAwait(false);
        return await _readinessService.GetReadinessAsync(
                new SecurityMasterOperationalReadinessRequest(fundAccountId, entity, assetClass, evidenceSnapshot),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<SecurityMasterOperationalEvidenceSnapshot?> BuildEvidenceSnapshotAsync(
        string? fundAccountId,
        ReconciliationBreakQueueScope scope,
        CancellationToken ct)
    {
        if (!Guid.TryParse(fundAccountId, out var accountId))
        {
            return null;
        }

        var evidence = new List<SecurityMasterOperationalEvidenceItem>();
        var route = BuildRoute(UiApiRoutes.FundAccountBrokerageSyncReconciliationLatest, accountId);
        var latest = _providerLedgerReconciliation is null
            ? null
            : await _providerLedgerReconciliation.GetLatestAsync(accountId, scope, ct).ConfigureAwait(false);

        if (latest is not null)
        {
            foreach (var check in latest.Checks)
            {
                evidence.Add(new SecurityMasterOperationalEvidenceItem(
                    EvidenceId: $"check:{check.CheckId}",
                    Category: "Reconciliation",
                    EvidenceKind: EvidenceKindFromCheck(check),
                    Status: check.Status.ToString(),
                    Source: $"{check.ExpectedSource}/{check.ActualSource}",
                    Label: check.Label,
                    EvidenceRoute: route,
                    EvidenceLink: latest.Summary.DetailPath,
                    Reason: check.Reason));
            }

            foreach (var breakRow in latest.Breaks)
            {
                evidence.Add(new SecurityMasterOperationalEvidenceItem(
                    EvidenceId: $"break:{breakRow.BreakId}",
                    Category: "Reconciliation",
                    EvidenceKind: EvidenceKindFromBreak(breakRow),
                    Status: breakRow.SignOffState == ProviderLedgerReconciliationBreakSignOffStateDto.SignedOff
                        ? "Ready"
                        : breakRow.Severity is ReconciliationBreakSeverity.Critical or ReconciliationBreakSeverity.High
                            ? "Blocked"
                            : "ReviewRequired",
                    Source: $"{breakRow.ExpectedSource}/{breakRow.ActualSource}",
                    Label: breakRow.CheckId,
                    EvidenceRoute: breakRow.EvidenceLink ?? route,
                    EvidenceLink: breakRow.EvidenceLink ?? latest.Summary.DetailPath,
                    Reason: breakRow.Reason));
            }

            foreach (var passport in latest.SecurityMasterPassports ?? [])
            {
                evidence.Add(new SecurityMasterOperationalEvidenceItem(
                    EvidenceId: $"security-master-passport:{passport.Symbol}",
                    Category: "SecurityMaster",
                    EvidenceKind: "underlying security position identity provider evidence",
                    Status: passport.Status.ToString(),
                    Source: passport.ResolutionSource,
                    AssetClass: passport.AssetClass,
                    Label: passport.Symbol,
                    EvidenceRoute: UiApiRoutes.WorkstationSecurityMasterSearch,
                    EvidenceLink: latest.Summary.DetailPath,
                    Reason: passport.Reason));
            }

            if (latest.ShadowBookComparison is not null)
            {
                foreach (var line in latest.ShadowBookComparison.Lines)
                {
                    evidence.Add(new SecurityMasterOperationalEvidenceItem(
                        EvidenceId: $"shadow-book:{line.Dimension}",
                        Category: "ShadowBook",
                        EvidenceKind: EvidenceKindFromShadowBookLine(line),
                        Status: line.Status.ToString(),
                        Source: $"{line.InternalSource}/{line.ProviderSource}",
                        Label: line.Label,
                        EvidenceRoute: route,
                        EvidenceLink: latest.Summary.DetailPath,
                        Reason: line.Reason));
                }
            }

            if (latest.CorporateActionReadiness is not null)
            {
                foreach (var line in latest.CorporateActionReadiness.Lines)
                {
                    evidence.Add(new SecurityMasterOperationalEvidenceItem(
                        EvidenceId: $"corporate-action-line:{line.Dimension}",
                        Category: "ProviderEvidence",
                        EvidenceKind: $"{line.Dimension} {line.RequiredFeed}",
                        Status: line.Status.ToString(),
                        Source: line.EvidenceSource,
                        Label: line.Label,
                        EvidenceRoute: route,
                        EvidenceLink: latest.Summary.DetailPath,
                        Reason: line.Reason));
                }

                foreach (var candidate in latest.CorporateActionReadiness.EvidenceCandidates)
                {
                    evidence.Add(new SecurityMasterOperationalEvidenceItem(
                        EvidenceId: $"corporate-action-candidate:{candidate.CandidateId}",
                        Category: "ProviderEvidence",
                        EvidenceKind: $"{candidate.CandidateType} {candidate.RequiredFeed}",
                        Status: candidate.Status.ToString(),
                        Source: candidate.EvidenceSource,
                        AssetClass: CandidateAssetClass(candidate),
                        Label: candidate.Symbol ?? candidate.CandidateType,
                        EvidenceRoute: route,
                        EvidenceLink: latest.Summary.DetailPath,
                        Reason: candidate.Reason));
                }

                foreach (var feed in latest.CorporateActionReadiness.SecurityMasterScheduleFeeds)
                {
                    evidence.Add(new SecurityMasterOperationalEvidenceItem(
                        EvidenceId: $"security-master-schedule:{feed.FeedId}",
                        Category: "ProviderEvidence",
                        EvidenceKind: $"{feed.FeedKind} {feed.RequiredFeed}",
                        Status: feed.Status.ToString(),
                        Source: feed.EvidenceSource,
                        AssetClass: ScheduleAssetClass(feed),
                        Label: feed.Symbol ?? feed.FeedKind,
                        EvidenceRoute: route,
                        EvidenceLink: latest.Summary.DetailPath,
                        Reason: feed.Reason));
                }
            }

            foreach (var link in latest.EvidenceLinks)
            {
                evidence.Add(new SecurityMasterOperationalEvidenceItem(
                    EvidenceId: $"evidence-link:{link}",
                    Category: "ProviderEvidence",
                    EvidenceKind: "provider projection raw retained evidence",
                    Status: "Ready",
                    Source: "provider-sync",
                    EvidenceRoute: route,
                    EvidenceLink: link,
                    Reason: "Retained provider projection evidence is linked to the latest reconciliation detail."));
            }
        }

        var closeReadiness = _closeReadiness is null
            ? null
            : await _closeReadiness.GetAsync(accountId, scope, ct).ConfigureAwait(false);
        if (closeReadiness is not null)
        {
            foreach (var component in closeReadiness.Components)
            {
                evidence.Add(new SecurityMasterOperationalEvidenceItem(
                    EvidenceId: $"close-component:{component.Key}",
                    Category: "CloseReadiness",
                    EvidenceKind: $"close readiness {component.Key} {component.Label}",
                    Status: component.Status.ToString(),
                    Source: "FundAccountCloseReadinessService",
                    Label: component.Label,
                    EvidenceRoute: component.RouteHint,
                    EvidenceLink: component.EvidenceLink,
                    Reason: component.Reason));
            }

            foreach (var blocker in closeReadiness.Blockers)
            {
                evidence.Add(new SecurityMasterOperationalEvidenceItem(
                    EvidenceId: $"close-blocker:{blocker.Code}",
                    Category: "CloseReadiness",
                    EvidenceKind: $"close readiness blocker {blocker.Category} {blocker.Code}",
                    Status: string.Equals(blocker.Severity, "Critical", StringComparison.OrdinalIgnoreCase)
                        ? "Blocked"
                        : "ReviewRequired",
                    Source: "FundAccountCloseReadinessService",
                    Label: blocker.Code,
                    EvidenceRoute: blocker.RouteHint,
                    EvidenceLink: blocker.EvidenceLink,
                    Reason: blocker.Message));
            }
        }

        if (latest is null && closeReadiness is null)
        {
            return null;
        }

        return new SecurityMasterOperationalEvidenceSnapshot(
            latest?.Summary.ProviderId,
            latest?.Summary.ExternalAccountId,
            latest?.Summary.Status.ToString() ?? closeReadiness?.Status.ToString(),
            latest?.Summary.DetailPath,
            evidence);
    }

    private static string EvidenceKindFromCheck(ProviderLedgerReconciliationCheckDto check)
    {
        var id = check.CheckId;
        if (id.Contains("cash", StringComparison.OrdinalIgnoreCase))
        {
            return "cash";
        }

        if (id.Contains("market-value", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("securities-market-value", StringComparison.OrdinalIgnoreCase))
        {
            return "market value";
        }

        if (id.Contains("AccountPositions", StringComparison.OrdinalIgnoreCase) ||
            id.Contains("position", StringComparison.OrdinalIgnoreCase))
        {
            return "position quantity market value";
        }

        if (id.Contains("CorporateActions", StringComparison.OrdinalIgnoreCase))
        {
            return "corporate action";
        }

        if (id.Contains("FactorSchedule", StringComparison.OrdinalIgnoreCase))
        {
            return "factor schedule";
        }

        return check.Label;
    }

    private static string EvidenceKindFromBreak(ProviderLedgerReconciliationBreakDto breakRow)
    {
        var combined = $"{breakRow.CheckId} {breakRow.Code}";
        if (combined.Contains("FACTOR", StringComparison.OrdinalIgnoreCase))
        {
            return "factor schedule";
        }

        if (combined.Contains("CASH", StringComparison.OrdinalIgnoreCase))
        {
            return "cash";
        }

        if (combined.Contains("MARKET_VALUE", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("market-value", StringComparison.OrdinalIgnoreCase))
        {
            return "market value";
        }

        if (combined.Contains("POSITION", StringComparison.OrdinalIgnoreCase))
        {
            return "position quantity";
        }

        if (combined.Contains("SECURITY", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("SM_", StringComparison.OrdinalIgnoreCase))
        {
            return "Security Master identity provider evidence";
        }

        return breakRow.CheckId;
    }

    private static string EvidenceKindFromShadowBookLine(ProviderShadowBookComparisonLineDto line)
    {
        var dimension = line.Dimension;
        if (dimension.Contains("quantity", StringComparison.OrdinalIgnoreCase))
        {
            return "quantity position";
        }

        if (dimension.Contains("market-value", StringComparison.OrdinalIgnoreCase) ||
            dimension.Contains("market value", StringComparison.OrdinalIgnoreCase))
        {
            return "market value";
        }

        if (dimension.Contains("cash", StringComparison.OrdinalIgnoreCase))
        {
            return "cash settlement";
        }

        if (dimension.Contains("accrual", StringComparison.OrdinalIgnoreCase) ||
            dimension.Contains("income", StringComparison.OrdinalIgnoreCase))
        {
            return "accrual cash";
        }

        if (dimension.Contains("pnl", StringComparison.OrdinalIgnoreCase))
        {
            return "market value ledger P&L";
        }

        return line.Label;
    }

    private static string? CandidateAssetClass(ProviderCorporateActionEvidenceCandidateDto candidate)
    {
        var combined = $"{candidate.CandidateType} {candidate.RequiredFeed} {candidate.Symbol} {candidate.SecurityDisplayName} {candidate.Reason}";
        if (IsPrivateCreditEvidence(combined))
        {
            return "DirectLoan";
        }

        if (IsStructuredAssetEvidence(combined))
        {
            return "CustomAsset";
        }

        return candidate.CandidateType.Contains("Factor", StringComparison.OrdinalIgnoreCase)
            ? "Bond"
            : null;
    }

    private static string? ScheduleAssetClass(ProviderSecurityMasterScheduleFeedDto feed)
    {
        var combined = $"{feed.FeedKind} {feed.RequiredFeed} {feed.Symbol} {feed.LedgerEffectKind} {feed.Reason}";
        if (IsPrivateCreditEvidence(combined))
        {
            return "DirectLoan";
        }

        if (IsStructuredAssetEvidence(combined))
        {
            return "CustomAsset";
        }

        return feed.FeedKind.Contains("Loan", StringComparison.OrdinalIgnoreCase)
            ? "DirectLoan"
            : feed.FeedKind.Contains("Factor", StringComparison.OrdinalIgnoreCase) ||
              feed.RequiredFeed.Contains("factor", StringComparison.OrdinalIgnoreCase)
                ? "Bond"
                : null;
    }

    private static bool IsPrivateCreditEvidence(string value)
        => value.Contains("loan", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("private credit", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("privatecredit", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("borrower", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("covenant", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("unfunded", StringComparison.OrdinalIgnoreCase);

    private static bool IsStructuredAssetEvidence(string value)
        => value.Contains("mbs", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("abs", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("clo", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("cmbs", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("structured", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("servicer", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("trustee", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("warehouse", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("nav", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("capital call", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("distribution", StringComparison.OrdinalIgnoreCase) ||
           value.Contains("obligation", StringComparison.OrdinalIgnoreCase);

    private static string BuildRoute(string routeTemplate, Guid accountId)
        => routeTemplate.Replace("{accountId}", accountId.ToString("D"), StringComparison.OrdinalIgnoreCase);
}
