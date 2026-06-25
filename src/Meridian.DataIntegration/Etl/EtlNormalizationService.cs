using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Contracts.Etl;
using Meridian.DataIntegration.Canonicalization;
using Meridian.Domain.Events;

namespace Meridian.DataIntegration.Etl;

public sealed class PartnerSchemaRegistry : IPartnerSchemaRegistry
{
    private static readonly IReadOnlyDictionary<string, CsvSchemaDefinition> Schemas =
        new Dictionary<string, CsvSchemaDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["partner.trades.csv.v1"] = new()
            {
                SchemaId = "partner.trades.csv.v1",
                HasHeaderRow = true,
                Delimiter = ',',
                Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["timestamp"] = "timestamp",
                    ["symbol"] = "symbol",
                    ["price"] = "price",
                    ["size"] = "size",
                    ["venue"] = "venue",
                    ["sequence"] = "sequence",
                    ["aggressor"] = "aggressor"
                }
            },
            ["bank.statement.csv.v1"] = new()
            {
                SchemaId = "bank.statement.csv.v1",
                HasHeaderRow = true,
                Delimiter = ',',
                Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["transaction_date"] = "transaction_date",
                    ["value_date"] = "value_date",
                    ["amount"] = "amount",
                    ["currency"] = "currency",
                    ["transaction_type"] = "transaction_type",
                    ["description"] = "description",
                    ["reference"] = "reference",
                    ["closing_balance"] = "closing_balance"
                }
            },
            ["bank.transactions.csv.v1"] = new()
            {
                SchemaId = "bank.transactions.csv.v1",
                HasHeaderRow = true,
                Delimiter = ',',
                Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["transaction_date"] = "transaction_date",
                    ["amount"] = "amount",
                    ["currency"] = "currency",
                    ["transaction_type"] = "transaction_type",
                    ["description"] = "description",
                    ["reference"] = "reference"
                }
            }
        };

    public CsvSchemaDefinition GetCsvSchema(string schemaId)
        => Schemas.TryGetValue(schemaId, out var schema)
            ? schema
            : throw new InvalidOperationException($"Unsupported ETL schema '{schemaId}'.");

    public bool IsSupported(string schemaId) => Schemas.ContainsKey(schemaId);
}

public sealed class EtlNormalizationService
{
    private readonly IEventCanonicalizer _canonicalizer;

    public EtlNormalizationService(IEventCanonicalizer canonicalizer)
    {
        _canonicalizer = canonicalizer;
    }

    public ValueTask<NormalizationOutcome> NormalizeAsync(EtlJobDefinition definition, PartnerRecordEnvelope record, CancellationToken ct = default)
    {
        if (!string.Equals(definition.PartnerSchemaId, "partner.trades.csv.v1", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(new NormalizationOutcome
            {
                Disposition = EtlRecordDisposition.Rejected,
                RejectCode = "unsupported_schema",
                RejectMessage = $"Unsupported schema '{definition.PartnerSchemaId}'."
            });
        }

        try
        {
            if (!record.Fields.TryGetValue("timestamp", out var timestampRaw) ||
                !DateTimeOffset.TryParse(timestampRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            {
                return ValueTask.FromResult(Reject("invalid_timestamp", "Missing or invalid timestamp."));
            }

            if (!record.Fields.TryGetValue("symbol", out var symbol) || string.IsNullOrWhiteSpace(symbol))
                return ValueTask.FromResult(Reject("missing_symbol", "Symbol is required."));

            if (!record.Fields.TryGetValue("price", out var priceRaw) || !decimal.TryParse(priceRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
                return ValueTask.FromResult(Reject("invalid_price", "Price is required and must be numeric."));

            if (!record.Fields.TryGetValue("size", out var sizeRaw) || !long.TryParse(sizeRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
                return ValueTask.FromResult(Reject("invalid_size", "Size is required and must be numeric."));

            var venue = record.Fields.TryGetValue("venue", out var venueRaw) ? venueRaw : null;
            var aggressor = ParseAggressor(record.Fields.TryGetValue("aggressor", out var aggressorRaw) ? aggressorRaw : null);
            var seq = record.Fields.TryGetValue("sequence", out var seqRaw) && long.TryParse(seqRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSeq)
                ? parsedSeq
                : record.RecordIndex;

            var trade = new Trade(timestamp, symbol!, price, size, aggressor, seq, record.SourceFileName, venue);
            var evt = MarketEvent.Trade(timestamp, symbol!, trade, seq, definition.LogicalSourceName).StampReceiveTime(timestamp);
            evt = _canonicalizer.Canonicalize(evt, ct);

            return ValueTask.FromResult(new NormalizationOutcome
            {
                Disposition = EtlRecordDisposition.Accepted,
                Event = evt,
                RecordHash = ComputeRecordHash(record.SourceFileChecksum, record.RecordIndex)
            });
        }
        catch (Exception ex)
        {
            return ValueTask.FromResult(Reject("normalization_error", ex.Message));
        }
    }

    private static NormalizationOutcome Reject(string code, string message) => new()
    {
        Disposition = EtlRecordDisposition.Rejected,
        RejectCode = code,
        RejectMessage = message
    };

    private static AggressorSide ParseAggressor(string? value)
        => value?.Trim().ToUpperInvariant() switch
        {
            "BUY" or "B" => AggressorSide.Buy,
            "SELL" or "S" => AggressorSide.Sell,
            _ => AggressorSide.Unknown
        };

    private static string ComputeRecordHash(string checksum, long index)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{checksum}:{index}")))[..24];
}
