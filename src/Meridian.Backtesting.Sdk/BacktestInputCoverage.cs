using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;

namespace Meridian.Backtesting.Sdk;

/// <summary>Coverage observed from the exact inputs consumed by a native replay.</summary>
public sealed record BacktestInputCoverage(
    decimal RequiredSessionFraction,
    IReadOnlyList<BacktestInstrumentCoverage> Instruments,
    IReadOnlyList<BacktestDataRevision> Revisions)
{
    /// <summary>Counts of replayed bars by effective symbol and observed source.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BacktestSourceCount>? SourceMix { get; init; }

    /// <summary>Identity of the observed bar distribution, independent of replay order.</summary>
    [JsonIgnore]
    public string? SourceMixHash
    {
        get
        {
            if (SourceMix is not { Count: > 0 })
                return null;

            var canonical = new StringBuilder("meridian.backtest-source-mix.v1|");
            foreach (var row in SourceMix.OrderBy(static item => item.Symbol, StringComparer.Ordinal)
                         .ThenBy(static item => item.Source, StringComparer.Ordinal))
            {
                AppendField(canonical, row.Symbol);
                AppendField(canonical, row.Source);
                canonical.Append(row.Bars.ToString(CultureInfo.InvariantCulture)).Append('|');
            }

            return Sha256Digest.ComputeUtf8(canonical.ToString());
        }
    }

    private static void AppendField(StringBuilder builder, string value) =>
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');
}

public sealed record BacktestSourceCount(string Symbol, string Source, long Bars);

public sealed record BacktestInstrumentCoverage(
    string Instrument,
    string Status,
    string? Calendar,
    int ObservedSessions,
    int? ExpectedSessions,
    IReadOnlyList<DateOnly> MissingSessions);

public sealed record BacktestDataRevision(string RevisionId, string ContentHash, string ServingProvider, string Instrument, string Granularity);
