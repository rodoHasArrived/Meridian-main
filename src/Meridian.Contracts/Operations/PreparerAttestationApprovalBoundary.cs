namespace Meridian.Contracts.Operations;

/// <summary>
/// Identifies preparer-assertion references that must never satisfy an ordinary approval field.
/// A reference which is not identified here still needs its domain's normal authority checks;
/// this boundary does not authenticate opaque legacy approval text.
/// </summary>
public static class PreparerAttestationApprovalBoundary
{
    public const string ReferencePrefix = "preparer-attestation:";
    public const string ReservedReceiptIdPrefix = "a77e57ed";
    private const string ReceiptRoute = "/api/workstation/evidence/attestations/";

    /// <summary>
    /// Allocates a UUIDv8 from the preparer-receipt namespace, preserving 90 random bits.
    /// The marker only establishes a denial category; it is not proof that a receipt exists
    /// or that its contents are authentic. Ordinary UUIDv4 approvals are unaffected.
    /// </summary>
    public static Guid NewReceiptId()
    {
        var random = Guid.NewGuid().ToString("N");
        return Guid.ParseExact($"{ReservedReceiptIdPrefix}{random[8..12]}8{random[13..]}", "N");
    }

    public static bool IsReservedReceiptId(Guid identity)
    {
        var value = identity.ToString("N");
        return value.StartsWith(ReservedReceiptIdPrefix, StringComparison.Ordinal)
            && value[12] == '8' && value[16] is '8' or '9' or 'a' or 'b';
    }

    public static string ReceiptReference(Guid receiptId)
    {
        if (receiptId == Guid.Empty)
            throw new ArgumentException("A retained preparer receipt identity is required.", nameof(receiptId));
        return $"{ReferencePrefix}v1:{receiptId:D}";
    }

    /// <summary>Unknown versions in the reserved namespace are also refused as ordinary authority.</summary>
    public static bool IsReservedReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;
        var normalized = reference.Trim();
        return normalized.StartsWith(ReferencePrefix, StringComparison.OrdinalIgnoreCase)
            || Guid.TryParse(normalized, out var identity) && IsReservedReceiptId(identity)
            || normalized.StartsWith(ReceiptRoute, StringComparison.OrdinalIgnoreCase)
            || Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
                && uri.AbsolutePath.StartsWith(ReceiptRoute, StringComparison.OrdinalIgnoreCase);
    }

    public static void RequireOrdinaryReference(string? reference, string parameterName)
    {
        if (IsReservedReference(reference))
            throw new ArgumentException(
                "A preparer attestation is not an ordinary approval reference and grants no approval authority.",
                parameterName);
    }
}

/// <summary>
/// Trusted server-side denial classifier for exact receipt identities in one authenticated scope.
/// It exposes no receipt contents, never grants approval, and must not search other tenants.
/// Missing state contains no receipts; unreadable state must throw instead of returning an ordinary classification.
/// </summary>
public interface IPreparerAttestationReferenceClassifier
{
    Task<bool> IsPreparerReceiptReferenceAsync(
        string tenantId,
        string companyId,
        string? reference,
        CancellationToken ct = default);
}
