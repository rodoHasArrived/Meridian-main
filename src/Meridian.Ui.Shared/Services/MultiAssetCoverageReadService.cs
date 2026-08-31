using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
// Fully qualified at each use: Meridian.Contracts.SecurityMaster also declares an
// ISecurityMasterQueryService, so importing that namespace here would make the name ambiguous.
using SecurityAssetClassCatalog = Meridian.Contracts.SecurityMaster.SecurityAssetClassCatalog;

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
    private readonly ISecurityMasterQueryService? _securityMasterQuery;

    public MultiAssetCoverageReadService(
        ISecurityMasterOperationalReadinessService readinessService,
        ProviderLedgerReconciliationService? providerLedgerReconciliation = null,
        FundAccountCloseReadinessService? closeReadiness = null,
        ISecurityMasterQueryService? securityMasterQuery = null)
    {
        _readinessService = readinessService;
        _providerLedgerReconciliation = providerLedgerReconciliation;
        _closeReadiness = closeReadiness;
        _securityMasterQuery = securityMasterQuery;
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

                // Resolve the asset class the referenced securities actually DECLARE before falling
                // back to inferring one from the evidence's own description.
                var declaredAssetClasses = await ResolveDeclaredAssetClassesAsync(
                        latest.CorporateActionReadiness.EvidenceCandidates
                            .Select(static candidate => candidate.SecurityId)
                            .Concat(latest.CorporateActionReadiness.SecurityMasterScheduleFeeds
                                .Select(static feed => feed.SecurityId)),
                        ct)
                    .ConfigureAwait(false);

                foreach (var candidate in latest.CorporateActionReadiness.EvidenceCandidates)
                {
                    evidence.Add(new SecurityMasterOperationalEvidenceItem(
                        EvidenceId: $"corporate-action-candidate:{candidate.CandidateId}",
                        Category: "ProviderEvidence",
                        EvidenceKind: $"{candidate.CandidateType} {candidate.RequiredFeed}",
                        Status: candidate.Status.ToString(),
                        Source: candidate.EvidenceSource,
                        AssetClass: CandidateAssetClass(candidate, declaredAssetClasses),
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
                        AssetClass: ScheduleAssetClass(feed, declaredAssetClasses),
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

    /// <summary>
    /// Resolves the asset class each referenced security DECLARES, keyed by security id. Evidence
    /// rows that name a security get their real class from the Security Master instead of one
    /// guessed from the evidence text. Securities that cannot be read (or an unavailable query
    /// service) are simply absent, and the caller falls back to the evidence-shape inference.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, string>> ResolveDeclaredAssetClassesAsync(
        IEnumerable<Guid?> securityIds,
        CancellationToken ct)
    {
        if (_securityMasterQuery is null)
        {
            return new Dictionary<Guid, string>();
        }

        var declaredAssetClasses = new Dictionary<Guid, string>();
        foreach (var securityId in securityIds
                     .Where(static securityId => securityId is { } value && value != Guid.Empty)
                     .Select(static securityId => securityId!.Value)
                     .Distinct())
        {
            var detail = await _securityMasterQuery.GetByIdAsync(securityId, ct).ConfigureAwait(false);
            if (detail is not null && !string.IsNullOrWhiteSpace(detail.AssetClass))
            {
                // Normalize vendor spellings onto the canonical catalog name; an unrecognized class
                // is still the record's own declared value, so it is kept rather than dropped.
                var descriptor = SecurityAssetClassCatalog.GetOrDefault(detail.AssetClass);
                declaredAssetClasses[securityId] = descriptor.AssetClass == "Unknown"
                    ? detail.AssetClass.Trim()
                    : descriptor.AssetClass;
            }
        }

        return declaredAssetClasses;
    }

    private static string? CandidateAssetClass(
        ProviderCorporateActionEvidenceCandidateDto candidate,
        IReadOnlyDictionary<Guid, string> declaredAssetClasses)
        => ResolveDeclared(candidate.SecurityId, declaredAssetClasses)
           ?? InferAssetClassFromEvidenceShape($"{candidate.CandidateType} {candidate.RequiredFeed}");

    private static string? ScheduleAssetClass(
        ProviderSecurityMasterScheduleFeedDto feed,
        IReadOnlyDictionary<Guid, string> declaredAssetClasses)
        => ResolveDeclared(feed.SecurityId, declaredAssetClasses)
           ?? InferAssetClassFromEvidenceShape($"{feed.FeedKind} {feed.RequiredFeed} {feed.LedgerEffectKind}");

    private static string? ResolveDeclared(
        Guid? securityId,
        IReadOnlyDictionary<Guid, string> declaredAssetClasses)
        => securityId is { } id && declaredAssetClasses.TryGetValue(id, out var declared) ? declared : null;

    /// <summary>
    /// Last-resort classification for evidence that names no security: reads the SHAPE of the feed —
    /// its candidate/feed kind, required feed, and ledger effect — and never the security's symbol,
    /// display name, or free-text reason. Those carry operator prose, and matching them classified a
    /// security by what its name happened to contain.
    /// <para>
    /// Structured-asset evidence resolves to <c>StructuredCredit</c>, the canonical securitized home
    /// under ADR-022 — not <c>CustomAsset</c>, which the asset-class validator now warns against for
    /// securitized products (<c>SM_CUSTOM_ASSET_SECURITIZED_NONCANONICAL</c>).
    /// </para>
    /// </summary>
    private static string? InferAssetClassFromEvidenceShape(string evidenceShape)
    {
        if (ContainsAny(evidenceShape, "loan", "private credit", "privatecredit", "borrower", "covenant", "unfunded"))
        {
            return "DirectLoan";
        }

        if (ContainsAny(evidenceShape, "mbs", "abs", "clo", "cmbs", "structured", "servicer", "trustee", "warehouse"))
        {
            return "StructuredCredit";
        }

        // NAV, capital-call and distribution feeds are private-fund lifecycle evidence; the previous
        // resolver swept them into CustomAsset alongside securitized evidence.
        if (ContainsAny(evidenceShape, "nav", "capital call", "capitalcall", "distribution"))
        {
            return "PrivateFundInterest";
        }

        return ContainsAny(evidenceShape, "factor") ? "Bond" : null;
    }

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string BuildRoute(string routeTemplate, Guid accountId)
        => routeTemplate.Replace("{accountId}", accountId.ToString("D"), StringComparison.OrdinalIgnoreCase);
}
