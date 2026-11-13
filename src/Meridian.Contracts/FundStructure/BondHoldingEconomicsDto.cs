using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.FundStructure;

[JsonConverter(typeof(JsonStringEnumConverter<BondHoldingQuoteBasis>))]
public enum BondHoldingQuoteBasis { Unknown, PercentOfCurrentFace, PercentOfOriginalFace, CurrencyPerCurrentFace }

[JsonConverter(typeof(JsonStringEnumConverter<BondHoldingInvoiceBasis>))]
public enum BondHoldingInvoiceBasis { Unknown, Nominal, IndexedPrincipal }

/// <summary>Explicit quantities; face excludes inflation, and amounts retain the source's currency.</summary>
public sealed record BondHoldingEconomicsDto(
    [property: JsonRequired] decimal OriginalFace,
    [property: JsonRequired] decimal CurrentFace,
    [property: JsonRequired] decimal PrincipalFactor,
    [property: JsonRequired] decimal InflationRatio,
    [property: JsonRequired] BondHoldingQuoteBasis QuoteBasis,
    [property: JsonRequired] decimal Quote,
    [property: JsonRequired] bool QuoteIsDirty,
    [property: JsonRequired] decimal AccruedInterest,
    decimal? CleanValue = null,
    decimal? DirtyValue = null,
    decimal Fees = 0m,
    string? SourceEvidenceId = null,
    string? OriginalSourceJson = null,
    [property: JsonRequired] BondHoldingInvoiceBasis InvoiceBasis = BondHoldingInvoiceBasis.Unknown);

/// <summary>An explicitly reviewed full statement; source evidence is not accounting authority.</summary>
public sealed record CustodianStatementAuthorityDto(
    int Revision,
    string SourceEvidenceId,
    string SourceContentSha256,
    bool IsAccepted = true,
    bool IsFullSnapshot = true,
    Guid? SupersedesBatchId = null);

public sealed record CustodianHoldingSnapshotDto(
    CustodianStatementBatchDto Batch,
    IReadOnlyList<CustodianPositionLineDto> Lines);

/// <summary>Pure normalization and immutable identity shared by both account stores.</summary>
public static class BondHoldingNormalization
{
    public const string ModelVersion = "bond-holding-normalization-v1";

    public static (decimal Clean, decimal Dirty) Calculate(BondHoldingEconomicsDto bond, string currency)
    {
        ArgumentNullException.ThrowIfNull(bond);
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new ArgumentException("An explicit uppercase three-letter currency is required.");
        if (!Enum.IsDefined(bond.QuoteBasis) || bond.QuoteBasis == BondHoldingQuoteBasis.Unknown
            || !Enum.IsDefined(bond.InvoiceBasis) || bond.InvoiceBasis == BondHoldingInvoiceBasis.Unknown
            || bond.OriginalFace == 0m || bond.PrincipalFactor is < 0m or > 1m
            || bond.InflationRatio <= 0m || bond.Fees < 0m)
            throw new ArgumentException("Bond holdings require explicit valid face, factor, inflation ratio and quotation basis.");
        if (bond.OriginalFace * bond.PrincipalFactor != bond.CurrentFace)
            throw new ArgumentException("Current face must equal original face times principal factor; inflation is separate.");
        var invoiceRatio = bond.InvoiceBasis == BondHoldingInvoiceBasis.IndexedPrincipal ? bond.InflationRatio : 1m;
        var quoted = bond.QuoteBasis switch
        {
            BondHoldingQuoteBasis.PercentOfCurrentFace => bond.CurrentFace * invoiceRatio * bond.Quote / 100m,
            BondHoldingQuoteBasis.PercentOfOriginalFace => bond.OriginalFace * invoiceRatio * bond.Quote / 100m,
            BondHoldingQuoteBasis.CurrencyPerCurrentFace => bond.CurrentFace * invoiceRatio * bond.Quote,
            _ => throw new ArgumentException("Unsupported bond quotation basis.")
        };
        var clean = bond.QuoteIsDirty ? quoted - bond.AccruedInterest : quoted;
        var dirty = clean + bond.AccruedInterest;
        if (bond.CleanValue is { } observedClean && LedgerCurrencyRounding.RoundCurrency(clean, currency) != LedgerCurrencyRounding.RoundCurrency(observedClean, currency)
            || bond.DirtyValue is { } observedDirty && LedgerCurrencyRounding.RoundCurrency(dirty, currency) != LedgerCurrencyRounding.RoundCurrency(observedDirty, currency))
            throw new ArgumentException("Reported clean/dirty value does not reconcile with explicit bond units.");
        return (clean, dirty);
    }

    public static string ContentHash(IngestCustodianStatementRequest request)
    {
        var value = JsonSerializer.SerializeToElement(new
        {
            request.BatchId,
            request.AccountId,
            request.AsOfDate,
            request.CustodianName,
            request.SourceFormat,
            request.Authority,
            Lines = request.Lines.OrderBy(l => l.LineId).ToArray()
        });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, value);
        return Sha256Digest.Compute(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var child in value.EnumerateArray())
                    WriteCanonical(writer, child);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetDecimal().ToString("G29", CultureInfo.InvariantCulture));
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}
