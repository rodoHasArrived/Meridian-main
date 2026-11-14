using System.Text.Json;
using System.Globalization;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage;
using Meridian.Storage.Archival;

namespace Meridian.Application.SecurityMaster.CashFlow;

/// <summary>Append-only observed-reset receipts. Explicit calculation pins, never a mutable latest value, select evidence.</summary>
public sealed class FileBondObservedFixingStore(StorageOptions options, TimeProvider? timeProvider = null) : IBondObservedFixingStore
{
    private readonly string _root = Path.Combine(options.RootPath, "bonds", "observed-fixings");
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<BondObservedFixingReceipt> RetainAsync(BondObservedFixingCandidate fixing, CancellationToken ct = default)
    {
        var observation = fixing.Observation ?? throw new ArgumentException("An observed source is required.");
        var expectedFraction = observation.Unit switch
        {
            SecurityRateUnit.Fraction => observation.Value,
            SecurityRateUnit.Percent => observation.Value / 100m,
            SecurityRateUnit.BasisPoints => observation.Value / 10_000m,
            _ => throw new ArgumentException("An observed fixing requires explicit annual rate units.")
        };
        if (fixing.SecurityId == Guid.Empty || fixing.SecurityVersion <= 0 || fixing.ResetDate == default ||
            string.IsNullOrWhiteSpace(fixing.IndexName) || string.IsNullOrWhiteSpace(fixing.RecordedBy) || string.IsNullOrWhiteSpace(fixing.Justification) ||
            fixing.Observation.Kind != SecurityRateObservationKind.ObservedFixing || fixing.Observation.ObservationDate != fixing.ResetDate ||
            !string.Equals(fixing.IndexName, fixing.Observation.SeriesId, StringComparison.OrdinalIgnoreCase) || Math.Abs(fixing.RateFraction) > 1m ||
            fixing.RateFraction != expectedFraction || observation.RetrievedAt == default || observation.RetrievedAt > _timeProvider.GetUtcNow() ||
            observation.PublishedAt > observation.RetrievedAt || observation.ObservationDate > DateOnly.FromDateTime(observation.RetrievedAt.UtcDateTime) ||
            observation.VintageDate > observation.VintageEndDate || string.IsNullOrWhiteSpace(observation.Provider) || string.IsNullOrWhiteSpace(observation.Currency) ||
            string.IsNullOrWhiteSpace(observation.SourceUnits) || string.IsNullOrWhiteSpace(observation.Transformation) ||
            !decimal.TryParse(observation.OriginalValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var original) || original != observation.Value)
            throw new ArgumentException("An observed fixing needs a bond/version, exact reset, named index, source observation, annual fractional rate and review justification.");
        fixing = fixing with { IndexName = fixing.IndexName.Trim(), RecordedBy = fixing.RecordedBy.Trim(), Justification = fixing.Justification.Trim() };
        // Retries re-verify the same source at a later retrieval time. Only this transport time is
        // excluded from semantic identity; the original complete evidence and hash remain retained.
        var identity = Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(fixing with
        { Observation = fixing.Observation with { RetrievedAt = default } }, BondObservedFixingJsonContext.Default.BondObservedFixingCandidate));
        var receiptId = new Guid(Convert.FromHexString(identity[..32]));
        var path = FilePath(fixing.SecurityId, receiptId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await using var fileLock = new FileStream(Path.Combine(Path.GetDirectoryName(path)!, "writer.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(path))
            {
                var retained = await GetAsync(fixing.SecurityId, receiptId, ct).ConfigureAwait(false)
                    ?? throw new InvalidDataException("The retained observed fixing is unavailable.");
                if ((retained.Fixing with { Observation = retained.Fixing.Observation with { RetrievedAt = default } })
                    != (fixing with { Observation = fixing.Observation with { RetrievedAt = default } }))
                    throw new InvalidDataException("Observed fixing receipt identity conflicts with retained evidence.");
                return retained;
            }
            var receipt = new BondObservedFixingReceipt(receiptId, fixing, _timeProvider.GetUtcNow(), "");
            receipt = receipt with { ContentSha256 = Hash(receipt) };
            await AtomicFileWriter.WriteAsync(path, JsonSerializer.Serialize(receipt, BondObservedFixingJsonContext.Default.BondObservedFixingReceipt), ct).ConfigureAwait(false);
            return receipt;
        }
        finally { _gate.Release(); }
    }

    public async Task<BondObservedFixingReceipt?> GetAsync(Guid securityId, Guid receiptId, CancellationToken ct = default)
    {
        var path = FilePath(securityId, receiptId);
        if (!File.Exists(path))
            return null;
        var receipt = JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, ct).ConfigureAwait(false),
            BondObservedFixingJsonContext.Default.BondObservedFixingReceipt) ?? throw new InvalidDataException("Observed fixing receipt is empty.");
        if (receipt.ReceiptId != receiptId || receipt.Fixing.SecurityId != securityId || !Sha256Digest.FixedEquals(receipt.ContentSha256, Hash(receipt)))
            throw new InvalidDataException("Observed fixing receipt scope or retained content hash does not match.");
        return receipt;
    }

    public async Task<IReadOnlyList<BondObservedFixingReceipt>> ListAsync(Guid securityId, CancellationToken ct = default)
    {
        var directory = Path.Combine(_root, securityId.ToString("N"));
        if (!Directory.Exists(directory))
            return [];
        var receipts = new List<BondObservedFixingReceipt>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var receiptId))
                throw new InvalidDataException("An observed fixing receipt has an invalid retained identity.");
            receipts.Add(await GetAsync(securityId, receiptId, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Observed fixing receipt disappeared during the read."));
        }
        return receipts.OrderByDescending(static receipt => receipt.RecordedAt).ThenBy(static receipt => receipt.ReceiptId).ToArray();
    }

    private static string Hash(BondObservedFixingReceipt receipt) => Sha256Digest.ComputeUtf8(
        JsonSerializer.Serialize(receipt with { ContentSha256 = "" }, BondObservedFixingJsonContext.Default.BondObservedFixingReceipt));

    private string FilePath(Guid securityId, Guid receiptId)
    {
        if (securityId == Guid.Empty || receiptId == Guid.Empty)
            throw new ArgumentException("Security and observed-fixing receipt identities are required.");
        return Path.Combine(_root, securityId.ToString("N"), receiptId.ToString("N") + ".json");
    }
}
