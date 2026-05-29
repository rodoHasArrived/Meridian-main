namespace Meridian.Ledger;

/// <summary>
/// Export-ready report-pack artifact with a content checksum for audit verification.
/// </summary>
public sealed record LedgerReportPackArtifact(
    string Name,
    string ContentType,
    string Content,
    string ChecksumSha256);
