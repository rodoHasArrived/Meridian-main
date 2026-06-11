namespace Meridian.Wpf.Models;

public sealed record FundPrivateCapitalCloseLaneRow(
    string LaneId,
    string Label,
    string StatusLabel,
    string Summary,
    string EvidenceLabel,
    string RequiredActionsLabel,
    string SourceTarget,
    bool IsReady);

public sealed record FundPrivateCapitalNavSupportPackageRow(
    string PackageId,
    string Label,
    string StatusLabel,
    string Summary,
    string ShadowNavLabel,
    string EvidenceLabel,
    string ComponentLabel,
    string SourceTarget,
    bool IsReady);

public sealed record FundPrivateCapitalCloseApprovalRow(
    string ApprovalId,
    string StatusLabel,
    string OperatorLabel,
    string ReviewerLabel,
    string Rationale,
    string DecidedAtLabel,
    string EvidenceLabel,
    string SourceTarget);
