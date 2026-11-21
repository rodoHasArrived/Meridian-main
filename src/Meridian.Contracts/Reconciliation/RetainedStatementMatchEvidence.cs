namespace Meridian.Contracts.Reconciliation;

/// <summary>The exact tolerance policy used by the run, effective for the recorded statement date.</summary>
public sealed record ExecutedStatementTolerancePolicy(string ProfileId, int Version, DateOnly EffectiveDate, string ComparisonPolicyFingerprint);

/// <summary>One source or ledger record's signed contribution in the units supplied to the matcher.</summary>
public sealed record StatementMatchAllocation(
    string RecordId,
    string EvidenceReference,
    string? Instrument,
    DateOnly? RecordDate,
    DateOnly? SettlementDate,
    string? Quantity,
    string? Amount,
    string? Currency,
    string? UnitPrice = null,
    string? TransactionType = null,
    string? ExternalTransactionId = null);

/// <summary>Exact comparison in one unit. Numeric values are invariant decimal text throughout the read contract.</summary>
public sealed record StatementMatchResidual(
    string Measure,
    string Unit,
    string SourceTotal,
    string LedgerTotal,
    string Residual,
    string? Tolerance);

/// <summary>A single result from the matcher, with its rule basis and the records it actually evaluated.</summary>
public sealed record RetainedStatementMatchEvidence(
    string Tier,
    string Explanation,
    string? Confidence,
    string? AmountMeasure,
    IReadOnlyList<string> RuleIds,
    IReadOnlyList<StatementMatchAllocation> SourceAllocations,
    IReadOnlyList<StatementMatchAllocation> LedgerAllocations,
    IReadOnlyList<StatementMatchResidual> Residuals);

/// <summary>Case evidence from the immutable match artifact of the originating run.</summary>
public sealed record RetainedStatementCaseEvidence(
    string RunId,
    string MatchArtifactSha256,
    ExecutedStatementTolerancePolicy TolerancePolicy,
    RetainedStatementMatchEvidence Match);
