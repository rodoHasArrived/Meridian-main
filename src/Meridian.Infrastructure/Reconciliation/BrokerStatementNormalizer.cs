using Meridian.Domain.Reconciliation;

namespace Meridian.Infrastructure.Reconciliation;

public sealed class BrokerStatementNormalizer
{
    public NormalizedStatementRow Normalize(string rowId, StatementRowKind kind, IReadOnlyDictionary<string, string> fields)
    {
        var symbol = fields.GetValueOrDefault("symbol") ?? string.Empty;
        var quantity = decimal.TryParse(fields.GetValueOrDefault("quantity"), out var q) ? q : 0m;
        var amount = decimal.TryParse(fields.GetValueOrDefault("amount"), out var a) ? a : 0m;
        var asOf = DateTimeOffset.TryParse(fields.GetValueOrDefault("timestamp"), out var ts) ? ts : DateTimeOffset.UnixEpoch;
        var currency = fields.GetValueOrDefault("currency") ?? "USD";
        var fingerprint = DeterministicFingerprint.Compute($"{kind}|{symbol}|{quantity}|{amount}|{asOf:O}|{currency}");

        return new NormalizedStatementRow(rowId, kind, symbol, quantity, amount, asOf, currency, fingerprint, new Dictionary<string, string>(fields));
    }
}
