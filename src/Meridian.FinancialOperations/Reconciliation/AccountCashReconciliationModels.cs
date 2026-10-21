using System.Text.Json.Serialization;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed record AccountCashReconciliationScope(string TenantId, string CompanyId, string AccountId, Guid LedgerBookId);

/// <summary>The original row payload remains independent of its normalized comparison amounts.</summary>
public sealed record AccountCashBalanceSourceRow(NormalizedCashEntry Row, string OriginalPayload);

public sealed record AccountCashBalanceSource(
    string SourceId,
    ReconciliationSourceType SourceType,
    string Version,
    string EvidenceReference,
    DateTimeOffset CapturedAtUtc,
    bool CompletePopulation,
    IReadOnlyList<AccountCashBalanceSourceRow> Rows);

public sealed record AccountCashReconciliationRequest(
    string RunId,
    AccountCashReconciliationScope Scope,
    DateOnly BusinessDate,
    DateTimeOffset ObservedAtUtc,
    AccountCashBalanceSource Left,
    AccountCashBalanceSource Right,
    StatementToleranceProfile Tolerance,
    string InitialOwner);

public sealed record AccountCashComparison(
    string Currency,
    string ObservationState,
    IReadOnlyList<MatchGroup> Matches,
    IReadOnlyList<BreakRecord> Breaks,
    IReadOnlyList<MatchEvidence> Evidence);

public sealed record AccountCashReconciliationRun(
    AccountCashReconciliationRequest Input,
    string InputHash,
    IReadOnlyList<AccountCashComparison> Comparisons,
    string ResultHash);

public sealed record AccountCashCaseObservation(string RunId, DateTimeOffset ObservedAtUtc, string State, string ResultHash);
public sealed record AccountCashCaseAssignment(string Owner, string Actor, string Reason, DateTimeOffset AssignedAtUtc);

/// <summary>Source resolution is separate from governed sign-off and accounting close authority.</summary>
public sealed record AccountCashReconciliationCase(
    string CaseId,
    AccountCashReconciliationScope Scope,
    string LeftSourceId,
    string RightSourceId,
    string Currency,
    string Owner,
    int Version,
    DateTimeOffset FirstObservedAtUtc,
    DateTimeOffset LastObservedAtUtc,
    string State,
    DateTimeOffset? ResolvedAtUtc,
    string? ResolutionRunId,
    IReadOnlyList<AccountCashCaseObservation> History,
    IReadOnlyList<AccountCashCaseAssignment> Assignments)
{
    public int AgingDays => Math.Max(0, ((ResolvedAtUtc ?? LastObservedAtUtc).UtcDateTime.Date - FirstObservedAtUtc.UtcDateTime.Date).Days);
}

internal sealed record AccountCashReconciliationDocument(
    int SchemaVersion,
    IReadOnlyList<AccountCashReconciliationRun> Runs,
    IReadOnlyList<AccountCashReconciliationCase> Cases);
/// <summary>Legacy single-file layout, read only for the lossless split into scope files.</summary>
internal sealed record AccountCashReconciliationEnvelope(string ContentHash, AccountCashReconciliationDocument Document);
internal sealed record AccountCashReconciliationScopeEnvelope(string ContentHash, AccountCashReconciliationScope Scope, AccountCashReconciliationDocument Document);
internal sealed record AccountCashReconciliationScopeIndex(int SchemaVersion, string ContentHash, AccountCashReconciliationScope[] Scopes);

[JsonSerializable(typeof(AccountCashReconciliationRequest))]
[JsonSerializable(typeof(AccountCashReconciliationRun))]
[JsonSerializable(typeof(AccountCashReconciliationEnvelope))]
[JsonSerializable(typeof(AccountCashReconciliationScopeEnvelope))]
[JsonSerializable(typeof(AccountCashReconciliationScopeIndex))]
[JsonSerializable(typeof(AccountCashReconciliationScope))]
[JsonSerializable(typeof(AccountCashReconciliationScope[]))]
[JsonSerializable(typeof(AccountCashReconciliationDocument))]
[JsonSerializable(typeof(AccountCashComparison[]))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class AccountCashReconciliationJsonContext : JsonSerializerContext;
