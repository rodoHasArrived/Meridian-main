using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Meridian.Contracts.Integrity;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.Camt;

/// <summary>
/// ISO 20022 camt.053 (Bank-to-Customer Statement) connector. Ingests the closing booked balance
/// and every booked entry from each <c>Stmt</c>, signing amounts by <c>CdtDbtInd</c>, so institutional
/// bank cash statements reconcile without hand-conversion to CSV. The format carries fixed semantics,
/// so it maps directly to canonical records rather than through a column mapping profile. XML is read
/// forward-only with DTD processing prohibited and no external resolver to avoid XXE.
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
        if (document.Content.Length > StatementConnectorLimits.MaxFileBytes)
        {
            issues.Add(StatementParseIssue.Error(
                "STATEMENT_FILE_TOO_LARGE",
                $"Statement exceeds the {StatementConnectorLimits.MaxFileBytes}-byte import limit."));
            return Task.FromResult(EmptyResult(issues));
        }

        var records = new List<StatementCanonicalRecord>();
        var statementAccounts = new List<string>();
        var rowNumber = 0;
        var sourceRecordCount = 0;
        var statementCount = 0;
        var statementDepth = -1;
        string? account = null;
        var accountCurrency = "USD";

        try
        {
            using var stream = CreateReadStream(document.Content);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                CloseInput = false,
                IgnoreComments = true,
                MaxCharactersInDocument = StatementConnectorLimits.MaxFileBytes
            });

            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.NodeType == XmlNodeType.Element
                    && string.Equals(reader.LocalName, "Stmt", StringComparison.Ordinal))
                {
                    statementCount++;
                    statementDepth = reader.Depth;
                    account = null;
                    accountCurrency = "USD";
                    if (reader.IsEmptyElement)
                    {
                        issues.Add(StatementParseIssue.Error(
                            "CAMT_MISSING_ACCOUNT_ID",
                            "The camt.053 statement has no IBAN or other account identifier; a statement run must reconcile a single, identified account."));
                        return Task.FromResult(EmptyResult(issues));
                    }

                    continue;
                }

                if (statementDepth < 0)
                {
                    continue;
                }

                if (reader.NodeType == XmlNodeType.EndElement
                    && reader.Depth == statementDepth
                    && string.Equals(reader.LocalName, "Stmt", StringComparison.Ordinal))
                {
                    if (string.IsNullOrWhiteSpace(account))
                    {
                        issues.Add(StatementParseIssue.Error(
                            "CAMT_MISSING_ACCOUNT_ID",
                            "The camt.053 statement has no IBAN or other account identifier; a statement run must reconcile a single, identified account. Repair the file so the statement carries its account id before importing."));
                        return Task.FromResult(EmptyResult(issues));
                    }

                    statementAccounts.Add(account);
                    statementDepth = -1;
                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element || reader.Depth != statementDepth + 1)
                {
                    continue;
                }

                if (string.Equals(reader.LocalName, "Acct", StringComparison.Ordinal))
                {
                    using var accountReader = reader.ReadSubtree();
                    var accountElement = XElement.Load(accountReader, LoadOptions.None);
                    account = ResolveAccount(accountElement);
                    accountCurrency = Value(accountElement, "Ccy") ?? "USD";
                    continue;
                }

                var isBalance = string.Equals(reader.LocalName, "Bal", StringComparison.Ordinal);
                var isEntry = string.Equals(reader.LocalName, "Ntry", StringComparison.Ordinal);
                if (!isBalance && !isEntry)
                {
                    continue;
                }

                sourceRecordCount++;
                if (sourceRecordCount > StatementConnectorLimits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error(
                        "STATEMENT_RECORD_LIMIT_EXCEEDED",
                        $"Statement exceeds the {StatementConnectorLimits.MaxRecords}-record import limit."));
                    return Task.FromResult(EmptyResult(issues));
                }

                using var recordReader = reader.ReadSubtree();
                var element = XElement.Load(recordReader, LoadOptions.None);
                if (string.IsNullOrWhiteSpace(account))
                {
                    issues.Add(StatementParseIssue.Error(
                        "CAMT_MISSING_ACCOUNT_ID",
                        "The camt.053 statement has no account identifier before its balance or entry records."));
                    return Task.FromResult(EmptyResult(issues));
                }

                if (isBalance)
                {
                    ProcessBalance(element, account, accountCurrency, records, issues, ref rowNumber);
                }
                else
                {
                    ProcessEntry(element, account, accountCurrency, records, issues, ref rowNumber);
                }
            }
        }
        catch (XmlException ex)
        {
            issues.Add(StatementParseIssue.Error("CAMT_MALFORMED", $"camt.053 document is not well-formed XML: {ex.Message}"));
            return Task.FromResult(EmptyResult(issues));
        }

        if (statementCount == 0)
        {
            issues.Add(StatementParseIssue.Error(
                "CAMT_NO_STATEMENT",
                "No camt.053 statement was found (expected a BkToCstmrStmt/Stmt element)."));
            return Task.FromResult(EmptyResult(issues));
        }

        if (statementCount > 1)
        {
            var distinctAccountCount = statementAccounts.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var (code, message) = distinctAccountCount > 1
                ? ("CAMT_MULTIPLE_ACCOUNTS",
                    $"The camt.053 document contains statements for {distinctAccountCount} different accounts, but a statement run reconciles a single account. Split the file into one document per account before importing.")
                : ("CAMT_MULTIPLE_STATEMENTS",
                    $"The camt.053 document contains {statementCount} statements for one account, but a statement run reconciles a single statement period. Split the file into one document per statement before importing.");
            issues.Add(StatementParseIssue.Error(code, message));
            return Task.FromResult(EmptyResult(issues));
        }

        if (records.Count == 0)
        {
            issues.Add(StatementParseIssue.Warning("CAMT_NO_RECORDS", "The camt.053 statement produced no closing balance or entries."));
        }

        var detectedColumns = new[] { "Bal/CLBD/Amt", "Ntry/Amt", "Ntry/CdtDbtInd", "Ntry/Refs" };
        var fingerprint = new StatementFormatFingerprint(
            Sha256Digest.Compute(document.Content.Span),
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

    private static void ProcessBalance(
        XElement balance,
        string account,
        string accountCurrency,
        ICollection<StatementCanonicalRecord> records,
        ICollection<StatementParseIssue> issues,
        ref int rowNumber)
    {
        if (!string.Equals(BalanceCode(balance), "CLBD", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var date = BalanceDate(balance);
        if (date is null)
        {
            issues.Add(StatementParseIssue.Warning("CAMT_BALANCE_NO_DATE", "Closing balance has no parseable date; skipped.", rowNumber + 1));
            return;
        }

        rowNumber++;
        var result = TrySignedAmount(balance, accountCurrency, out var amount, out var currency);
        if (result != CamtAmountResult.Ok)
        {
            var (code, message) = result == CamtAmountResult.BadDirection
                ? ("CAMT_BALANCE_BAD_DIRECTION", "Closing balance has a missing or unrecognized CdtDbtInd (credit/debit direction); the statement cannot be reconciled.")
                : ("CAMT_BALANCE_BAD_AMOUNT", "Closing balance has a missing or non-numeric Amt; the statement cannot be reconciled.");
            issues.Add(StatementParseIssue.Error(code, message, rowNumber));
            return;
        }

        records.Add(new StatementCanonicalRecord(
            StatementRecordKind.CashBalance, account, string.Empty, 0m, 0m, amount, "cashbalance",
            date.Value, null, currency, null, null));
    }

    private static void ProcessEntry(
        XElement entry,
        string account,
        string accountCurrency,
        ICollection<StatementCanonicalRecord> records,
        ICollection<StatementParseIssue> issues,
        ref int rowNumber)
    {
        var status = EntryStatus(entry);
        if (status is not null && !string.Equals(status, "BOOK", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(StatementParseIssue.Warning(
                "CAMT_ENTRY_NOT_BOOKED",
                $"Entry status '{status}' is not booked; skipped so only booked movements reconcile against the closing balance.",
                rowNumber + 1));
            return;
        }

        var bookingDate = EntryDate(entry, "BookgDt");
        var valueDate = EntryDate(entry, "ValDt");
        var tradeDate = bookingDate ?? valueDate;
        if (tradeDate is null)
        {
            issues.Add(StatementParseIssue.Warning("CAMT_ENTRY_NO_DATE", "Entry has no parseable booking or value date; skipped.", rowNumber + 1));
            return;
        }

        rowNumber++;
        var result = TrySignedAmount(entry, accountCurrency, out var amount, out var currency);
        if (result != CamtAmountResult.Ok)
        {
            var (code, message) = result == CamtAmountResult.BadDirection
                ? ("CAMT_ENTRY_BAD_DIRECTION", "Entry has a missing or unrecognized CdtDbtInd (credit/debit direction); the statement cannot be reconciled.")
                : ("CAMT_ENTRY_BAD_AMOUNT", "Entry has a missing or non-numeric Amt; the statement cannot be reconciled.");
            issues.Add(StatementParseIssue.Error(code, message, rowNumber));
            return;
        }

        records.Add(new StatementCanonicalRecord(
            StatementRecordKind.Transaction, account, string.Empty, 0m, 0m, amount, "transaction",
            tradeDate.Value, valueDate, currency, null, EntryReference(entry)));
    }

    // Why a signed amount could not be resolved, so the caller can report the specific defect rather
    // than a single ambiguous "bad amount" for both a malformed number and an unusable direction.
    private enum CamtAmountResult
    {
        Ok,
        BadAmount,
        BadDirection,
    }

    // Resolves the signed monetary amount and currency. Returns a non-Ok result when the Amt element is
    // missing or non-numeric (BadAmount) or the credit/debit direction is missing or unrecognized
    // (BadDirection): a manufactured 0 amount or a wrong-signed value could exact-match an internal
    // balance or transaction and leave a malformed statement apparently reconciled, so the caller must
    // reject the record instead.
    private static CamtAmountResult TrySignedAmount(XElement element, string fallbackCurrency, out decimal signed, out string currency)
    {
        signed = 0m;
        var amountElement = Element(element, "Amt");
        var rawCurrency = amountElement?.Attribute("Ccy")?.Value?.Trim().ToUpperInvariant();
        currency = string.IsNullOrWhiteSpace(rawCurrency) ? fallbackCurrency.Trim().ToUpperInvariant() : rawCurrency;
        if (amountElement is null
            || !decimal.TryParse(amountElement.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var magnitude))
        {
            return CamtAmountResult.BadAmount;
        }

        if (!TryResolveDirection(element, out var negative))
        {
            return CamtAmountResult.BadDirection;
        }

        signed = negative ? -magnitude : magnitude;
        return CamtAmountResult.Ok;
    }

    // Resolves the sign from the credit/debit indicator. Only recognized codes are honored: CRDT is a
    // credit (positive) and DBIT a debit (negative); the reversal codes some banks emit in place of a
    // separate flag invert the base direction — a reversal of a credit (RCRD) behaves as a debit and a
    // reversal of a debit (RDBT) as a credit. A separate reversal flag (RvslInd) flips a standard
    // CRDT/DBIT entry. A missing or unrecognized indicator is rejected rather than assumed to be a
    // positive credit: assuming credit would give a malformed debit the opposite sign and let it
    // exact-match an internal positive balance or transaction.
    private static bool TryResolveDirection(XElement element, out bool negative)
    {
        negative = false;
        var indicator = Value(element, "CdtDbtInd");
        if (string.IsNullOrWhiteSpace(indicator))
        {
            return false;
        }

        var isCredit = string.Equals(indicator, "CRDT", StringComparison.OrdinalIgnoreCase);
        var isDebit = string.Equals(indicator, "DBIT", StringComparison.OrdinalIgnoreCase);
        var isReversalOfCredit = string.Equals(indicator, "RCRD", StringComparison.OrdinalIgnoreCase);
        var isReversalOfDebit = string.Equals(indicator, "RDBT", StringComparison.OrdinalIgnoreCase);
        if (!isCredit && !isDebit && !isReversalOfCredit && !isReversalOfDebit)
        {
            return false;
        }

        negative = isDebit || isReversalOfCredit;
        // The reversal codes already encode the inversion; only a standard CRDT/DBIT entry is flipped by
        // a standalone RvslInd flag.
        if ((isCredit || isDebit) && IsReversal(Value(element, "RvslInd")))
        {
            negative = !negative;
        }

        return true;
    }

    private static bool IsReversal(string? reversalIndicator)
        => string.Equals(reversalIndicator, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reversalIndicator, "1", StringComparison.Ordinal);

    // Returns the statement's account identifier (IBAN, else Othr/Id), or null when neither is present.
    private static string? ResolveAccount(XElement? accountElement)
    {
        var idElement = Element(accountElement, "Id");
        var iban = Value(idElement, "IBAN");
        if (!string.IsNullOrWhiteSpace(iban))
        {
            return iban.Trim();
        }

        var other = Value(Element(idElement, "Othr"), "Id");
        return string.IsNullOrWhiteSpace(other) ? null : other.Trim();
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

    private static Stream CreateReadStream(ReadOnlyMemory<byte> content)
    {
        if (MemoryMarshal.TryGetArray(content, out var segment) && segment.Array is not null)
        {
            return new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false, publiclyVisible: true);
        }

        return new MemoryStream(content.ToArray(), writable: false);
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
