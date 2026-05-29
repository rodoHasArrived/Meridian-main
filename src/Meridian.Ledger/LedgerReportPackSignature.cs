namespace Meridian.Ledger;

/// <summary>
/// Integrity signature for a generated ledger report pack.
/// </summary>
public sealed record LedgerReportPackSignature(
    string Algorithm,
    string PayloadChecksumSha256,
    string SignedBy,
    DateTimeOffset SignedAtUtc);
