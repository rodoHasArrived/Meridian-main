using Meridian.Contracts.Operations;
using Meridian.Ledger;
using Meridian.Strategies.Models;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// W9-TRUTH-001: derives the data-provenance mark a report pack must carry from the evidence it
/// cites — the strategy runs behind its figures and the posted valuation entries behind its
/// balances. A pack citing any simulated, seeded, or sample input inherits the strongest non-real
/// provenance of those inputs, so a derived figure can never enter report-pack evidence without the
/// blocking mark. Unknown tokens degrade to "simulated", never to real.
/// </summary>
/// <remarks>
/// Valuation entries are consulted because a pack can be built entirely from fabricated marks while
/// citing no simulated strategy run at all: a NAV priced off a synthetic provider posts real-looking
/// journal entries, and deriving provenance from runs alone would report that pack as real.
/// </remarks>
public static class ReportPackProvenanceResolver
{
    public static string? ResolveDerivedToken(IReadOnlyList<StrategyRunEntry> runs)
        => ResolveDerivedToken(runs, []);

    /// <summary>
    /// Derives the pack's mark from both evidence lanes.
    /// </summary>
    /// <param name="runs">Strategy runs the pack cites.</param>
    /// <param name="valuationEntries">
    /// Posted journal entries backing the pack's balances. Their
    /// <see cref="ValuationProvenanceTag"/> declares whether the marks behind them were observed or
    /// fabricated.
    /// </param>
    /// <returns>The provenance token to carry, or <c>null</c> when every input is real.</returns>
    public static string? ResolveDerivedToken(
        IReadOnlyList<StrategyRunEntry> runs,
        IReadOnlyList<JournalEntry> valuationEntries)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(valuationEntries);

        var strongest = DataProvenanceExtensions.Strongest(
            runs
                .Where(static run => !string.IsNullOrWhiteSpace(run.DataProvenanceToken))
                .Select(static run => DataProvenanceExtensions.ParseTokenOrSimulated(run.DataProvenanceToken))
                .Append(ValuationProvenanceTag.Strongest(valuationEntries)));

        return strongest.IsNonReal() ? strongest.Token() : null;
    }
}
