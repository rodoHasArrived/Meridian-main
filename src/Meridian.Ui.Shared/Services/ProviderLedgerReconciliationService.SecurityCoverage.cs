using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Operations;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;
using Meridian.ProviderSdk;
using Meridian.Storage.Archival;
using Meridian.Storage.SecurityMaster;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ProviderLedgerReconciliationService
{
    private async Task AddSecurityCoverageChecksAsync(
        Guid runId,
        BreakLifecycleContext lifecycle,
        FundAccountBrokerageSyncActivityDto providerProjection,
        string? requestedBy,
        List<ProviderLedgerReconciliationCheckDto> checks,
        List<ProviderLedgerReconciliationBreakDto> breaks,
        List<ProviderSecurityMasterPassportDto> securityMasterPassports,
        DateTimeOffset observedAt,
        int providerStaleAfterMinutes,
        CancellationToken ct)
    {
        var positions = providerProjection.Positions
            .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
            .GroupBy(static position => position.Symbol.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        IReadOnlyList<SecurityMasterConflict> openIdentifierConflicts = _securityMasterConflictService is null
            ? Array.Empty<SecurityMasterConflict>()
            : await _securityMasterConflictService.GetOpenConflictsAsync(ct).ConfigureAwait(false);

        foreach (var position in positions)
        {
            ct.ThrowIfCancellationRequested();
            var symbol = position.Symbol.Trim().ToUpperInvariant();
            var checkId = $"security-master:{symbol}";

            if (position.Security is not null)
            {
                var overrideHistory = await GetSecurityMasterOverrideHistoryAsync(position.Security.SecurityId, ct)
                    .ConfigureAwait(false);
                if (AddInactiveSecurityMasterBreak(
                    runId,
                    lifecycle,
                    providerProjection,
                    position,
                    position.Security,
                    checks,
                    breaks,
                    securityMasterPassports,
                    checkId,
                    symbol,
                    observedAt,
                    providerStaleAfterMinutes,
                    overrideHistory,
                    openIdentifierConflicts))
                {
                    continue;
                }

                securityMasterPassports.Add(BuildSecurityMasterPassport(
                    providerProjection,
                    position,
                    position.Security,
                    validation: null,
                    status: MapPassportStatus(position.Security),
                    confidenceScore: position.Security.IsInferredMatch ? 85m : 100m,
                    resolutionSource: "provider-position",
                    reason: "Provider position already carries a resolved Security Master reference.",
                    observedAt: observedAt,
                    providerStaleAfterMinutes: providerStaleAfterMinutes,
                    overrideHistory: overrideHistory,
                    openIdentifierConflicts: openIdentifierConflicts));
                AddMatched(
                    checks,
                    checkId,
                    $"Security Master identity for {symbol}",
                    ReconciliationBreakCategory.ClassificationGap,
                    "security-master",
                    "provider-sync",
                    null,
                    null,
                    "Provider position already carries a resolved Security Master reference.");
                continue;
            }

            var resolved = _securityReferenceLookup is null
                ? null
                : await _securityReferenceLookup
                    .GetByCanonicalAsync(
                        new SecurityReferenceLookupRequest(
                            IdentifierKind: SecurityIdentifierKind.Ticker.ToString(),
                            IdentifierValue: symbol,
                            Symbol: symbol,
                            Currency: position.Currency,
                            AssetClass: position.AssetClass,
                            Source: "provider-ledger-reconciliation"),
                        ct)
                    .ConfigureAwait(false);

            if (resolved is not null)
            {
                var overrideHistory = await GetSecurityMasterOverrideHistoryAsync(resolved.SecurityId, ct)
                    .ConfigureAwait(false);
                if (AddInactiveSecurityMasterBreak(
                    runId,
                    lifecycle,
                    providerProjection,
                    position,
                    resolved,
                    checks,
                    breaks,
                    securityMasterPassports,
                    checkId,
                    symbol,
                    observedAt,
                    providerStaleAfterMinutes,
                    overrideHistory,
                    openIdentifierConflicts))
                {
                    continue;
                }

                securityMasterPassports.Add(BuildSecurityMasterPassport(
                    providerProjection,
                    position,
                    resolved,
                    validation: null,
                    status: MapPassportStatus(resolved),
                    confidenceScore: resolved.IsInferredMatch ? 80m : 90m,
                    resolutionSource: "security-master-lookup",
                    reason: "Provider position resolved through the shared Security Master lookup.",
                    observedAt: observedAt,
                    providerStaleAfterMinutes: providerStaleAfterMinutes,
                    overrideHistory: overrideHistory,
                    openIdentifierConflicts: openIdentifierConflicts));
                AddMatched(
                    checks,
                    checkId,
                    $"Security Master identity for {symbol}",
                    ReconciliationBreakCategory.ClassificationGap,
                    "security-master",
                    "provider-sync",
                    null,
                    null,
                    "Provider position resolved through the shared Security Master lookup.");
                continue;
            }

            var code = "SM_PROVIDER_POSITION_SECURITY_UNRESOLVED";
            var reason = $"Provider position '{symbol}' could not be resolved to a Security Master record.";
            var severity = ReconciliationBreakSeverity.High;
            if (_securityValidationGate is not null)
            {
                var validation = await _securityValidationGate
                    .ValidateSymbolAsync(
                        symbol,
                        SecurityValidationWorkflowDto.ReconciliationBreakIntake,
                        workflowReference: runId.ToString("N"),
                        actor: string.IsNullOrWhiteSpace(requestedBy) ? DefaultActor : requestedBy.Trim(),
                        persistSnapshot: false,
                        ct)
                    .ConfigureAwait(false);

                if (validation.IsResolved && !validation.IsBlocked)
                {
                    var overrideHistory = await GetSecurityMasterOverrideHistoryAsync(validation.SecurityId, ct)
                        .ConfigureAwait(false);
                    securityMasterPassports.Add(BuildSecurityMasterPassport(
                        providerProjection,
                        position,
                        security: null,
                        validation: validation,
                        status: ProviderSecurityMasterPassportStatusDto.Resolved,
                        confidenceScore: 80m,
                        resolutionSource: "security-validation-gate",
                        reason: "Security Master validation accepted the provider position.",
                        observedAt: observedAt,
                        providerStaleAfterMinutes: providerStaleAfterMinutes,
                        overrideHistory: overrideHistory,
                        openIdentifierConflicts: openIdentifierConflicts));
                    AddMatched(
                        checks,
                        checkId,
                        $"Security Master identity for {symbol}",
                        ReconciliationBreakCategory.ClassificationGap,
                        "security-master",
                        "provider-sync",
                        null,
                        null,
                        "Security Master validation accepted the provider position.");
                    continue;
                }

                var issue = validation.Report.Issues.FirstOrDefault();
                if (issue is not null)
                {
                    code = issue.Code;
                    reason = $"Security Master validation {issue.Code}: {issue.Message}";
                    severity = MapSecurityValidationSeverity(issue.Severity);
                }

                securityMasterPassports.Add(BuildSecurityMasterPassport(
                    providerProjection,
                    position,
                    security: null,
                    validation: validation,
                    status: validation.IsBlocked || validation.Report.HasBlockingIssues
                        ? ProviderSecurityMasterPassportStatusDto.Blocked
                        : ProviderSecurityMasterPassportStatusDto.Unresolved,
                    confidenceScore: 0m,
                    resolutionSource: "security-validation-gate",
                    reason: reason,
                    observedAt: observedAt,
                    providerStaleAfterMinutes: providerStaleAfterMinutes,
                    overrideHistory: [],
                    openIdentifierConflicts: openIdentifierConflicts));
            }
            else
            {
                securityMasterPassports.Add(BuildSecurityMasterPassport(
                    providerProjection,
                    position,
                    security: null,
                    validation: null,
                    status: ProviderSecurityMasterPassportStatusDto.Unresolved,
                    confidenceScore: 0m,
                    resolutionSource: "unresolved",
                    reason: reason,
                    observedAt: observedAt,
                    providerStaleAfterMinutes: providerStaleAfterMinutes,
                    overrideHistory: [],
                    openIdentifierConflicts: openIdentifierConflicts));
            }

            AddBreak(
                runId,
                lifecycle,
                checks,
                breaks,
                checkId,
                $"Security Master identity for {symbol}",
                ProviderLedgerReconciliationCheckStatusDto.Break,
                code,
                ReconciliationBreakCategory.ClassificationGap,
                severity,
                "security-master",
                "provider-sync",
                null,
                null,
                reason,
                symbol,
                "/workstation/data/security-master");
        }
    }

    private static bool AddInactiveSecurityMasterBreak(
        Guid runId,
        BreakLifecycleContext lifecycle,
        FundAccountBrokerageSyncActivityDto providerProjection,
        FundAccountBrokeragePositionDto position,
        WorkstationSecurityReference security,
        List<ProviderLedgerReconciliationCheckDto> checks,
        List<ProviderLedgerReconciliationBreakDto> breaks,
        List<ProviderSecurityMasterPassportDto> securityMasterPassports,
        string checkId,
        string symbol,
        DateTimeOffset observedAt,
        int providerStaleAfterMinutes,
        IReadOnlyList<string> overrideHistory,
        IReadOnlyList<SecurityMasterConflict> openIdentifierConflicts)
    {
        if (security.Status == SecurityStatusDto.Active)
        {
            return false;
        }

        var reason = $"Security Master reference for provider position '{symbol}' is {security.Status}; active approved Security Master status is required for ledger posting and close readiness.";
        securityMasterPassports.Add(BuildSecurityMasterPassport(
            providerProjection,
            position,
            security,
            validation: null,
            status: ProviderSecurityMasterPassportStatusDto.Blocked,
            confidenceScore: 0m,
            resolutionSource: "security-master-status",
            reason: reason,
            observedAt: observedAt,
            providerStaleAfterMinutes: providerStaleAfterMinutes,
            overrideHistory: overrideHistory,
            openIdentifierConflicts: openIdentifierConflicts));

        AddBreak(
            runId,
            lifecycle,
            checks,
            breaks,
            checkId,
            $"Security Master identity for {symbol}",
            ProviderLedgerReconciliationCheckStatusDto.Blocked,
            "SM_SECURITY_NOT_ACTIVE",
            ReconciliationBreakCategory.ClassificationGap,
            ReconciliationBreakSeverity.Critical,
            "active-security-master",
            "provider-sync",
            null,
            null,
            reason,
            symbol,
            "/workstation/data/security-master");
        return true;
    }

    private static string BuildProviderCapabilityBlockReason(
        ProviderCapabilityKind capability,
        ProviderRouteResult result)
    {
        var reason = string.IsNullOrWhiteSpace(result.PolicyGate)
            ? $"Provider capability '{capability}' is not routable for provider-ledger reconciliation."
            : result.PolicyGate;
        var skipped = result.SkippedCandidates
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Take(3)
            .ToArray();

        return skipped.Length == 0
            ? reason
            : $"{reason} Skipped: {string.Join(" ", skipped)}";
    }

    private static ProviderSecurityMasterPassportDto BuildSecurityMasterPassport(
        FundAccountBrokerageSyncActivityDto providerProjection,
        FundAccountBrokeragePositionDto position,
        WorkstationSecurityReference? security,
        SecurityValidationGateResultDto? validation,
        ProviderSecurityMasterPassportStatusDto status,
        decimal confidenceScore,
        string resolutionSource,
        string reason,
        DateTimeOffset observedAt,
        int providerStaleAfterMinutes,
        IReadOnlyList<string> overrideHistory,
        IReadOnlyList<SecurityMasterConflict> openIdentifierConflicts)
    {
        var issues = validation?.Report.Issues ?? [];
        var securityId = security?.SecurityId ?? validation?.SecurityId;
        var openConflictSummaries = FormatOpenIdentifierConflicts(securityId, openIdentifierConflicts);
        var identifierConflicts = issues
            .Where(static issue =>
                issue.Code.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase)
                || issue.Title.Contains("conflict", StringComparison.OrdinalIgnoreCase)
                || issue.AffectedFields.Any(static field => field.Contains("identifier", StringComparison.OrdinalIgnoreCase)))
            .Select(static issue => issue.Code)
            .Concat(openConflictSummaries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var freshnessMinutes = Math.Max(0, (int)Math.Floor((observedAt - providerProjection.SyncedAt).TotalMinutes));
        var providerEvidenceStale = providerProjection.Status.IsStale || freshnessMinutes > providerStaleAfterMinutes;
        var issueCodes = issues
            .Select(static issue => issue.Code)
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Concat(providerEvidenceStale ? ["PROVIDER_EVIDENCE_STALE"] : [])
            .Concat(openConflictSummaries.Length > 0 ? ["SM_IDENTIFIER_CONFLICT"] : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var adjustedConfidenceScore = confidenceScore;
        if (providerEvidenceStale && adjustedConfidenceScore > 70m)
        {
            adjustedConfidenceScore = 70m;
        }

        if (openConflictSummaries.Length > 0 && adjustedConfidenceScore > 60m)
        {
            adjustedConfidenceScore = 60m;
        }

        var reasonParts = new List<string> { reason };
        if (providerEvidenceStale)
        {
            reasonParts.Add("Provider evidence is stale for this reconciliation run.");
        }

        if (openConflictSummaries.Length > 0)
        {
            reasonParts.Add($"{openConflictSummaries.Length} open Security Master identifier conflict(s) involve this resolved instrument.");
        }

        return new ProviderSecurityMasterPassportDto(
            Symbol: position.Symbol.Trim().ToUpperInvariant(),
            ProviderId: providerProjection.Link.ProviderId,
            ExternalAccountId: providerProjection.Link.ExternalAccountId,
            ProviderSyncedAt: providerProjection.SyncedAt,
            ProviderIsStale: providerEvidenceStale,
            AssetClass: position.AssetClass,
            Currency: position.Currency,
            PositionId: position.PositionId,
            SecurityId: securityId,
            SecurityDisplayName: security?.DisplayName,
            SecurityStatus: security?.Status,
            Status: status,
            ConfidenceScore: adjustedConfidenceScore,
            ResolutionSource: resolutionSource,
            IdentifierConflicts: identifierConflicts,
            ValidationIssueCodes: issueCodes,
            OverrideHistory: overrideHistory,
            ObservedAt: observedAt,
            FreshnessMinutes: freshnessMinutes,
            Reason: string.Join(" ", reasonParts));
    }

    private static string[] FormatOpenIdentifierConflicts(
        Guid? securityId,
        IReadOnlyList<SecurityMasterConflict> openIdentifierConflicts)
    {
        var resolvedSecurityId = securityId.GetValueOrDefault();
        if (resolvedSecurityId == Guid.Empty || openIdentifierConflicts.Count == 0)
        {
            return [];
        }

        return openIdentifierConflicts
            .Where(conflict =>
                conflict.SecurityId == resolvedSecurityId &&
                string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static conflict => conflict.DetectedAt)
            .Select(static conflict =>
                $"conflict={conflict.ConflictId:N}; kind={conflict.ConflictKind}; field={conflict.FieldPath}; providers={conflict.ProviderA}/{conflict.ProviderB}")
            .ToArray();
    }

    private async Task<IReadOnlyList<string>> GetSecurityMasterOverrideHistoryAsync(
        Guid? securityId,
        CancellationToken ct)
    {
        var resolvedSecurityId = securityId.GetValueOrDefault();
        if (_operatorOverridesStore is null || resolvedSecurityId == Guid.Empty)
        {
            return [];
        }

        var overrides = await _operatorOverridesStore.GetAsync(resolvedSecurityId, ct).ConfigureAwait(false);
        if (overrides is null || overrides.AuditTrail.Count == 0)
        {
            return [];
        }

        return overrides.AuditTrail
            .OrderByDescending(static entry => entry.OccurredAt)
            .Take(10)
            .Select(FormatOverrideHistory)
            .ToArray();
    }

    private static string FormatOverrideHistory(SecurityOverrideAuditEntryDto entry)
    {
        var parts = new List<string>
        {
            entry.EventType,
            entry.ApprovalStatus.ToString(),
            $"actor={entry.Actor}",
            $"at={entry.OccurredAt:O}"
        };

        if (!string.IsNullOrWhiteSpace(entry.Reviewer))
        {
            parts.Add($"reviewer={entry.Reviewer.Trim()}");
        }

        if (entry.ReviewedAt.HasValue)
        {
            parts.Add($"reviewedAt={entry.ReviewedAt.Value:O}");
        }

        if (!string.IsNullOrWhiteSpace(entry.ReasonCode))
        {
            parts.Add($"reason={entry.ReasonCode.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(entry.Comment))
        {
            parts.Add($"comment={entry.Comment.Trim()}");
        }

        return string.Join("; ", parts);
    }
}

