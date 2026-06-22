using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Models;

public sealed record StatementReconciliationWorkbenchSnapshot(
    IReadOnlyList<StatementRunSummaryDto> StatementRuns,
    IReadOnlyList<StatementRunExceptionDto> ValidationIssues,
    IReadOnlyList<StatementBreakDto> UnresolvedBreaks,
    IReadOnlyList<ReconciliationCaseSummaryDto> OpenCases,
    IReadOnlyList<ReconciliationQueueAccountStatusDto> QueueStatuses,
    DateTimeOffset RefreshedAt);

public sealed record StatementRunWorkbenchRow(
    string RunId,
    string ImportId,
    string StartedAtText,
    string CompletedAtText,
    int PositionMatches,
    int CashMatches,
    int TransactionMatches,
    int OpenExceptionCount,
    string StatusText,
    WorkstationReadinessTone ReadinessTone,
    string Tone);

public sealed record StatementValidationIssueRow(
    string IssueId,
    string RunId,
    string ImportId,
    string SourceReference,
    string SeverityLabel,
    string CategoryLabel,
    string BreakCode,
    string DeltaText,
    string ToleranceText,
    string StatusLabel,
    string CreatedAtText,
    string RecommendedAction,
    string EvidenceLink);

public sealed record StatementUnresolvedBreakRow(
    string BreakId,
    string RunId,
    string StatementReference,
    string BreakTypeLabel,
    string SeverityLabel,
    string MatchTierLabel,
    string DeltaText,
    string ToleranceText,
    string StatusLabel,
    string OwnerLabel,
    string LastObservedText,
    string RecommendedAction,
    string EvidenceLink,
    string SlaLabel,
    string EscalationLabel,
    string EscalationReason,
    WorkstationReadinessTone ReadinessTone,
    string Tone);

public sealed record StatementCaseActionRow(
    string ActionId,
    string Label,
    string TargetRoute,
    string EvidenceLink,
    string OwnerLabel,
    string DisabledReason,
    bool IsEnabled,
    WorkstationReadinessTone ReadinessTone,
    string Tone);
