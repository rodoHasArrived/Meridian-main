using System;
using System.Collections.Generic;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Wpf.ViewModels;

public sealed record SecurityConflictLaneGroup(
    Guid SecurityId,
    string SecurityLabel,
    string SecurityIdentifier,
    string GroupSummary,
    string HighestSeverityLabel,
    string HighestSeverityTone,
    int SafeAutoResolveCount,
    IReadOnlyList<SecurityConflictLaneEntry> Conflicts)
{
    public int ConflictCount => Conflicts.Count;
}

public sealed record SecurityConflictLaneEntry(
    SecurityMasterConflict Conflict,
    string SecurityLabel,
    string SecurityIdentifier,
    string FieldLabel,
    string SeverityLabel,
    string SeverityTone,
    int SeverityRank,
    string ConfidenceLabel,
    string AutoResolveHint,
    string ImpactSummary,
    string ImpactDetail,
    string NextStepSummary,
    bool RoutesToFundReview,
    bool RoutesToReconciliation,
    bool RoutesToCashFlow,
    bool RoutesToReportPack,
    bool RequiresTradingBackfill,
    bool IsAutoResolveSafe);

public sealed record SecurityConflictSecurityContext(
    string DisplayName,
    string PrimaryIdentifier,
    string AssetClass);

public sealed record SecurityConflictAssessment(
    string SeverityLabel,
    string SeverityTone,
    int SeverityRank,
    string ConfidenceLabel,
    string AutoResolveHint,
    string ImpactSummary,
    string ImpactDetail,
    string NextStepSummary,
    bool RoutesToFundReview,
    bool RoutesToReconciliation,
    bool RoutesToCashFlow,
    bool RoutesToReportPack,
    bool RequiresTradingBackfill,
    bool IsAutoResolveSafe);
