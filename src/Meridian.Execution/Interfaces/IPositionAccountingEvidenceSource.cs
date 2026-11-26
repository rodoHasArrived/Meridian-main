using Meridian.Contracts.Workstation;

namespace Meridian.Execution.Interfaces;

/// <summary>Optional native-book source of exact position and account facts, captured atomically.</summary>
public interface IPositionAccountingEvidenceSource
{
    PositionAccountingEvidenceSnapshot CapturePositionAccountingEvidence();
}

/// <summary>A retained population and its economic cutoff; absent partitions are not empty books.</summary>
public sealed record PositionAccountingEvidenceSnapshot(
    DateTimeOffset AsOf, IReadOnlyList<StrategyPositionAccountingEvidence> Positions)
{
    /// <summary>Exact account partitions actually covered by this capture, including empty ones.</summary>
    public IReadOnlyList<string> CoveredFinancialAccountIds { get; init; } = [];
    /// <summary>Account money and display positions copied in the same native-book capture.</summary>
    public IReadOnlyList<ExecutionAccountDetailSnapshot> Accounts { get; init; } = [];
}
