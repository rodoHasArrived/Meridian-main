namespace Meridian.Storage.Packaging;

/// <summary>
/// Serialized accounting evidence carried by the package transport. Storage treats its journal
/// schema as opaque; a configured accounting adapter must validate it before publication or import.
/// </summary>
public sealed record PortableLedgerPayload(string Json, string BalanceChecksumSha256);

/// <summary>
/// Accounting-owned validation boundary for an opaque portable journal. Implementations verify
/// its schema, double-entry invariants and declared account-balance checksum, or throw.
/// </summary>
public interface IPortableLedgerValidator
{
    Task ValidateAsync(PortableLedgerPayload payload, CancellationToken ct = default);
}
