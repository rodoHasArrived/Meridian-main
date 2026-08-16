using Meridian.Contracts.Operations;
using Meridian.Strategies.Models;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// W9-TRUTH-001: derives the data-provenance mark a report pack must carry from the strategy runs
/// it cites. A pack that cites any simulated, seeded, or sample run inherits the strongest
/// non-real provenance of its inputs, so the derived figure can never enter report-pack evidence
/// without the blocking mark. Unknown tokens degrade to "simulated", never to real.
/// </summary>
public static class ReportPackProvenanceResolver
{
    public static string? ResolveDerivedToken(IReadOnlyList<StrategyRunEntry> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        DataProvenance? strongest = null;
        foreach (var run in runs)
        {
            if (string.IsNullOrWhiteSpace(run.DataProvenanceToken))
            {
                continue;
            }

            var declared = DataProvenanceExtensions.ParseTokenOrSimulated(run.DataProvenanceToken);
            if (!declared.IsNonReal())
            {
                continue;
            }

            if (declared == DataProvenance.Simulated)
            {
                return declared.Token();
            }

            if (strongest is null || declared < strongest)
            {
                strongest = declared;
            }
        }

        return strongest?.Token();
    }
}
