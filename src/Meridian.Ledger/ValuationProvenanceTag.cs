using Meridian.Contracts.Operations;

namespace Meridian.Ledger;

/// <summary>
/// The journal tag that carries a fair-value draft's data provenance, and the reader that recovers
/// it from posted entries.
/// </summary>
/// <remarks>
/// Valuation is where fabricated market data enters the books: a daily mark priced off a synthetic
/// provider becomes a posted journal entry, and every figure derived from that entry — carrying
/// value, NAV, a report pack — inherits the fabrication without inheriting the disclosure. Tagging
/// the draft and reading the tag back is what lets a derived figure state its true origin instead
/// of defaulting to real.
/// </remarks>
public static class ValuationProvenanceTag
{
    /// <summary>Journal metadata tag key written by <see cref="DailyPortfolioPricingDraftBuilder"/>.</summary>
    public const string Key = "valuation.dataProvenance";

    /// <summary>
    /// Origin declared by one journal entry's valuation tag. An entry with no tag is not a
    /// valuation entry and contributes <see cref="DataProvenance.Real"/>; an entry carrying an
    /// unrecognized token degrades to <see cref="DataProvenance.Simulated"/> rather than to real.
    /// </summary>
    public static DataProvenance Read(JournalEntryMetadata? metadata)
    {
        if (metadata?.Tags is not { } tags ||
            !tags.TryGetValue(Key, out var token) ||
            string.IsNullOrWhiteSpace(token))
        {
            return DataProvenance.Real;
        }

        return DataProvenanceExtensions.ParseTokenOrSimulated(token);
    }

    /// <summary>
    /// Strongest non-real origin declared across a set of posted entries — the mark any figure
    /// derived from them must carry.
    /// </summary>
    public static DataProvenance Strongest(IEnumerable<JournalEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return DataProvenanceExtensions.Strongest(entries.Select(static entry => Read(entry.Metadata)));
    }
}
