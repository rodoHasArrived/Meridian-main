using System.Text.Json;

namespace Meridian.Contracts.Workstation;

/// <summary>
/// Frozen evidence of a preparer assertion. The original command results remain JSON so future
/// DTO additions cannot rewrite a previously exported receipt or declaration.
/// </summary>
public sealed record PreparerAttestationProofExportDto(
    int SchemaVersion,
    string Kind,
    string Disclosure,
    DateTimeOffset RecordedAt,
    string ReceiptReference,
    JsonElement Receipt,
    JsonElement Declaration,
    string AuthorityBoundary);
