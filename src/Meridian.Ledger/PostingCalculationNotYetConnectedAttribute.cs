namespace Meridian.Ledger;

/// <summary>
/// Declares that a posting calculation (a method that builds journal drafts or journal lines) is
/// finished but deliberately has no production caller yet. The structural test
/// <c>PostingCalculationConnectionTests</c> fails for any posting calculation that is neither
/// called from production source nor carries this marker, so an unconnected calculation is always
/// a recorded decision with a reason instead of an accident.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class PostingCalculationNotYetConnectedAttribute(string reason) : Attribute
{
    /// <summary>Why the calculation is not connected, and what it is waiting for.</summary>
    public string Reason { get; } = string.IsNullOrWhiteSpace(reason)
        ? throw new ArgumentException("A not-yet-connected posting calculation must state its reason.", nameof(reason))
        : reason.Trim();
}
