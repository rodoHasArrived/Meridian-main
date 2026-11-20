using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Services;
using Meridian.Storage.Archival;

namespace Meridian.Ui.Shared.Services;

/// <summary>Retains the first completed comparison for each paper session, account, and cutoff.</summary>
public sealed class PaperSessionDailyReconciliationStore(string root)
{
    private const int SchemaVersion = 1;

    internal async Task<PaperSessionCutoffEvidence?> ReadLatestAsync(string sessionId, string accountId, CancellationToken ct)
    {
        var directory = ScopeDirectory(sessionId, accountId);
        if (!Directory.Exists(directory))
            return null;

        long latest = -1;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            ct.ThrowIfCancellationRequested();
            if (!long.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks > DateTimeOffset.MaxValue.UtcTicks)
                throw new InvalidDataException("A paper reconciliation cutoff receipt has an invalid name.");
            latest = Math.Max(latest, ticks);
        }

        if (latest < 0)
            return null;
        var cutoff = new DateTimeOffset(latest, TimeSpan.Zero);
        return await ReadAsync(sessionId, accountId, cutoff, ct).ConfigureAwait(false)
            ?? throw new InvalidDataException("The latest paper reconciliation receipt disappeared.");
    }

    internal async Task<PaperSessionCutoffEvidence?> ReadAsync(
        string sessionId, string accountId, DateTimeOffset cutoff, CancellationToken ct = default)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(ReceiptPath(sessionId, accountId, cutoff), ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        PaperSessionDailyReconciliationReceipt? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize(bytes, PaperSessionDailyReconciliationJsonContext.Default.PaperSessionDailyReconciliationReceipt);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The paper reconciliation receipt cannot be read.", ex);
        }

        if (receipt?.SchemaVersion != SchemaVersion || receipt.Evidence is not { } evidence
            || !string.Equals(evidence.SessionId, sessionId, StringComparison.Ordinal)
            || !string.Equals(evidence.AccountId, accountId, StringComparison.OrdinalIgnoreCase)
            || evidence.Cutoff != cutoff || !Sha256Digest.FixedEquals(receipt.ContentHash, Hash(evidence)))
            throw new InvalidDataException("The paper reconciliation receipt does not match its scope and retained evidence.");

        return evidence;
    }

    internal async Task RecordAsync(PaperSessionCutoffEvidence evidence, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        evidence = evidence with { Cutoff = evidence.Cutoff.ToUniversalTime() };
        var path = ReceiptPath(evidence.SessionId, evidence.AccountId, evidence.Cutoff);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var lease = await AcquireReceiptAsync(path + ".lock", ct).ConfigureAwait(false);
        var retained = await ReadAsync(evidence.SessionId, evidence.AccountId, evidence.Cutoff, ct).ConfigureAwait(false);
        var hash = Hash(evidence);
        if (retained is not null)
        {
            if (!Sha256Digest.FixedEquals(Hash(retained), hash))
                throw new InvalidDataException("The paper reconciliation cutoff already has different retained evidence.");
            return;
        }

        var receipt = new PaperSessionDailyReconciliationReceipt(SchemaVersion, hash, evidence);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(receipt, PaperSessionDailyReconciliationJsonContext.Default.PaperSessionDailyReconciliationReceipt);
        await AtomicFileWriter.WriteAsync(path, bytes, ct).ConfigureAwait(false);
    }

    private static async Task<FileStream> AcquireReceiptAsync(string path, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 39)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            }
        }
    }

    private string ScopeDirectory(string sessionId, string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        return Path.Combine(root, Sha256Digest.ComputeUtf8(sessionId), Sha256Digest.ComputeUtf8(accountId.ToUpperInvariant()));
    }

    private string ReceiptPath(string sessionId, string accountId, DateTimeOffset cutoff) =>
        Path.Combine(ScopeDirectory(sessionId, accountId), cutoff.UtcTicks.ToString(CultureInfo.InvariantCulture) + ".json");

    private static string Hash(PaperSessionCutoffEvidence evidence) => Sha256Digest.Compute(
        JsonSerializer.SerializeToUtf8Bytes(evidence, PaperSessionDailyReconciliationJsonContext.Default.PaperSessionCutoffEvidence));

    internal static PaperSessionCutoffEvidence AssessFreshness(
        PaperSessionCutoffEvidence retained, PaperSessionCutoffEvidence? current) =>
        current is not null && Sha256Digest.FixedEquals(Hash(retained), Hash(current))
            ? retained
            : retained with { CoverageBlocker = "Activity recorded after this reconciliation changed the cutoff evidence." };
}

internal sealed record PaperSessionDailyReconciliationReceipt(int SchemaVersion, string ContentHash, PaperSessionCutoffEvidence Evidence);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PaperSessionCutoffEvidence))]
[JsonSerializable(typeof(PaperSessionDailyReconciliationReceipt))]
internal sealed partial class PaperSessionDailyReconciliationJsonContext : JsonSerializerContext;
