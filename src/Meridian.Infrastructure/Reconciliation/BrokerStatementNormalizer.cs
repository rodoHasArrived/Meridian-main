using System.Security.Cryptography;
using System.Text;
using Meridian.Domain.Reconciliation;

namespace Meridian.Infrastructure.Reconciliation;

public sealed class BrokerStatementNormalizer
{
    public NormalizedStatementRow Normalize(string rowId, StatementRowKind kind, IDictionary<string, string> fields)
    {
        var symbol = GetField(fields, "symbol", string.Empty);
        var quantity = decimal.TryParse(GetField(fields, "quantity", string.Empty), out var q) ? q : 0m;
        var amount = decimal.TryParse(GetField(fields, "amount", string.Empty), out var a) ? a : 0m;
        var asOf = DateTimeOffset.TryParse(GetField(fields, "timestamp", string.Empty), out var ts) ? ts : DateTimeOffset.UnixEpoch;
        var currency = GetField(fields, "currency", "USD");
        var fingerprint = ComputeFingerprint($"{kind}|{symbol}|{quantity}|{amount}|{asOf:O}|{currency}");

        return new NormalizedStatementRow(rowId, kind, symbol, quantity, amount, asOf, currency, fingerprint, new Dictionary<string, string>(fields));
    }

    private static string GetField(IDictionary<string, string> fields, string key, string fallback)
        => fields.TryGetValue(key, out var value) ? value : fallback;

    private static string ComputeFingerprint(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
