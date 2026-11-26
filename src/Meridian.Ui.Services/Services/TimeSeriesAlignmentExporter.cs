using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Api;
using Meridian.Storage.Archival;
using Meridian.Storage.Export;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Meridian.Ui.Services;

/// <summary>Serializes a bounded result and publishes it on the operator workstation.</summary>
internal static class TimeSeriesAlignmentExporter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] ProvenanceColumns =
    [
        "provider", "symbol", "session_date", "timestamp", "adjustment_basis", "value_status", "source_ids_json",
        "content_hash", "calendar", "timezone", "timestamp_rule", "input_kind", "interval", "gap_strategy", "max_gap_intervals",
        "missing_value_rule", "adjustment_policy", "duplicate_policy"
    ];

    internal static async Task WriteAsync(TimeSeriesAlignmentExecution result, ExportFormat format, string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bytes = format switch
        {
            ExportFormat.Csv => FormatCsv(result, ct),
            ExportFormat.JsonLines => FormatJsonLines(result, ct),
            ExportFormat.Parquet => await FormatParquetAsync(result, ct).ConfigureAwait(false),
            _ => throw new ArgumentException("Choose CSV, JSON Lines or Parquet.")
        };
        ct.ThrowIfCancellationRequested();
        await AtomicFileWriter.WriteAsync(path, bytes, ct).ConfigureAwait(false);
    }

    private static byte[] FormatCsv(TimeSeriesAlignmentExecution result, CancellationToken ct)
    {
        var text = new StringBuilder();
        text.AppendJoin(',', Columns(result.Summary)).Append('\n');
        foreach (var row in result.Rows)
        {
            ct.ThrowIfCancellationRequested();
            text.AppendJoin(',', Cells(row, result.Summary).Select(SpreadsheetFormulaGuard.EscapeCsvCell)).Append('\n');
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static byte[] FormatJsonLines(TimeSeriesAlignmentExecution result, CancellationToken ct)
    {
        var text = new StringBuilder();
        text.Append(JsonSerializer.Serialize(new { type = "alignment", result.Summary }, Json)).Append('\n');
        foreach (var row in result.Rows)
        {
            ct.ThrowIfCancellationRequested();
            text.Append(JsonSerializer.Serialize(row, Json)).Append('\n');
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static async Task<byte[]> FormatParquetAsync(TimeSeriesAlignmentExecution result, CancellationToken ct)
    {
        // Invariant decimal text preserves the full precision of prices and interpolation.
        var columns = Columns(result.Summary).Select(name => new DataField<string>(name)).ToArray();
        using var output = new MemoryStream();
        using (var writer = await ParquetWriter.CreateAsync(new ParquetSchema(columns), output, cancellationToken: ct).ConfigureAwait(false))
        {
            writer.CustomMetadata = new Dictionary<string, string>
            {
                ["meridian_alignment"] = JsonSerializer.Serialize(result.Summary, Json)
            };
            if (result.Rows.Length != 0)
            {
                var values = result.Rows.Select(row => Cells(row, result.Summary)).ToArray();
                using var group = writer.CreateRowGroup();
                for (var index = 0; index < columns.Length; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var rows = values.Select(row => row[index]).ToArray();
                    await group.WriteColumnAsync(new DataColumn(columns[index], rows), ct).ConfigureAwait(false);
                }
            }
        }
        return output.ToArray();
    }

    private static string[] Columns(TimeSeriesAlignmentSummary summary)
        => [.. ProvenanceColumns, .. summary.Fields.Select(field => field.ToLowerInvariant())];

    private static string?[] Cells(TimeSeriesAlignmentRow row, TimeSeriesAlignmentSummary summary) =>
    [
        row.Provider, row.Symbol, row.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        row.Timestamp.ToString("O", CultureInfo.InvariantCulture), row.AdjustmentBasis, row.ValueStatus,
        JsonSerializer.Serialize(row.SourceIds, Json), summary.ContentHash, summary.Calendar, summary.Timezone,
        summary.TimestampRule, summary.InputKind, summary.Interval, summary.GapStrategy,
        summary.MaxGapIntervals.ToString(CultureInfo.InvariantCulture), summary.MissingValueRule, summary.AdjustmentPolicy, summary.DuplicatePolicy,
        .. summary.Fields.Select(field => (field switch
        {
            "Open" => row.Open,
            "High" => row.High,
            "Low" => row.Low,
            "Close" => row.Close,
            "Volume" => row.Volume,
            _ => throw new ArgumentException("The host returned an unrecognized alignment field.")
        })?.ToString(CultureInfo.InvariantCulture))
    ];
}
