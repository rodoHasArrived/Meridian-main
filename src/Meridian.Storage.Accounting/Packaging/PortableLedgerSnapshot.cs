using Meridian.Contracts.Integrity;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Ledger;

namespace Meridian.Storage.Packaging;

/// <summary>A portable immutable journal plus a canonical account-balance checksum.
/// Loading always replays the ledger's invariant checks before accepting the checksum.</summary>
public sealed record PortableLedgerSnapshot(int Version, JournalEntry[] Journal, string BalanceChecksumSha256)
{
    public static PortableLedgerSnapshot Capture(IReadOnlyLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var journal = ledger.Journal.ToArray();
        var replay = Replay(journal);
        return new(1, journal, ComputeBalanceChecksum(replay));
    }

    /// <summary>Captures a validated journal for the lower-level package transport.</summary>
    public static PortableLedgerPayload CapturePayload(IReadOnlyLedger ledger)
    {
        var snapshot = Capture(ledger);
        return new(Serialize(snapshot), snapshot.BalanceChecksumSha256);
    }

    public IReadOnlyLedger Restore()
    {
        if (Version != 1 || Journal is null)
            throw new InvalidDataException("Unsupported or incomplete portable ledger snapshot.");
        var ledger = Replay(Journal);
        if (!Sha256Digest.FixedEquals(BalanceChecksumSha256, ComputeBalanceChecksum(ledger)))
            throw new InvalidDataException("Portable ledger account-balance checksum mismatch.");
        return ledger;
    }

    internal static string Serialize(PortableLedgerSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, PortableLedgerJsonContext.Default.PortableLedgerSnapshot);

    internal static PortableLedgerSnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize(json, PortableLedgerJsonContext.Default.PortableLedgerSnapshot)
        ?? throw new InvalidDataException("Portable ledger snapshot is missing.");

    internal static async Task<PortableLedgerSnapshot> ReadAsync(Stream stream, CancellationToken ct) =>
        await JsonSerializer.DeserializeAsync(stream, PortableLedgerJsonContext.Default.PortableLedgerSnapshot, ct)
        ?? throw new InvalidDataException("Portable ledger snapshot is missing.");

    private static Meridian.Ledger.Ledger Replay(IEnumerable<JournalEntry> entries)
    {
        var ledger = new Meridian.Ledger.Ledger();
        foreach (var entry in entries)
            ledger.Post(entry);
        return ledger;
    }

    private static string ComputeBalanceChecksum(IReadOnlyLedger ledger)
    {
        var rows = ledger.TrialBalance().Select(pair => new PortableLedgerBalance(
            pair.Key.Name, pair.Key.AccountType, pair.Key.Symbol,
            pair.Key.FinancialAccountId?.ToUpperInvariant(), pair.Value))
            .OrderBy(row => row.Name, StringComparer.Ordinal).ThenBy(row => row.AccountType)
            .ThenBy(row => row.Symbol, StringComparer.Ordinal).ThenBy(row => row.FinancialAccountId, StringComparer.Ordinal).ToArray();
        return Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(rows,
            PortableLedgerJsonContext.Default.PortableLedgerBalanceArray));
    }
}

internal sealed record PortableLedgerBalance(string Name, LedgerAccountType AccountType,
    string? Symbol, string? FinancialAccountId, decimal Balance);

[JsonSerializable(typeof(PortableLedgerSnapshot))]
[JsonSerializable(typeof(PortableLedgerBalance[]))]
internal sealed partial class PortableLedgerJsonContext : JsonSerializerContext;
