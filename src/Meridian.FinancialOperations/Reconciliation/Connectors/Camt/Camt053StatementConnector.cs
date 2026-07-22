using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.Camt;

/// <summary>
/// ISO 20022 camt.053 (Bank-to-Customer Statement) connector. Ingests the closing booked balance
/// and every booked entry from each <c>Stmt</c>, signing amounts by <c>CdtDbtInd</c>, so institutional
/// bank cash statements reconcile without hand-conversion to CSV. The format carries fixed semantics,
/// so it maps directly to canonical records rather than through a column mapping profile. XML is loaded
/// with DTD processing prohibited and no external resolver to avoid XXE.
/// </summary>
public sealed class Camt053StatementConnector : IStatementConnector
{
    public const string ConnectorId = "camt053";

    public StatementConnectorDescriptor Descriptor { get; } = new(
        ConnectorId,
        "ISO 20022 camt.053 bank statement",
        [".xml", ".camt", ".053"],
        SupportsFileImport: true,
        SupportsRemoteFetch: false,
        RequiresMappingProfile: false,
        DefaultProfileId: null);

    public bool CanHandle(StatementSourceDocument document)
    {
        // Content sniff, not just extension: camt.053 shares the .xml extension with IB Flex, so match
        // on the camt namespace or the Bank-to-Customer Statement root element and let Flex claim its own.
        var text = Sniff(document);
        return text.Contains("camt.053", StringComparison.OrdinalIgnoreCase)
            || text.Contains("BkToCstmrStmt", StringComparison.OrdinalIgnoreCase);
    }

    public Task<StatementParseResult> ParseAsync(StatementSourceDocument document, CancellationToken ct = default)
    {
        var issues = new List<StatementParseIssue>();
        var content = Encoding.UTF8.GetString(document.Content.Span);

        XDocument parsed;
        try
        {
            parsed = LoadSecure(content);
        }
        catch (XmlException ex)
        {
            issues.Add(StatementParseIssue.Error("CAMT_MALFORMED", $"camt.053 document is not well-formed XML: {ex.Message}"));
            return Task.FromResult(EmptyResult(issues));
        }

        var statements = Descendants(parsed.Root, "Stmt").ToArray();
        if (statements.Length == 0)
        {
            issues.Add(StatementParseIssue.Error(
                "CAMT_NO_STATEMENT",
                "No camt.053 statement was found (expected a BkToCstmrStmt/Stmt element)."));
            return Task.FromResult(EmptyResult(issues));
        }

        // A camt.053 file may carry several Stmt elements, but a statement run reconciles a single
        // account for a single statement period against one internal cash record. Combining multiple
        // statements — even for the same account across different periods — would give the matcher
        // several closing balances for one internal record and let it match one while opening a false
        // break for the others under the single operator-supplied period. Require exactly one statement;
        // the operator must split the file into one document per statement before importing.
        if (statements.Length > 1)
        {
            var distinctAccounts = statements
                .Select(static statement => ResolveAccount(Element(statement, "Acct")))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var (code, message) = distinctAccounts.Length > 1
                ? ("CAMT_MULTIPLE_ACCOUNTS",
                    $"The camt.053 document contains statements for {distinctAccounts.Length} different accounts, but a statement run reconciles a single account. Split the file into one document per account before importing.")
                : ("CAMT_MULTIPLE_STATEMENTS",
                    $"The camt.053 document contains {statements.Length} statements for one account, but a statement run reconciles a single statement period. Split the file into one document per statement before importing.");
            issues.Add(StatementParseIssue.Error(code, message));
            return Task.FromResult(EmptyResult(issues));
        }

        var records = new List<StatementCanonicalRecord>();
        var rowNumber = 0;
        foreach (var statement in statements)
        {
            ct.ThrowIfCancellationRequested();
            var accountElement = Element(statement, "Acct");
            var account = ResolveAccount(accountElement);
            var accountCurrency = Value(accountElement, "Ccy") ?? "USD";

            foreach (var balance in Elements(statement, "Bal"))
            {
                // Only the closing booked balance (CLBD) is reconciled; opening and interim/available
                // balances are informational and would otherwise double-count against the ledger.
                if (!string.Equals(BalanceCode(balance), "CLBD", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var date = BalanceDate(balance);
                if (date is null)
                {
                    issues.Add(StatementParseIssue.Warning("CAMT_BALANCE_NO_DATE", "Closing balance has no parseable date; skipped.", rowNumber + 1));
                    continue;
                }

                rowNumber++;
                if (!TrySignedAmount(balance, accountCurrency, out var amount, out var currency))
                {
                    issues.Add(StatementParseIssue.Error(
                        "CAMT_BALANCE_BAD_AMOUNT",
                        "Closing balance has a missing or non-numeric Amt; the statement cannot be reconciled.",
                        rowNumber));
                    continue;
                }

                records.Add(new StatementCanonicalRecord(
                    StatementRecordKind.CashBalance,
                    account,
                    Symbol: string.Empty,
                    Quantity: 0m,
                    Price: 0m,
                    CashAmount: amount,
                    ActivityType: "cashbalance",
                    TradeDate: date.Value,
                    SettlementDate: null,
                    Currency: currency,
                    FeesCommission: null,
                    ExternalTransactionId: null));
            }

            foreach (var entry in Elements(statement, "Ntry"))
            {
                // Only booked (BOOK) entries contribute to the closing booked balance the run
                // reconciles against. Pending (PDNG) and informational entries are not yet booked, so
                // importing them as ledger transactions would open false cases and double-count the
                // movement once it posts. Entries with no explicit status are treated as booked.
                var status = EntryStatus(entry);
                if (status is not null && !string.Equals(status, "BOOK", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(StatementParseIssue.Warning(
                        "CAMT_ENTRY_NOT_BOOKED",
                        $"Entry status '{status}' is not booked; skipped so only booked movements reconcile against the closing balance.",
                        rowNumber + 1));
                    continue;
                }

                var bookingDate = EntryDate(entry, "BookgDt");
                var valueDate = EntryDate(entry, "ValDt");
                var tradeDate = bookingDate ?? valueDate;
                if (tradeDate is null)
                {
                    issues.Add(StatementParseIssue.Warning("CAMT_ENTRY_NO_DATE", "Entry has no parseable booking or value date; skipped.", rowNumber + 1));
                    continue;
                }

                rowNumber++;
                if (!TrySignedAmount(entry, accountCurrency, out var amount, out var currency))
                {
                    issues.Add(StatementParseIssue.Error(
                        "CAMT_ENTRY_BAD_AMOUNT",
                        "Entry has a missing or non-numeric Amt; the statement cannot be reconciled.",
                        rowNumber));
                    continue;
                }

                records.Add(new StatementCanonicalRecord(
                    StatementRecordKind.Transaction,
                    account,
                    Symbol: string.Empty,
                    Quantity: 0m,
                    Price: 0m,
                    CashAmount: amount,
                    ActivityType: "transaction",
                    TradeDate: tradeDate.Value,
                    SettlementDate: valueDate,
                    Currency: currency,
                    FeesCommission: null,
                    ExternalTransactionId: EntryReference(entry)));
            }
        }

        if (records.Count == 0)
        {
            issues.Add(StatementParseIssue.Warning("CAMT_NO_RECORDS", "The camt.053 statement produced no closing balance or entries."));
        }

        var detectedColumns = new[] { "Bal/CLBD/Amt", "Ntry/Amt", "Ntry/CdtDbtInd", "Ntry/Refs" };
        var fingerprint = new StatementFormatFingerprint(
            Convert.ToHexString(SHA256.HashData(document.Content.Span)).ToLowerInvariant(),
            detectedColumns.Select(static column => column.ToLowerInvariant()).ToArray(),
            "camt053");

        return Task.FromResult(new StatementParseResult(
            ConnectorId,
            ProfileId: null,
            detectedColumns,
            ColumnMappings: [],
            records,
            issues,
            fingerprint));
    }

    // Resolves the signed monetary amount and currency. Returns false when the Amt element is missing
    // or non-numeric: a manufactured 0 amount could exact-match an internal zero balance and leave a
    // malformed statement apparently reconciled, so the caller must reject the record instead.
    private static bool TrySignedAmount(XElement element, string fallbackCurrency, out decimal signed, out string currency)
    {
        signed = 0m;
        var amountElement = Element(element, "Amt");
        var rawCurrency = amountElement?.Attribute("Ccy")?.Value?.Trim().ToUpperInvariant();
        currency = string.IsNullOrWhiteSpace(rawCurrency) ? fallbackCurrency.Trim().ToUpperInvariant() : rawCurrency;
        if (amountElement is null
            || !decimal.TryParse(amountElement.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var magnitude))
        {
            return false;
        }

        var indicator = Value(element, "CdtDbtInd");
        // Direction from the credit/debit indicator. A debit is negative; the reversal codes some banks
        // emit invert the base direction — a reversal of a credit (RCRD) behaves as a debit and a
        // reversal of a debit (RDBT) as a credit. A separate reversal flag (RvslInd) flips a standard
        // CRDT/DBIT entry. Treating every non-DBIT indicator as a positive credit would give reversals
        // the opposite sign and manufacture false matches or breaks.
        var negative = string.Equals(indicator, "DBIT", StringComparison.OrdinalIgnoreCase)
            || string.Equals(indicator, "RCRD", StringComparison.OrdinalIgnoreCase);
        if ((string.Equals(indicator, "CRDT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(indicator, "DBIT", StringComparison.OrdinalIgnoreCase))
            && IsReversal(Value(element, "RvslInd")))
        {
            negative = !negative;
        }

        signed = negative ? -magnitude : magnitude;
        return true;
    }

    private static bool IsReversal(string? reversalIndicator)
        => string.Equals(reversalIndicator, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reversalIndicator, "1", StringComparison.Ordinal);

    private static string ResolveAccount(XElement? accountElement)
    {
        var idElement = Element(accountElement, "Id");
        var iban = Value(idElement, "IBAN");
        if (!string.IsNullOrWhiteSpace(iban))
        {
            return iban.Trim();
        }

        var other = Value(Element(idElement, "Othr"), "Id");
        return string.IsNullOrWhiteSpace(other) ? "unknown-account" : other.Trim();
    }

    private static string? BalanceCode(XElement balance)
        => Value(Element(Element(balance, "Tp"), "CdOrPrtry"), "Cd");

    // camt.053.001.02 carries the entry status as a simple <Sts> code (BOOK/PDNG/INFO); later
    // versions nest it under <Sts><Cd>. Resolve either shape and normalize to an upper-case code.
    private static string? EntryStatus(XElement entry)
    {
        var status = Element(entry, "Sts");
        if (status is null)
        {
            return null;
        }

        var code = Value(status, "Cd") ?? status.Value?.Trim();
        return string.IsNullOrWhiteSpace(code) ? null : code.ToUpperInvariant();
    }

    private static DateOnly? BalanceDate(XElement balance) => ResolveDate(Element(balance, "Dt"));

    private static DateOnly? EntryDate(XElement entry, string tag) => ResolveDate(Element(entry, tag));

    private static DateOnly? ResolveDate(XElement? dateContainer)
    {
        if (dateContainer is null)
        {
            return null;
        }

        var raw = Value(dateContainer, "Dt") ?? Value(dateContainer, "DtTm");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
            ? DateOnly.FromDateTime(timestamp.UtcDateTime)
            : null;
    }

    private static string? EntryReference(XElement entry)
    {
        var accountServicerReference = Value(entry, "AcctSvcrRef");
        if (!string.IsNullOrWhiteSpace(accountServicerReference))
        {
            return accountServicerReference.Trim();
        }

        var references = Descendants(entry, "Refs").FirstOrDefault();
        var endToEnd = Value(references, "EndToEndId");
        return string.IsNullOrWhiteSpace(endToEnd) || string.Equals(endToEnd, "NOTPROVIDED", StringComparison.OrdinalIgnoreCase)
            ? null
            : endToEnd.Trim();
    }

    private static XDocument LoadSecure(string content)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = true
        };
        using var reader = XmlReader.Create(new StringReader(content), settings);
        return XDocument.Load(reader);
    }

    private static string Sniff(StatementSourceDocument document)
    {
        var span = document.Content.Span;
        return Encoding.UTF8.GetString(span.Length > 1024 ? span[..1024] : span);
    }

    // Namespace-agnostic navigation: camt.053 ships in several versions (001.02, 001.08, ...) under
    // different namespaces, so elements are matched by local name.
    private static IEnumerable<XElement> Elements(XElement? parent, string localName)
        => parent is null ? [] : parent.Elements().Where(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));

    private static IEnumerable<XElement> Descendants(XElement? root, string localName)
        => root is null ? [] : root.Descendants().Where(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal));

    private static XElement? Element(XElement? parent, string localName) => Elements(parent, localName).FirstOrDefault();

    private static string? Value(XElement? parent, string localName) => Element(parent, localName)?.Value?.Trim();

    private static StatementParseResult EmptyResult(IReadOnlyList<StatementParseIssue> issues)
        => new(ConnectorId, null, [], [], [], issues, new StatementFormatFingerprint(string.Empty, [], "camt053"));
}
