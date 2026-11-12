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

// Conflict assessment, winning-side resolution and the field-value readers it compares.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private static SecurityMasterConflictAssessmentDto AssessConflict(
        SecurityMasterConflict conflict,
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading,
        WinningSourceInfo? winningSource,
        SecurityMasterDownstreamImpactDto downstreamImpact)
    {
        var currentValue = ExtractCurrentFieldValue(conflict.FieldPath, detail, economic, trading);
        var winningSide = ResolveWinningSide(conflict, currentValue, winningSource);

        var currentWinningValue = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ValueB,
            _ => conflict.ValueA
        };
        var challengerValue = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ValueA,
            _ => conflict.ValueB
        };
        var currentWinningSource = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ProviderB,
            _ => conflict.ProviderA
        };
        var challengerSource = winningSide switch
        {
            ConflictSide.ProviderB => conflict.ProviderA,
            _ => conflict.ProviderB
        };
        var recommendation = BuildRecommendation(currentWinningValue, challengerValue);
        var recommendedResolution = recommendation switch
        {
            SecurityMasterConflictRecommendationKind.DismissAsEquivalent => "Dismiss",
            SecurityMasterConflictRecommendationKind.Challenger => winningSide == ConflictSide.ProviderB ? "AcceptA" : "AcceptB",
            SecurityMasterConflictRecommendationKind.PreserveWinner => winningSide == ConflictSide.ProviderB ? "AcceptB" : "AcceptA",
            _ => string.Empty
        };

        var impactSeverity = DetermineConflictImpactSeverity(conflict, downstreamImpact);
        var impactSummary = BuildConflictImpactSummary(conflict.FieldPath, impactSeverity, downstreamImpact);
        var impactDetail = BuildConflictImpactDetail(conflict.FieldPath, impactSeverity, downstreamImpact);
        var winnerBlank = string.IsNullOrWhiteSpace(currentWinningValue);
        var challengerPresent = !string.IsNullOrWhiteSpace(challengerValue);
        var isOpen = string.Equals(conflict.Status, "Open", StringComparison.OrdinalIgnoreCase);
        var isBulkEligible = isOpen
            && impactSeverity is SecurityMasterImpactSeverity.None or SecurityMasterImpactSeverity.Low
            && (recommendation == SecurityMasterConflictRecommendationKind.DismissAsEquivalent
                || (winnerBlank && challengerPresent));

        var bulkIneligibilityReason = isBulkEligible
            ? null
            : !isOpen
                ? "Conflict is not open."
                : recommendation == SecurityMasterConflictRecommendationKind.ManualReview
                    ? "Conflict requires manual review."
                    : impactSeverity is SecurityMasterImpactSeverity.Unknown
                        ? "Downstream impact is not scoped."
                        : $"Impact severity is {impactSeverity}.";

        var recommendedWinner = recommendation switch
        {
            SecurityMasterConflictRecommendationKind.DismissAsEquivalent =>
                $"{currentWinningSource} and {challengerSource} normalize to the same value.",
            SecurityMasterConflictRecommendationKind.Challenger =>
                $"Accept {challengerSource} because the current winning value is blank.",
            SecurityMasterConflictRecommendationKind.PreserveWinner =>
                $"Preserve {currentWinningSource} as the current winner.",
            _ =>
                $"Manual review required between {currentWinningSource} and {challengerSource}."
        };

        return new SecurityMasterConflictAssessmentDto(
            Conflict: conflict,
            CurrentWinningValue: currentWinningValue,
            ChallengerValue: challengerValue,
            CurrentWinningSource: currentWinningSource,
            ChallengerSource: challengerSource,
            Recommendation: recommendation,
            RecommendedResolution: recommendedResolution,
            RecommendedWinner: recommendedWinner,
            ImpactSeverity: impactSeverity,
            ImpactSummary: impactSummary,
            ImpactDetail: impactDetail,
            IsBulkEligible: isBulkEligible,
            BulkIneligibilityReason: bulkIneligibilityReason);
    }

    private static SecurityMasterConflictRecommendationKind BuildRecommendation(string? currentWinningValue, string? challengerValue)
    {
        if (AreEquivalent(currentWinningValue, challengerValue))
        {
            return SecurityMasterConflictRecommendationKind.DismissAsEquivalent;
        }

        if (string.IsNullOrWhiteSpace(currentWinningValue) && !string.IsNullOrWhiteSpace(challengerValue))
        {
            return SecurityMasterConflictRecommendationKind.Challenger;
        }

        if (!string.IsNullOrWhiteSpace(currentWinningValue) && !string.IsNullOrWhiteSpace(challengerValue))
        {
            return SecurityMasterConflictRecommendationKind.PreserveWinner;
        }

        return SecurityMasterConflictRecommendationKind.ManualReview;
    }

    private static ConflictSide ResolveWinningSide(
        SecurityMasterConflict conflict,
        string? currentValue,
        WinningSourceInfo? winningSource)
    {
        var normalizedCurrentValue = NormalizeComparableString(currentValue);
        if (!string.IsNullOrWhiteSpace(normalizedCurrentValue))
        {
            var matchesA = string.Equals(NormalizeComparableString(conflict.ValueA), normalizedCurrentValue, StringComparison.Ordinal);
            var matchesB = string.Equals(NormalizeComparableString(conflict.ValueB), normalizedCurrentValue, StringComparison.Ordinal);

            if (matchesA && !matchesB)
            {
                return ConflictSide.ProviderA;
            }

            if (matchesB && !matchesA)
            {
                return ConflictSide.ProviderB;
            }
        }

        if (!string.IsNullOrWhiteSpace(winningSource?.SourceSystem))
        {
            var sourceMatchesA = string.Equals(winningSource.SourceSystem, conflict.ProviderA, StringComparison.OrdinalIgnoreCase);
            var sourceMatchesB = string.Equals(winningSource.SourceSystem, conflict.ProviderB, StringComparison.OrdinalIgnoreCase);
            if (sourceMatchesA && !sourceMatchesB)
            {
                return ConflictSide.ProviderA;
            }

            if (sourceMatchesB && !sourceMatchesA)
            {
                return ConflictSide.ProviderB;
            }
        }

        return ConflictSide.ProviderA;
    }

    private static string? ExtractCurrentFieldValue(
        string fieldPath,
        SecurityDetailDto detail,
        SecurityEconomicDefinitionRecord? economic,
        TradingParametersDto? trading)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            return null;
        }

        var segments = fieldPath
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        if (segments[0].Equals("Identifiers", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length == 1 || segments[1].Equals("Primary", StringComparison.OrdinalIgnoreCase))
            {
                return detail.Identifiers.FirstOrDefault(static identifier => identifier.IsPrimary)?.Value
                    ?? detail.Identifiers.FirstOrDefault()?.Value;
            }

            var kindSegment = segments[1];
            var identifier = detail.Identifiers.FirstOrDefault(identifier =>
                identifier.Kind.ToString().Equals(kindSegment, StringComparison.OrdinalIgnoreCase));
            if (identifier is not null)
            {
                return identifier.Value;
            }

            var alias = detail.Aliases.FirstOrDefault(alias =>
                alias.AliasKind.Equals(kindSegment, StringComparison.OrdinalIgnoreCase));
            return alias?.AliasValue;
        }

        if (segments[0].Equals("TradingParameters", StringComparison.OrdinalIgnoreCase))
        {
            return segments.Last().ToLowerInvariant() switch
            {
                "lotsize" => FormatNullableDecimal(trading?.LotSize),
                "ticksize" => FormatNullableDecimal(trading?.TickSize),
                "contractmultiplier" => FormatNullableDecimal(trading?.ContractMultiplier),
                "marginrequirementpct" => FormatNullableDecimal(trading?.MarginRequirementPct),
                "tradinghoursutc" => trading?.TradingHoursUtc,
                "circuitbreakerthresholdpct" => FormatNullableDecimal(trading?.CircuitBreakerThresholdPct),
                _ => null
            };
        }

        if (segments[0].Equals("EconomicDefinition", StringComparison.OrdinalIgnoreCase))
        {
            return segments.Last().ToLowerInvariant() switch
            {
                "displayname" => detail.DisplayName,
                "currency" => detail.Currency,
                "assetclass" => detail.AssetClass,
                "assetfamily" => ClassificationLabelReader.Normalize(economic?.AssetFamily),
                "subtype" => ClassificationLabelReader.Normalize(economic?.SubType),
                "issuertype" => economic?.IssuerType,
                _ => null
            };
        }

        var roots = new List<JsonElement>(4);
        if (economic is not null)
        {
            roots.Add(economic.Classification);
            roots.Add(economic.CommonTerms);
            roots.Add(economic.EconomicTerms);
        }

        roots.Add(detail.CommonTerms);
        roots.Add(detail.AssetSpecificTerms);

        foreach (var root in roots)
        {
            if (TryReadJsonPath(root, segments, out var value) ||
                TryReadJsonPath(root, segments.Skip(1), out value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool TryReadJsonPath(
        JsonElement root,
        IEnumerable<string> segments,
        out string? value)
    {
        value = null;
        var current = root;
        var hasAny = false;

        foreach (var segment in segments)
        {
            hasAny = true;
            if (current.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (!TryGetPropertyCaseInsensitive(current, segment, out current))
            {
                return false;
            }
        }

        if (!hasAny)
        {
            return false;
        }

        value = current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => current.GetRawText()
        };

        return true;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeComparableString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (character is ' ' or '-' or '/' or '.' or '_')
            {
                continue;
            }

            builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }

    private static bool AreEquivalent(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(
               NormalizeComparableString(left),
               NormalizeComparableString(right),
               StringComparison.Ordinal);

    private static HashSet<string> BuildNormalizedIdentifierSet(SecurityDetailDto detail)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var identifier in detail.Identifiers)
        {
            identifiers.Add(NormalizeComparableString(SecurityIdentifierNormalizer.GetOrComputeNormalizedValue(identifier)));
        }

        foreach (var alias in detail.Aliases.Where(static alias => alias.IsEnabled))
        {
            identifiers.Add(NormalizeComparableString(SecurityIdentifierNormalizer.NormalizeAliasValue(alias.AliasKind, alias.AliasValue)));
        }

        identifiers.Add(NormalizeComparableString(detail.DisplayName));
        return identifiers;
    }

    private static bool MatchesSecurity(
        string? symbol,
        Guid? securityId,
        SecurityDetailDto detail,
        ISet<string> normalizedIdentifiers)
    {
        if (securityId.HasValue && securityId.Value == detail.SecurityId)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        return normalizedIdentifiers.Contains(NormalizeComparableString(symbol));
    }
}
