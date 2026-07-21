using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.Bai2;

/// <summary>
/// BAI2 (Cash Management Balance Reporting) connector. Reads the group as-of date and currency,
/// each account's closing ledger balance (status code 015), and every transaction-detail (16) record,
/// signing amounts by BAI type-code class (100–399 credit, 400–799 debit). Amounts are expressed in
/// the currency's minor units (cents) per the BAI2 convention and scaled to major units. This lets
/// institutional bank cash statements reconcile without hand-conversion to CSV.
/// </summary>
public sealed class Bai2StatementConnector : IStatementConnector
{
    public const string ConnectorId = "bai2";
    private const string ClosingLedgerStatusCode = "015";
    private const string ClosingAvailableStatusCode = "045";

    public StatementConnectorDescriptor Descriptor { get; } = new(
        ConnectorId,
        "BAI2 cash management statement",
        [".bai", ".bai2"],
        SupportsFileImport: true,
        SupportsRemoteFetch: false,
        RequiresMappingProfile: false,
        DefaultProfileId: null);

    public bool CanHandle(StatementSourceDocument document)
    {
        // BAI2 files begin with a "01," File Header record. Content-sniff so a .txt export is routed
        // here rather than to the CSV catch-all.
        var text = Sniff(document).TrimStart();
        return text.StartsWith("01,", StringComparison.Ordinal);
    }

    public Task<StatementParseResult> ParseAsync(StatementSourceDocument document, CancellationToken ct = default)
    {
        var issues = new List<StatementParseIssue>();
        var content = Encoding.UTF8.GetString(document.Content.Span);
        var records = new List<StatementCanonicalRecord>();

        var groupCurrency = "USD";
        DateOnly? asOfDate = null;
        var account = "unknown-account";
        var accountCurrency = groupCurrency;
        var rowNumber = 0;

        foreach (var rawLine in content.Split('\n'))
        {
            ct.ThrowIfCancellationRequested();
            var line = rawLine.Trim().TrimEnd('/').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split(',');
            switch (fields[0])
            {
                case "02":
                    asOfDate = ParseBaiDate(FieldAt(fields, 4)) ?? asOfDate;
                    groupCurrency = NormalizeCurrency(FieldAt(fields, 6), groupCurrency);
                    accountCurrency = groupCurrency;
                    break;

                case "03":
                    account = string.IsNullOrWhiteSpace(FieldAt(fields, 1)) ? "unknown-account" : FieldAt(fields, 1)!.Trim();
                    accountCurrency = NormalizeCurrency(FieldAt(fields, 2), groupCurrency);
                    if (TryResolveClosingBalance(fields, out var balanceMinorUnits) && asOfDate is { } balanceDate)
                    {
                        rowNumber++;
                        records.Add(new StatementCanonicalRecord(
                            StatementRecordKind.CashBalance,
                            account,
                            Symbol: string.Empty,
                            Quantity: 0m,
                            Price: 0m,
                            CashAmount: ToMajorUnits(balanceMinorUnits),
                            ActivityType: "cashbalance",
                            TradeDate: balanceDate,
                            SettlementDate: null,
                            Currency: accountCurrency,
                            FeesCommission: null,
                            ExternalTransactionId: null));
                    }

                    break;

                case "16":
                    if (asOfDate is not { } transactionDate)
                    {
                        issues.Add(StatementParseIssue.Warning("BAI2_NO_ASOF_DATE", "Transaction detail appeared before a group as-of date; skipped.", rowNumber + 1));
                        break;
                    }

                    var typeCode = FieldAt(fields, 1);
                    if (!TryParseMinorUnits(FieldAt(fields, 2), out var amountMinorUnits))
                    {
                        issues.Add(StatementParseIssue.Warning("BAI2_BAD_AMOUNT", "Transaction detail has no parseable amount; skipped.", rowNumber + 1));
                        break;
                    }

                    rowNumber++;
                    var signedAmount = SignByTypeCode(ToMajorUnits(amountMinorUnits), typeCode);
                    records.Add(new StatementCanonicalRecord(
                        StatementRecordKind.Transaction,
                        account,
                        Symbol: string.Empty,
                        Quantity: 0m,
                        Price: 0m,
                        CashAmount: signedAmount,
                        ActivityType: "transaction",
                        TradeDate: transactionDate,
                        SettlementDate: null,
                        Currency: accountCurrency,
                        FeesCommission: null,
                        ExternalTransactionId: ResolveReference(fields)));
                    break;
            }
        }

        var detectedColumns = new[] { "03/015", "16/TypeCode", "16/Amount", "16/CustomerRef" };
        var fingerprint = new StatementFormatFingerprint(
            Convert.ToHexString(SHA256.HashData(document.Content.Span)).ToLowerInvariant(),
            detectedColumns.Select(static column => column.ToLowerInvariant()).ToArray(),
            "bai2");

        // A BAI2 file can carry several 03 account-identifier records, but a statement run reconciles a
        // single account and the matcher normalizes every imported row to the run's one external account.
        // Committing a multi-account file would compare (and coincidentally match) one account's balances
        // and entries against another account's Meridian records, so reject it: the operator must split
        // the file into one document per account before importing.
        var distinctAccounts = records
            .Select(static record => record.Account)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctAccounts.Length > 1)
        {
            issues.Add(StatementParseIssue.Error(
                "BAI2_MULTIPLE_ACCOUNTS",
                $"The BAI2 file contains records for {distinctAccounts.Length} different accounts, but a statement run reconciles a single account. Split the file into one document per account before importing."));
            return Task.FromResult(new StatementParseResult(
                ConnectorId,
                ProfileId: null,
                detectedColumns,
                ColumnMappings: [],
                [],
                issues,
                fingerprint));
        }

        if (records.Count == 0)
        {
            issues.Add(StatementParseIssue.Warning("BAI2_NO_RECORDS", "The BAI2 file produced no closing balances or transaction details."));
        }

        return Task.FromResult(new StatementParseResult(
            ConnectorId,
            ProfileId: null,
            detectedColumns,
            ColumnMappings: [],
            records,
            issues,
            fingerprint));
    }

    private static bool TryResolveClosingBalance(string[] fields, out long minorUnits)
    {
        // The account-identifier record carries repeating (status-code, amount, item-count, funds-type)
        // groups whose funds-type can span a variable number of sub-fields. Rather than assume a fixed
        // stride, scan for the closing-ledger status code and take the following field as the amount,
        // falling back to closing-available.
        if (TryResolveBalanceForStatus(fields, ClosingLedgerStatusCode, out minorUnits))
        {
            return true;
        }

        return TryResolveBalanceForStatus(fields, ClosingAvailableStatusCode, out minorUnits);
    }

    private static bool TryResolveBalanceForStatus(string[] fields, string statusCode, out long minorUnits)
    {
        for (var index = 3; index < fields.Length - 1; index++)
        {
            if (string.Equals(fields[index].Trim(), statusCode, StringComparison.Ordinal)
                && TryParseMinorUnits(fields[index + 1], out minorUnits))
            {
                return true;
            }
        }

        minorUnits = 0;
        return false;
    }

    private static decimal SignByTypeCode(decimal amount, string? typeCode)
    {
        // BAI type-code classes: 100–399 are credits (positive), 400–799 are debits (negative).
        if (int.TryParse(typeCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) && code is >= 400 and <= 799)
        {
            return -Math.Abs(amount);
        }

        return amount;
    }

    private static string? ResolveReference(string[] fields)
    {
        var customerReference = FieldAt(fields, 5);
        if (!string.IsNullOrWhiteSpace(customerReference))
        {
            return customerReference.Trim();
        }

        var bankReference = FieldAt(fields, 4);
        return string.IsNullOrWhiteSpace(bankReference) ? null : bankReference.Trim();
    }

    private static bool TryParseMinorUnits(string? value, out long minorUnits)
        => long.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out minorUnits);

    private static decimal ToMajorUnits(long minorUnits) => minorUnits / 100m;

    private static DateOnly? ParseBaiDate(string? value)
    {
        var trimmed = value?.Trim();
        if (trimmed is null || trimmed.Length != 6
            || !int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        var year = 2000 + int.Parse(trimmed[..2], CultureInfo.InvariantCulture);
        var month = int.Parse(trimmed.Substring(2, 2), CultureInfo.InvariantCulture);
        var day = int.Parse(trimmed.Substring(4, 2), CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return null;
        }

        try
        {
            return new DateOnly(year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string NormalizeCurrency(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();

    private static string? FieldAt(string[] fields, int index) => index < fields.Length ? fields[index] : null;

    private static string Sniff(StatementSourceDocument document)
    {
        var span = document.Content.Span;
        return Encoding.UTF8.GetString(span.Length > 256 ? span[..256] : span);
    }
}
