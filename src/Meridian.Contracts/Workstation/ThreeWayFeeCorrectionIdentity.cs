using System.Text.Json;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Workstation;

/// <summary>Economic identity excludes observation/run, amount and period so retries cannot double-book a fee.</summary>
public static class ThreeWayFeeCorrectionIdentity
{
    public static string Compute(string tenantId, string companyId, string providerId, string externalAccountId,
        Guid financialAccountId, Guid ledgerBookId, string feeTransactionId)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var value in new[] { Normalize(tenantId), Normalize(companyId),
                Normalize(providerId), externalAccountId,
                financialAccountId.ToString("D"), ledgerBookId.ToString("D"), feeTransactionId })
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                writer.WriteStringValue(value);
            }
            writer.WriteEndArray();
        }
        return Sha256Digest.Compute(stream.ToArray());
    }

    private static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().ToUpperInvariant();
    }

    public static string Compute(ThreeWayFeeCorrectionEvidenceDto evidence) => Compute(evidence.TenantId,
        evidence.CompanyId, evidence.ProviderId, evidence.ExternalAccountId, evidence.FinancialAccountId,
        evidence.LedgerBookId, evidence.FeeTransactionId);

    public static Guid SourceEventId(ThreeWayFeeCorrectionEvidenceDto evidence)
        => new(Convert.FromHexString(Compute(evidence)).AsSpan(0, 16));
}
