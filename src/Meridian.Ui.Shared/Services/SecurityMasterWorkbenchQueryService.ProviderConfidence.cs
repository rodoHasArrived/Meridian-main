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

// Provider confidence rows and provider mapping scoring.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private static IReadOnlyList<InstrumentPassportProviderConfidenceDto> BuildProviderConfidence(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterIdentifierSummaryDto identifierSummary,
        IReadOnlyList<SecurityMasterChangeHistoryItemDto> lifecycleEvents)
    {
        var overrideHistory = lifecycleEvents
            .Where(IsOverrideHistory)
            .OrderByDescending(static item => item.ChangedAtUtc)
            .ToArray();

        return DeduplicateProviderConfidenceMappings(identifierSummary.ProviderMappings)
            .Select(mapping =>
            {
                var relatedConflicts = snapshot.ConflictAssessments
                    .Where(assessment => IsRelatedProviderConflict(mapping, assessment))
                    .ToArray();
                var freshnessAsOf = ResolveProviderMappingFreshness(snapshot, mapping, lifecycleEvents);
                var confidenceScore = ScoreProviderMapping(mapping, snapshot.TrustPosture, relatedConflicts);
                return new InstrumentPassportProviderConfidenceDto(
                    Provider: string.IsNullOrWhiteSpace(mapping.Provider) ? "unknown-provider" : mapping.Provider,
                    ProviderSource: mapping.MappingSource,
                    MappingKind: mapping.MappingKind,
                    Symbol: mapping.Value,
                    NormalizedSymbol: mapping.NormalizedValue,
                    IsPrimary: mapping.IsPrimary,
                    IsActive: mapping.IsActive,
                    FreshnessAsOf: freshnessAsOf,
                    FreshnessMinutes: freshnessAsOf.HasValue
                        ? Math.Max(0, (int)(snapshot.RetrievedAtUtc - freshnessAsOf.Value).TotalMinutes)
                        : null,
                    ConfidenceScore: confidenceScore,
                    ConfidenceReason: BuildProviderMappingConfidenceReason(mapping, snapshot.TrustPosture, relatedConflicts),
                    IdentifierConflictIds: relatedConflicts.Select(static assessment => assessment.Conflict.ConflictId).ToArray(),
                    IdentifierConflictSummaries: relatedConflicts.Select(BuildProviderIdentifierConflictSummary).ToArray(),
                    OverrideHistory: overrideHistory);
            })
            .OrderByDescending(static item => item.IsPrimary)
            .ThenByDescending(static item => item.ConfidenceScore)
            .ThenBy(static item => item.Provider, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildProviderIdentifierConflictSummary(SecurityMasterConflictAssessmentDto assessment)
    {
        var conflict = assessment.Conflict;
        return $"Identifier conflict {conflict.FieldPath}: {conflict.ProviderA} '{conflict.ValueA}' versus {conflict.ProviderB} '{conflict.ValueB}'. {assessment.ImpactSummary}";
    }

    private static IReadOnlyList<SecurityMasterProviderSymbolMappingDto> DeduplicateProviderConfidenceMappings(
        IReadOnlyList<SecurityMasterProviderSymbolMappingDto> providerMappings)
    {
        return providerMappings
            .GroupBy(
                static mapping => string.Join(
                    "|",
                    string.IsNullOrWhiteSpace(mapping.NormalizedProvider)
                        ? SecurityIdentifierNormalizer.NormalizeProvider(mapping.Provider)
                        : mapping.NormalizedProvider,
                    string.IsNullOrWhiteSpace(mapping.NormalizedValue)
                        ? mapping.Value.Trim().ToUpperInvariant()
                        : mapping.NormalizedValue),
                StringComparer.Ordinal)
            .Select(static group => group
                .OrderByDescending(static mapping => mapping.IsActive && mapping.IsEnabled)
                .ThenByDescending(static mapping => mapping.IsPrimary)
                .ThenBy(static mapping => mapping.MappingSource.Equals("Identifier", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenByDescending(static mapping => mapping.ValidFrom)
                .First())
            .ToArray();
    }

    private static DateTimeOffset? ResolveProviderMappingFreshness(
        SecurityMasterTrustSnapshotDto snapshot,
        SecurityMasterProviderSymbolMappingDto mapping,
        IReadOnlyList<SecurityMasterChangeHistoryItemDto> lifecycleEvents)
    {
        var sourceCandidateAsOf = snapshot.ProvenanceCandidates
            .Where(candidate =>
                ProviderMatches(mapping.Provider, candidate.SourceSystem) ||
                ProviderMatches(mapping.NormalizedProvider, candidate.SourceSystem))
            .Select(static candidate => candidate.AsOf)
            .Where(static asOf => asOf.HasValue)
            .OrderByDescending(static asOf => asOf)
            .FirstOrDefault();
        if (sourceCandidateAsOf.HasValue)
        {
            return sourceCandidateAsOf;
        }

        var lifecycleAsOf = lifecycleEvents
            .Where(item =>
                Contains(item.SourceSystem, mapping.Provider) ||
                Contains(item.SourceSystem, mapping.NormalizedProvider) ||
                item.ChangedFields.Any(field => Contains(field, mapping.MappingKind) || Contains(field, mapping.Value)))
            .Select(static item => item.ChangedAtUtc)
            .OrderByDescending(static item => item)
            .FirstOrDefault();

        return lifecycleAsOf == default ? mapping.ValidFrom : lifecycleAsOf;
    }

    private static decimal ScoreProviderMapping(
        SecurityMasterProviderSymbolMappingDto mapping,
        SecurityMasterTrustPostureDto trustPosture,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> relatedConflicts)
    {
        if (!mapping.IsActive || !mapping.IsEnabled)
        {
            return 0m;
        }

        if (relatedConflicts.Count > 0)
        {
            return Math.Min(55m, Math.Max(25m, trustPosture.TrustScore * 0.5m));
        }

        if (trustPosture.HasOpenConflicts)
        {
            return Math.Min(80m, Math.Max(60m, trustPosture.TrustScore));
        }

        return mapping.IsPrimary
            ? Math.Min(100m, Math.Max(85m, trustPosture.TrustScore))
            : Math.Min(90m, Math.Max(70m, trustPosture.TrustScore - 5m));
    }

    private static string BuildProviderMappingConfidenceReason(
        SecurityMasterProviderSymbolMappingDto mapping,
        SecurityMasterTrustPostureDto trustPosture,
        IReadOnlyList<SecurityMasterConflictAssessmentDto> relatedConflicts)
    {
        if (!mapping.IsActive || !mapping.IsEnabled)
        {
            return "Provider mapping is inactive or disabled and cannot be trusted for workflow routing.";
        }

        if (relatedConflicts.Count > 0)
        {
            return $"{relatedConflicts.Count} open identifier conflict(s) involve this provider mapping.";
        }

        if (trustPosture.HasOpenConflicts)
        {
            return "Mapping is active, but the instrument still has unrelated open conflicts.";
        }

        return mapping.IsPrimary
            ? "Primary active provider mapping with no related open identifier conflicts."
            : "Active provider mapping with no related open identifier conflicts.";
    }

    private static bool IsRelatedProviderConflict(
        SecurityMasterProviderSymbolMappingDto mapping,
        SecurityMasterConflictAssessmentDto assessment)
    {
        var conflict = assessment.Conflict;
        return ProviderMatches(mapping.Provider, conflict.ProviderA) ||
               ProviderMatches(mapping.Provider, conflict.ProviderB) ||
               ProviderMatches(mapping.NormalizedProvider, conflict.ProviderA) ||
               ProviderMatches(mapping.NormalizedProvider, conflict.ProviderB) ||
               SecurityMasterConflictRouting.HasSegment(conflict.FieldPath, mapping.MappingKind) ||
               SecurityMasterConflictRouting.EndsWithValue(conflict.FieldPath, mapping.Value);
    }

    private static bool IsOverrideHistory(SecurityMasterChangeHistoryItemDto item) =>
        Contains(item.EventType, "Override") ||
        item.ChangedFields.Any(static field => Contains(field, "override")) ||
        Contains(item.Reason, "override");

    private static bool ProviderMatches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
