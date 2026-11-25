using System.Text.Json;
using Meridian.Contracts.Backfill;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Integrity;
using Meridian.Domain.Events;
using Meridian.Storage.Archival;

namespace Meridian.Storage.Backfill;

/// <summary>Content-addressed receipts for flushed historical data responses in one storage root.</summary>
public sealed class BackfillBatchStore(string directory)
{
    private readonly string _directory = Path.GetFullPath(directory);

    /// <summary>Serializes cooperating backfill writers sharing the storage root.</summary>
    public Task<FileStream> AcquireRunLeaseAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_directory);
        return ArchiveAppendLock.AcquireAsync(Path.Combine(_directory, "run"), TimeSpan.FromSeconds(30), ct);
    }

    /// <summary>Publishes a batch once its rows have been flushed. Replaying the same revision is idempotent.</summary>
    public async Task<BackfillBatchReceipt> PublishAsync(BackfillBatchRevision batch, CancellationToken ct = default)
    {
        batch.Validate();
        var existing = await ReadAsync(batch.Receipt.RevisionId, ct).ConfigureAwait(false);
        if (existing is not null)
            return existing.Receipt;

        var json = JsonSerializer.Serialize(batch, BackfillStatusStoreJsonContext.Default.BackfillBatchRevision);
        await AtomicFileWriter.WriteAsync(PathFor(batch.Receipt.RevisionId), json, ct).ConfigureAwait(false);
        return batch.Receipt;
    }

    public async Task<BackfillBatchRevision?> ReadAsync(string revisionId, CancellationToken ct = default)
    {
        try
        {
            var json = await File.ReadAllTextAsync(PathFor(revisionId), ct).ConfigureAwait(false);
            var batch = JsonSerializer.Deserialize(json, BackfillStatusStoreJsonContext.Default.BackfillBatchRevision)
                ?? throw new InvalidDataException("The backfill batch receipt is empty.");
            batch.Validate();
            if (!Sha256Digest.FixedEquals(batch.Receipt.RevisionId, revisionId))
                throw new InvalidDataException("The backfill batch receipt address does not match its content.");
            return batch;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The backfill batch receipt could not be decoded.", ex);
        }
    }

    private string PathFor(string revisionId) => Sha256Digest.IsCanonical(revisionId)
        ? Path.Combine(_directory, revisionId + ".json")
        : throw new ArgumentException("A canonical SHA-256 backfill revision is required.", nameof(revisionId));
}

/// <summary>Retained row digests establish that the replayed bar belongs to the pinned revision.</summary>
public sealed record BackfillBatchRevision(int SchemaVersion, BackfillBatchReceipt Receipt, IReadOnlyList<string> RowHashes)
{
    private const int CurrentVersion = 1;

    /// <summary>Binds optional source quality facts before rows are stamped with this content address.</summary>
    public BackfillBatchRevision WithInputEvidence(Meridian.Contracts.Operations.MarketDataInputEvidence input)
    {
        var frozen = Meridian.Contracts.Operations.MarketDataInputEvidenceTags.Freeze([input])![0];
        var receipt = Receipt with { InputEvidence = frozen, RevisionId = string.Empty };
        return this with { Receipt = receipt with { RevisionId = Address(receipt) } };
    }

    public static BackfillBatchRevision Create(
        string provider, string? servingProvider, string feed, string instrument, string granularity,
        DateOnly? from, DateOnly? to, string? calendar, IReadOnlyList<DateOnly>? expected,
        IEnumerable<DateOnly> received, IEnumerable<string> rowHashes)
    {
        var receivedDates = received.Distinct().Order().ToArray();
        var expectedDates = expected?.Distinct().Order().ToArray();
        var hashes = rowHashes.Order(StringComparer.Ordinal).ToArray();
        var status = expectedDates is null ? "Unknown"
            : expectedDates.Except(receivedDates).Any() ? "Partial" : "Complete";
        var receipt = new BackfillBatchReceipt(
            string.Empty, provider.Trim().ToLowerInvariant(), servingProvider?.Trim().ToLowerInvariant(),
            feed.Trim().ToLowerInvariant(), instrument.Trim().ToUpperInvariant(), granularity,
            from, to, status, hashes.Length, HashRows(hashes), calendar, expectedDates, receivedDates);
        return new(CurrentVersion, receipt with { RevisionId = Address(receipt) }, hashes);
    }

    public static string HashRow(MarketEvent item) => item.Payload switch
    {
        HistoricalBar bar => HashHistoricalRow(bar),
        AggregateBarPayload bar => Sha256Digest.ComputeUtf8(FormattableString.Invariant(
            $"aggregate|{bar.StartTime.UtcTicks}|{bar.EndTime.UtcTicks}|{bar.Open:G29}|{bar.High:G29}|{bar.Low:G29}|{bar.Close:G29}|{bar.Volume}|{bar.Vwap:G29}|{bar.TradeCount}|{(byte)bar.Timeframe}")),
        _ => throw new ArgumentException("Backfill batch evidence requires a historical or aggregate bar.", nameof(item))
    };

    private static string HashHistoricalRow(HistoricalBar bar)
    {
        var retained = FormattableString.Invariant(
            $"daily|{bar.SessionDate:yyyy-MM-dd}|{bar.Open:G29}|{bar.High:G29}|{bar.Low:G29}|{bar.Close:G29}|{bar.Volume}|{bar.IsAdjusted}");

        // Older receipts did not declare a precise basis. Preserve their exact row identity,
        // while binding new declarations so numerically equal adjusted bars cannot substitute
        // for one another in retained ingestion or backtest evidence.
        if (bar.PriceBasis != HistoricalPriceBasis.Unknown)
            retained += $"|price-basis:v1:{bar.PriceBasis}";

        return Sha256Digest.ComputeUtf8(retained);
    }

    public void Validate()
    {
        if (SchemaVersion != CurrentVersion || Receipt is null || RowHashes is null ||
            Receipt.RowCount != RowHashes.Count || RowHashes.Any(hash => !Sha256Digest.IsCanonical(hash)) ||
            !Sha256Digest.FixedEquals(Receipt.ContentHash, HashRows(RowHashes)) ||
            !Sha256Digest.FixedEquals(Receipt.RevisionId, Address(Receipt)))
            throw new InvalidDataException("The retained backfill batch receipt failed its version or hash check.");
    }

    private static string Address(BackfillBatchReceipt receipt) => Sha256Digest.ComputeUtf8(
        JsonSerializer.Serialize(receipt with { RevisionId = string.Empty }, BackfillStatusStoreJsonContext.Default.BackfillBatchReceipt));

    private static string HashRows(IEnumerable<string> hashes) => Sha256Digest.ComputeUtf8(string.Join('\n', hashes));
}
