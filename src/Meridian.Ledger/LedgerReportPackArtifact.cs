using System.Text;

namespace Meridian.Ledger;

/// <summary>
/// Export-ready report-pack artifact with a content checksum for audit verification. Text
/// artifacts carry their body in <see cref="Content"/>; binary artifacts (real XLSX/PDF) carry
/// raw bytes in <see cref="BinaryContent"/> and a human-readable descriptor in
/// <see cref="Content"/>. The checksum always covers the delivered bytes.
/// </summary>
public sealed record LedgerReportPackArtifact(
    string Name,
    string ContentType,
    string Content,
    string ChecksumSha256,
    byte[]? BinaryContent = null)
{
    /// <summary>True when this artifact delivers raw bytes rather than UTF-8 text.</summary>
    public bool IsBinary => BinaryContent is not null;

    /// <summary>The delivered bytes: the raw binary body, or the UTF-8 encoding of the text body.</summary>
    public byte[] GetBytes() => BinaryContent ?? Encoding.UTF8.GetBytes(Content);
}
