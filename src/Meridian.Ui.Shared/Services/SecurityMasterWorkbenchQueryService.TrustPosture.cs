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

// Trust posture, provenance candidates, recommended actions and the identifier summary.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private static SecurityMasterTrustPostureDto BuildTrustPosture(
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading,
        IReadOnlyList<CorporateActionDto> corporateActions,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> assessments,
        WinningSourceInfo? winningSource,
        SecurityValidationReportDto validationReport)
    {
        var missingTradingFields = GetMissingTradingParameterFields(economic?.AssetClass, trading);
        var openConflictCount = assessments.Count;
        var blockingValidationCount = validationReport.CriticalIssueCount + validationReport.ErrorIssueCount;
        var advisoryValidationCount = Math.Max(0, validationReport.Issues.Count(static issue => issue.Severity != SecurityValidationSeverityDto.Info) - blockingValidationCount);
        // Effective view only: amendments fold to their latest terms and cancelled actions
        // stop counting against trust, so the posture reflects what will actually happen.
        var upcomingCorporateActions = CorporateActionEffectiveStateProjector
            .ProjectEffectiveActions(corporateActions, DateTimeOffset.UtcNow)
            .Where(action => action.ExDate >= DateOnly.FromDateTime(DateTime.UtcNow))
            .OrderBy(static action => action.ExDate)
            .Take(5)
            .ToArray();
        var corporateActionsTrusted = upcomingCorporateActions.Length == 0;

        var tone = openConflictCount > 0 || blockingValidationCount > 0
            ? SecurityMasterTrustTone.Blocked
            : missingTradingFields.Count > 0 || !corporateActionsTrusted || advisoryValidationCount > 0
                ? SecurityMasterTrustTone.Review
                : SecurityMasterTrustTone.Trusted;

        var trustScore = Math.Clamp(
            100
            - (openConflictCount * 25)
            - (blockingValidationCount * 12)
            - (advisoryValidationCount * 3)
            - (missingTradingFields.Count * 10)
            - (upcomingCorporateActions.Length > 0 ? 10 : 0),
            0,
            100);

        var summaryParts = new List<string>(3);
        if (openConflictCount > 0)
        {
            summaryParts.Add($"{openConflictCount} open conflict{(openConflictCount == 1 ? string.Empty : "s")}");
        }

        if (blockingValidationCount > 0)
        {
            summaryParts.Add($"{blockingValidationCount} blocking validation issue{(blockingValidationCount == 1 ? string.Empty : "s")}");
        }

        if (advisoryValidationCount > 0)
        {
            summaryParts.Add($"{advisoryValidationCount} advisory validation issue{(advisoryValidationCount == 1 ? string.Empty : "s")}");
        }

        var summary = tone switch
        {
            SecurityMasterTrustTone.Blocked when summaryParts.Count > 0 =>
                $"Trust is blocked by {string.Join(" and ", summaryParts)}.",
            SecurityMasterTrustTone.Review when missingTradingFields.Count > 0 =>
                $"Golden copy is stable, but trading readiness is incomplete: {string.Join(", ", missingTradingFields)}.",
            SecurityMasterTrustTone.Review when advisoryValidationCount > 0 =>
                $"Golden copy is stable, but validation still reports {advisoryValidationCount} advisory issue{(advisoryValidationCount == 1 ? string.Empty : "s")}.",
            SecurityMasterTrustTone.Review =>
                "Golden copy is stable, but upcoming corporate actions still require operator review.",
            SecurityMasterTrustTone.Trusted =>
                "Golden copy is trusted for downstream Accounting and Reporting workflows.",
            _ =>
                "Trust posture is unavailable."
        };

        var corporateActionReadiness = upcomingCorporateActions.Length == 0
            ? "No upcoming corporate actions are scheduled in the current review window."
            : upcomingCorporateActions.Length == 1
                ? $"Upcoming {CorporateActionTypeDescriptorCatalog.Find(upcomingCorporateActions[0].EventType)?.DisplayName ?? upcomingCorporateActions[0].EventType} on {upcomingCorporateActions[0].ExDate:yyyy-MM-dd} should be reviewed before downstream close."
                : $"{upcomingCorporateActions.Length} upcoming corporate actions should be reviewed before downstream close.";

        return new SecurityMasterTrustPostureDto(
            Tone: tone,
            TrustScore: trustScore,
            Summary: summary,
            GoldenCopySource: winningSource?.SourceSystem ?? "Unknown source",
            GoldenCopyRule: "Preserve winner unless the current winner is blank or the values are equivalent.",
            TradingParametersStatus: missingTradingFields.Count == 0
                ? trading is null
                    ? "Trading parameters could not be confirmed from the query surface."
                    : $"Trading parameters complete as of {trading.AsOf.LocalDateTime:g}."
                : $"Trading parameters incomplete: missing {string.Join(", ", missingTradingFields)}.",
            CorporateActionReadiness: corporateActionReadiness,
            HasOpenConflicts: openConflictCount > 0,
            OpenConflictCount: openConflictCount,
            TradingParametersComplete: missingTradingFields.Count == 0,
            HasUpcomingCorporateActions: upcomingCorporateActions.Length > 0,
            CorporateActionsTrusted: corporateActionsTrusted);
    }

    private static IReadOnlyList<SecurityMasterSourceCandidateDto> BuildProvenanceCandidates(
        SecurityDetailDto detail,
        WinningSourceInfo? winningSource,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> assessments)
    {
        var candidates = new List<SecurityMasterSourceCandidateDto>(assessments.Count + 1);
        if (winningSource is not null)
        {
            candidates.Add(new SecurityMasterSourceCandidateDto(
                ConflictId: null,
                FieldPath: "EconomicDefinition",
                SourceSystem: winningSource.SourceSystem,
                DisplayValue: detail.DisplayName,
                IsWinningSource: true,
                AsOf: winningSource.AsOf,
                UpdatedBy: winningSource.UpdatedBy,
                Reason: winningSource.Reason,
                SourceRecordId: winningSource.SourceRecordId,
                ImpactSeverity: SecurityMasterImpactSeverity.None));
        }

        foreach (var assessment in assessments)
        {
            candidates.Add(new SecurityMasterSourceCandidateDto(
                ConflictId: assessment.Conflict.ConflictId,
                FieldPath: assessment.Conflict.FieldPath,
                SourceSystem: assessment.ChallengerSource,
                DisplayValue: assessment.ChallengerValue ?? string.Empty,
                IsWinningSource: false,
                AsOf: assessment.Conflict.DetectedAt,
                UpdatedBy: null,
                Reason: assessment.ImpactSummary,
                SourceRecordId: null,
                ImpactSeverity: assessment.ImpactSeverity));
        }

        return candidates;
    }

    private static IReadOnlyList<SecurityMasterRecommendedActionDto> BuildRecommendedActions(
        SecurityDetailDto detail,
        SecurityMasterTrustPostureDto trustPosture,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> assessments,
        SecurityMasterDownstreamImpactDto downstreamImpact,
        SecurityValidationReportDto validationReport)
    {
        var actions = new List<SecurityMasterRecommendedActionDto>();
        var selectedConflict = assessments.FirstOrDefault(static assessment =>
            string.Equals(assessment.Conflict.Status, "Open", StringComparison.OrdinalIgnoreCase));

        if (selectedConflict is not null)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.ResolveSelectedConflict,
                Title: $"Resolve {FormatFieldLabel(selectedConflict.Conflict.FieldPath)}",
                Detail: $"{selectedConflict.RecommendedWinner} {selectedConflict.ImpactSummary}",
                IsPrimary: true,
                IsEnabled: selectedConflict.Recommendation != SecurityMasterConflictRecommendationKind.ManualReview,
                ConflictId: selectedConflict.Conflict.ConflictId));
        }

        var bulkEligibleCount = assessments.Count(static assessment => assessment.IsBulkEligible);
        if (bulkEligibleCount > 0)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.BulkResolveLowRiskConflicts,
                Title: "Apply low-risk bulk resolutions",
                Detail: $"{bulkEligibleCount} conflict(s) qualify for low-risk bulk assist.",
                IsPrimary: selectedConflict is null,
                IsEnabled: true));
        }

        var missingTradingFields = GetMissingTradingParameterFields(detail.AssetClass, null);
        if (!trustPosture.TradingParametersComplete)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.BackfillTradingParameters,
                Title: "Backfill trading parameters",
                Detail: trustPosture.TradingParametersStatus,
                IsPrimary: false,
                IsEnabled: true));
        }

        if (!trustPosture.CorporateActionsTrusted)
        {
            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: SecurityMasterRecommendedActionKind.ReviewCorporateActions,
                Title: "Review corporate actions",
                Detail: trustPosture.CorporateActionReadiness,
                IsPrimary: false,
                IsEnabled: true));
        }

        foreach (var link in downstreamImpact.Links.OrderBy(GetImpactLinkOrder))
        {
            var kind = link.Target switch
            {
                "reconciliation" => SecurityMasterRecommendedActionKind.OpenReconciliationImpact,
                "ledger" => SecurityMasterRecommendedActionKind.OpenLedgerImpact,
                "reportPack" => SecurityMasterRecommendedActionKind.OpenReportPackImpact,
                "portfolio" => SecurityMasterRecommendedActionKind.OpenPortfolioImpact,
                _ => SecurityMasterRecommendedActionKind.RefreshTrustSnapshot
            };

            actions.Add(new SecurityMasterRecommendedActionDto(
                Kind: kind,
                Title: link.Label,
                Detail: link.Summary,
                IsPrimary: false,
                IsEnabled: link.IsActive,
                Target: link.Target));
        }

        actions.Add(new SecurityMasterRecommendedActionDto(
            Kind: SecurityMasterRecommendedActionKind.EditSelectedSecurity,
            Title: "Edit selected security",
            Detail: validationReport.HasBlockingIssues
                ? $"Resolve {validationReport.CriticalIssueCount + validationReport.ErrorIssueCount} blocking validation issue(s) before downstream workflows consume the golden copy."
                : validationReport.Issues.Any(static issue => issue.Severity != SecurityValidationSeverityDto.Info)
                    ? $"Review {validationReport.Issues.Count(static issue => issue.Severity != SecurityValidationSeverityDto.Info)} advisory validation issue(s) before promoting the selected security."
                    : "Make a governed amendment to the golden copy after completing triage.",
            IsPrimary: false,
            IsEnabled: true));

        return actions;
    }

    private static SecurityMasterIdentifierSummaryDto BuildIdentifierSummary(SecurityDetailDto detail)
    {
        var evaluatedAtUtc = DateTimeOffset.UtcNow;
        var activeIdentifiers = detail.Identifiers
            .Where(identifier => IsIdentifierActive(identifier.ValidFrom, identifier.ValidTo, evaluatedAtUtc))
            .ToArray();
        var activeAliases = detail.Aliases
            .Where(alias => alias.IsEnabled && IsIdentifierActive(alias.ValidFrom, alias.ValidTo, evaluatedAtUtc))
            .ToArray();
        var primaryIdentifier = activeIdentifiers.FirstOrDefault(static identifier => identifier.IsPrimary)
            ?? activeIdentifiers.FirstOrDefault();

        var providerMappings = new List<SecurityMasterProviderSymbolMappingDto>();
        providerMappings.AddRange(activeIdentifiers
            .Where(static identifier =>
                identifier.Kind == SecurityIdentifierKind.ProviderSymbol ||
                !string.IsNullOrWhiteSpace(identifier.Provider))
            .Select(identifier => new SecurityMasterProviderSymbolMappingDto(
                MappingSource: "Identifier",
                MappingKind: identifier.Kind.ToString(),
                Value: identifier.Value,
                NormalizedValue: SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier),
                Provider: identifier.Provider,
                NormalizedProvider: SecurityIdentifierNormalizer.GetOrComputeNormalizedProvider(identifier),
                IsPrimary: identifier.IsPrimary,
                IsEnabled: true,
                ValidFrom: identifier.ValidFrom,
                ValidTo: identifier.ValidTo,
                IsActive: true)));
        providerMappings.AddRange(activeAliases
            .Where(static alias => !string.IsNullOrWhiteSpace(alias.Provider))
            .Select(alias => new SecurityMasterProviderSymbolMappingDto(
                MappingSource: "Alias",
                MappingKind: alias.AliasKind,
                Value: alias.AliasValue,
                NormalizedValue: NormalizeAliasValue(alias),
                Provider: alias.Provider,
                NormalizedProvider: SecurityIdentifierNormalizer.NormalizeProvider(alias.Provider),
                IsPrimary: false,
                IsEnabled: alias.IsEnabled,
                ValidFrom: alias.ValidFrom,
                ValidTo: alias.ValidTo,
                IsActive: true)));

        var distinctProviderCount = providerMappings
            .Select(mapping => mapping.NormalizedProvider)
            .Where(static provider => !string.IsNullOrWhiteSpace(provider))
            .Distinct(StringComparer.Ordinal)
            .Count();

        var summary = $"{activeIdentifiers.Length} active identifier(s), {activeAliases.Length} active alias(es), {providerMappings.Count} provider mapping(s).";

        return new SecurityMasterIdentifierSummaryDto(
            PrimaryIdentifierKind: primaryIdentifier?.Kind.ToString(),
            PrimaryIdentifierValue: primaryIdentifier?.Value,
            ActiveIdentifierCount: activeIdentifiers.Length,
            ActiveAliasCount: activeAliases.Length,
            ProviderMappingCount: providerMappings.Count,
            DistinctProviderCount: distinctProviderCount,
            HasPrimaryIdentifier: primaryIdentifier is not null,
            HasProviderMappings: providerMappings.Count > 0,
            Summary: summary,
            ProviderMappings: providerMappings
                .OrderBy(mapping => mapping.MappingSource, StringComparer.Ordinal)
                .ThenBy(mapping => mapping.MappingKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(mapping => mapping.NormalizedProvider, StringComparer.Ordinal)
                .ThenBy(mapping => mapping.NormalizedValue, StringComparer.Ordinal)
                .ToArray());
    }

    private static IReadOnlyList<string> GetMissingTradingParameterFields(string? assetClass, TradingParametersDto? trading)
    {
        var missingFields = new List<string>(4);

        if (trading?.LotSize is null or <= 0)
        {
            missingFields.Add("lot size");
        }

        if (trading?.TickSize is null or <= 0)
        {
            missingFields.Add("tick size");
        }

        if (string.IsNullOrWhiteSpace(trading?.TradingHoursUtc))
        {
            missingFields.Add("trading hours");
        }

        if (RequiresContractMultiplier(assetClass) && trading?.ContractMultiplier is null or <= 0)
        {
            missingFields.Add("contract multiplier");
        }

        return missingFields;
    }

    // A contract multiplier is a trading fact exactly where the catalog sizes lots in contracts or
    // declares a multiplier term (listed options, futures, warrants, commodity contracts).
    private static bool RequiresContractMultiplier(string? assetClass)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass) is { } descriptor
           && (descriptor.LotUnit == SecurityLotUnitKind.Contracts || descriptor.LotMultiplierTermKey is not null);

    private static string FormatFieldLabel(string? fieldPath)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return "Field";
        }

        var raw = fieldPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Last();
        var builder = new StringBuilder(raw.Length + 8);
        for (var index = 0; index < raw.Length; index++)
        {
            var character = raw[index];
            if (index > 0 && char.IsUpper(character) && !char.IsUpper(raw[index - 1]))
            {
                builder.Append(' ');
            }

            builder.Append(character);
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(builder.ToString().Replace('_', ' ').ToLowerInvariant());
    }
}
