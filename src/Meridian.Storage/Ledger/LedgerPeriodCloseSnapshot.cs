using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Immutable journal population retained in the same transaction as a hard close. The payload
/// includes opening history, closing journals, source lineage, currency and dimensions, so the
/// closed trial balance can be rebuilt without consulting a subsequently restated journal.
/// </summary>
public sealed record LedgerPeriodCloseSnapshot(
    Guid? LedgerBookId,
    Guid PeriodId,
    long PeriodVersion,
    Guid CloseEventId,
    DateTimeOffset ClosedAt,
    string PayloadJson,
    string ContentHashSha256)
{
    public static LedgerPeriodCloseSnapshot Capture(
        LedgerAccountingPeriod period,
        PeriodCloseEventRecord closeEvent,
        IReadOnlyList<LedgerJournalEntryRecord> entries)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(closeEvent);
        ArgumentNullException.ThrowIfNull(entries);
        if (period.LedgerBookId == Guid.Empty || period.PeriodId == Guid.Empty || period.Version < 1
            || period.Status != "HardClosed" || closeEvent.PeriodId != period.PeriodId
            || closeEvent.NewStatus != "HardClosed" || closeEvent.EventId == Guid.Empty)
            throw new LedgerValidationException("A close snapshot requires a retained hard-close transition.");
        if (entries.Select(x => x.Entry.JournalEntryId).Distinct().Count() != entries.Count)
            throw new LedgerValidationException("A close snapshot cannot contain duplicate journals.");
        if (entries.Any(x => !x.Entry.IsBalanced))
            throw new LedgerValidationException("A close snapshot cannot contain an unbalanced journal.");

        // Sort entry and line populations, then sort every JSON object key. Dictionary insertion
        // order, query order and decimal scale cannot change the canonical bytes or their hash.
        var ordered = entries.OrderBy(x => x.GlobalSequence).ThenBy(x => x.Entry.JournalEntryId)
            .Select(x => x with
            {
                Entry = new JournalEntry(x.Entry.JournalEntryId, x.Entry.Timestamp, x.Entry.Description,
                    x.Entry.Lines.OrderBy(line => line.EntryId).ToArray(), x.Entry.Metadata)
            }).ToArray();
        period = period with { OpenedAt = DatabaseTimestamp(period.OpenedAt), ClosedAt = period.ClosedAt is { } closed ? DatabaseTimestamp(closed) : null };
        closeEvent = closeEvent with { RecordedAt = DatabaseTimestamp(closeEvent.RecordedAt) };
        var payload = new LedgerPeriodCloseSnapshotPayload(1, period, closeEvent, ordered);
        var element = JsonSerializer.SerializeToElement(payload,
            LedgerPeriodCloseSnapshotJsonContext.Default.LedgerPeriodCloseSnapshotPayload);
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
            WriteCanonical(writer, element);
        var json = Encoding.UTF8.GetString(output.ToArray());
        return new(period.LedgerBookId, period.PeriodId, period.Version, closeEvent.EventId,
            closeEvent.RecordedAt, json, Hash(json));
    }

    /// <summary>Checks retained bytes and the scope bound into them before serving a snapshot.</summary>
    public LedgerPeriodCloseSnapshotPayload VerifyIntegrity()
    {
        if (!string.Equals(Hash(PayloadJson), ContentHashSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Ledger close snapshot content hash does not match its retained payload.");
        var payload = JsonSerializer.Deserialize(PayloadJson,
            LedgerPeriodCloseSnapshotJsonContext.Default.LedgerPeriodCloseSnapshotPayload)
            ?? throw new InvalidDataException("Ledger close snapshot payload is missing.");
        if (payload.SchemaVersion != 1 || payload.Period.PeriodId != PeriodId
            || payload.Period.LedgerBookId != LedgerBookId || payload.Period.Version != PeriodVersion
            || payload.CloseEvent.EventId != CloseEventId || payload.CloseEvent.RecordedAt != ClosedAt
            || payload.CloseEvent.PeriodId != PeriodId
            || payload.Period.Status != "HardClosed" || payload.CloseEvent.NewStatus != "HardClosed")
            throw new InvalidDataException("Ledger close snapshot scope differs from its retained payload.");
        return payload;
    }

    private static string Hash(string json)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    internal static DateTimeOffset DatabaseTimestamp(DateTimeOffset value)
        => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number when element.TryGetDecimal(out var number):
                writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}

public sealed record LedgerPeriodCloseSnapshotPayload(
    int SchemaVersion,
    LedgerAccountingPeriod Period,
    PeriodCloseEventRecord CloseEvent,
    IReadOnlyList<LedgerJournalEntryRecord> Entries);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(LedgerPeriodCloseSnapshotPayload))]
[JsonSerializable(typeof(LedgerPeriodCloseSnapshot))]
public sealed partial class LedgerPeriodCloseSnapshotJsonContext : JsonSerializerContext;
