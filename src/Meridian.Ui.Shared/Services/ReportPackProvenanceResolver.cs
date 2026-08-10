using Meridian.Contracts.Operations;
using Meridian.Ledger;
using Meridian.Strategies.Models;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// W9-TRUTH-001: derives the data-provenance mark a report pack must carry from the inputs used to
/// construct it: retained journal tags for durable-ledger reports, or strategy-run lineage for the
/// legacy run-backed path. A pack inherits the strongest non-real provenance of its inputs, so the
/// derived figure can never enter report-pack evidence without the blocking mark. Unknown tokens
/// degrade to "simulated", never to real.
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

    public static string? ResolveDerivedToken(IReadOnlyList<JournalEntry> journalEntries)
    {
        ArgumentNullException.ThrowIfNull(journalEntries);

        return ResolveStrongestToken(journalEntries.Select(static entry =>
            entry.Metadata.Tags is not null
            && entry.Metadata.Tags.TryGetValue("dataProvenance", out var token)
                ? token
                : null));
    }

    private static string? ResolveStrongestToken(IEnumerable<string?> tokens)
    {
        DataProvenance? strongest = null;
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var declared = DataProvenanceExtensions.ParseTokenOrSimulated(token);
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
