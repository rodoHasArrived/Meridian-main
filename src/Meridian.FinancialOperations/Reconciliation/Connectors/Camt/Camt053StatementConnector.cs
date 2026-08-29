using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
/// with DTD processing prohibited and no external resolver to avoid XXE.
/// </summary>
/// <remarks>
/// Parsing is streamed and bounded (PRD-010). The document is never decoded to a string or loaded
/// into a whole-document <c>XDocument</c>: a reader walks it once and materializes only the small
/// <c>Acct</c>, <c>Bal</c>, and <c>Ntry</c> subtrees it needs, one at a time, so peak memory tracks a
/// single entry rather than a parse tree many times the payload. The byte, record, and nesting bounds
/// in <see cref="StatementIngressLimits"/> are enforced during that walk, so a hostile document is
/// refused partway through rather than after it has already been expanded in memory.
/// </remarks>
public sealed class Camt053StatementConnector : IStatementConnector
{
    public const string ConnectorId = "camt053";

    private readonly StatementIngressLimits _limits;

    public Camt053StatementConnector()
        : this(StatementIngressLimits.Default)
    {
    }

    public Camt053StatementConnector(StatementIngressLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        _limits = limits;
    }

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

        // Byte cap before anything else. The workstation upload endpoint and the CLI each cap their own
        // input, but a StatementSourceDocument also reaches this connector from a scheduled fetch and from
        // in-process callers, so the connector cannot assume an earlier gate ran.
        if (document.Content.Length > _limits.MaxDocumentBytes)
        {
            issues.Add(_limits.DocumentTooLarge(document.Content.Length));
            return Task.FromResult(EmptyResult(issues));
        }

        // Pass one counts statements and collects each statement's account identifier. Only the small
        // Acct subtrees are materialized, so the multi-statement diagnostics below keep the exact wording
        // they had when the whole document was loaded, without a whole-document parse tree.
        if (!TryScanStatements(document, ct, out var statementAccounts, out var scanIssue))
        {
            issues.Add(scanIssue!);
            return Task.FromResult(EmptyResult(issues));
        }

        if (statementAccounts.Count == 0)
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
        if (statementAccounts.Count > 1)
        {
            var distinctAccounts = statementAccounts
                .Select(static account => account ?? "unknown-account")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var (code, message) = distinctAccounts.Length > 1
                ? ("CAMT_MULTIPLE_ACCOUNTS",
                    $"The camt.053 document contains statements for {distinctAccounts.Length} different accounts, but a statement run reconciles a single account. Split the file into one document per account before importing.")
                : ("CAMT_MULTIPLE_STATEMENTS",
                    $"The camt.053 document contains {statementAccounts.Count} statements for one account, but a statement run reconciles a single statement period. Split the file into one document per statement before importing.");
            issues.Add(StatementParseIssue.Error(code, message));
            return Task.FromResult(EmptyResult(issues));
        }

        var records = new List<StatementCanonicalRecord>();
        var rowNumber = 0;
        string? account = null;
        var accountCurrency = "USD";

        try
        {
            using var stream = AsStream(document);
            using var reader = XmlReader.Create(stream, SecureSettings());
            var statementDepth = -1;

            // Pass two walks the single statement once, materializing one Acct, Bal, or Ntry subtree at a
            // time. Only direct children of Stmt are significant, mirroring the element-axis navigation
            // this connector used before it streamed — a nested Acct inside an entry's related parties is
            // not the statement's account.
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.Depth > _limits.MaxNestingDepth)
                {
                    issues.Add(_limits.NestingTooDeep());
                    return Task.FromResult(EmptyResult(issues));
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (string.Equals(reader.LocalName, "Stmt", StringComparison.Ordinal))
                {
                    statementDepth = reader.Depth;
                    continue;
                }

                if (statementDepth < 0 || reader.Depth != statementDepth + 1)
                {
                    continue;
                }

                switch (reader.LocalName)
                {
                    case "Acct":
                    {
                        if (!TryReadBoundedSubtree(reader, statementDepth, out var accountElement))
                        {
                            issues.Add(_limits.NestingTooDeep());
                            return Task.FromResult(EmptyResult(issues));
                        }

                        account = ResolveAccount(accountElement);
                        if (string.IsNullOrWhiteSpace(account))
                        {
                            // A statement with no IBAN or other account identifier cannot be tied to the account being
                            // reconciled. StatementRunMatcher normalizes every row to the operator-supplied run
                            // account, so an unidentifiable statement could reconcile against the selected Meridian
                            // account; reject it rather than continue with an "unknown-account" placeholder.
                            issues.Add(StatementParseIssue.Error(
                                "CAMT_MISSING_ACCOUNT_ID",
                                "The camt.053 statement has no IBAN or other account identifier; a statement run must reconcile a single, identified account. Repair the file so the statement carries its account id before importing."));
                            return Task.FromResult(EmptyResult(issues));
                        }

                        accountCurrency = Value(accountElement, "Ccy") ?? "USD";
                        break;
                    }

                    case "Bal":
                    {
                        if (!TryReadBoundedSubtree(reader, statementDepth, out var balance))
                        {
                            issues.Add(_limits.NestingTooDeep());
                            return Task.FromResult(EmptyResult(issues));
                        }

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
                        var balanceAmount = TrySignedAmount(balance, accountCurrency, out var balanceValue, out var balanceCurrency);
                        if (balanceAmount != CamtAmountResult.Ok)
                        {
                            var (code, message) = balanceAmount == CamtAmountResult.BadDirection
                                ? ("CAMT_BALANCE_BAD_DIRECTION",
                                    "Closing balance has a missing or unrecognized CdtDbtInd (credit/debit direction); the statement cannot be reconciled.")
                                : ("CAMT_BALANCE_BAD_AMOUNT",
                                    "Closing balance has a missing or non-numeric Amt; the statement cannot be reconciled.");
                            issues.Add(StatementParseIssue.Error(code, message, rowNumber));
                            continue;
                        }

                        if (records.Count >= _limits.MaxRecords)
                        {
                            issues.Add(_limits.TooManyRecords());
                            return Task.FromResult(EmptyResult(issues));
                        }

                        records.Add(new StatementCanonicalRecord(
                            StatementRecordKind.CashBalance,
                            account ?? string.Empty,
                            Symbol: string.Empty,
                            Quantity: 0m,
                            Price: 0m,
                            CashAmount: balanceValue,
                            ActivityType: "cashbalance",
                            TradeDate: date.Value,
                            SettlementDate: null,
                            Currency: balanceCurrency,
                            FeesCommission: null,
                            ExternalTransactionId: null));
                        break;
                    }

                    case "Ntry":
                    {
                        if (!TryReadBoundedSubtree(reader, statementDepth, out var entry))
                        {
                            issues.Add(_limits.NestingTooDeep());
                            return Task.FromResult(EmptyResult(issues));
                        }

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
                        var entryAmount = TrySignedAmount(entry, accountCurrency, out var entryValue, out var entryCurrency);
                        if (entryAmount != CamtAmountResult.Ok)
                        {
                            var (code, message) = entryAmount == CamtAmountResult.BadDirection
                                ? ("CAMT_ENTRY_BAD_DIRECTION",
                                    "Entry has a missing or unrecognized CdtDbtInd (credit/debit direction); the statement cannot be reconciled.")
                                : ("CAMT_ENTRY_BAD_AMOUNT",
                                    "Entry has a missing or non-numeric Amt; the statement cannot be reconciled.");
                            issues.Add(StatementParseIssue.Error(code, message, rowNumber));
                            continue;
                        }

                        if (records.Count >= _limits.MaxRecords)
                        {
                            issues.Add(_limits.TooManyRecords());
                            return Task.FromResult(EmptyResult(issues));
                        }

                        records.Add(new StatementCanonicalRecord(
                            StatementRecordKind.Transaction,
                            account ?? string.Empty,
                            Symbol: string.Empty,
                            Quantity: 0m,
                            Price: 0m,
                            CashAmount: entryValue,
                            ActivityType: "transaction",
                            TradeDate: tradeDate.Value,
                            SettlementDate: valueDate,
                            Currency: entryCurrency,
                            FeesCommission: null,
                            ExternalTransactionId: EntryReference(entry)));
                        break;
                    }
                }
            }
        }
        catch (XmlException ex)
        {
            issues.Add(StatementParseIssue.Error("CAMT_MALFORMED", $"camt.053 document is not well-formed XML: {ex.Message}"));
            return Task.FromResult(EmptyResult(issues));
        }

        // The statement's Acct element is mandatory in camt.053, and pass one only reports a statement it
        // actually saw. A statement that carried no Acct at all reaches here with no account resolved, and
        // is rejected for the same reason a blank identifier is.
        if (string.IsNullOrWhiteSpace(account))
        {
            issues.Add(StatementParseIssue.Error(
                "CAMT_MISSING_ACCOUNT_ID",
                "The camt.053 statement has no IBAN or other account identifier; a statement run must reconcile a single, identified account. Repair the file so the statement carries its account id before importing."));
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

    // Pass one: counts Stmt elements and resolves each one's account identifier, materializing only the
    // Acct subtrees. Returns false with the blocking issue when the document is malformed or breaches the
    // nesting bound, so the caller reports the same diagnostic the whole-document load used to produce.
    private bool TryScanStatements(
        StatementSourceDocument document,
        CancellationToken ct,
        out List<string?> statementAccounts,
        out StatementParseIssue? issue)
    {
        statementAccounts = [];
        issue = null;

        try
        {
            using var stream = AsStream(document);
            using var reader = XmlReader.Create(stream, SecureSettings());
            var statementDepth = -1;
            var accountSeenForStatement = false;

            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (reader.Depth > _limits.MaxNestingDepth)
                {
                    issue = _limits.NestingTooDeep();
                    return false;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (string.Equals(reader.LocalName, "Stmt", StringComparison.Ordinal))
                {
                    statementDepth = reader.Depth;
                    accountSeenForStatement = false;
                    statementAccounts.Add(null);
                    continue;
                }

                // Only the first direct-child Acct of each statement identifies it, matching the
                // first-element navigation the previous implementation used.
                if (statementDepth < 0
                    || accountSeenForStatement
                    || reader.Depth != statementDepth + 1
                    || !string.Equals(reader.LocalName, "Acct", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryReadBoundedSubtree(reader, statementDepth, out var accountElement))
                {
                    issue = _limits.NestingTooDeep();
                    return false;
                }

                accountSeenForStatement = true;
                statementAccounts[^1] = ResolveAccount(accountElement);
            }
        }
        catch (XmlException ex)
        {
            issue = StatementParseIssue.Error("CAMT_MALFORMED", $"camt.053 document is not well-formed XML: {ex.Message}");
            return false;
        }

        return true;
    }

    // Materializes the element the reader is positioned on, and nothing else, enforcing the nesting bound
    // as it goes. XElement.Load over a subtree reader would copy the subtree without checking depth, so a
    // document nested deeply enough to exhaust the stack would fault inside the copy rather than be
    // refused; building the element node by node keeps the bound enforceable.
    private bool TryReadBoundedSubtree(
        XmlReader reader,
        int baseDepth,
        [NotNullWhen(true)] out XElement? element)
    {
        element = null;
        var open = new Stack<XElement>();
        XElement? root = null;

        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            // ReadSubtree restarts Depth at 0 for the subtree root, so the bound is checked against the
            // absolute document depth: the subtree root sits one level below the element it hangs off.
            if (baseDepth + 1 + subtree.Depth > _limits.MaxNestingDepth)
            {
                return false;
            }

            switch (subtree.NodeType)
            {
                case XmlNodeType.Element:
                {
                    var current = new XElement(XName.Get(subtree.LocalName, subtree.NamespaceURI));
                    if (subtree.HasAttributes)
                    {
                        for (var index = 0; index < subtree.AttributeCount; index++)
                        {
                            subtree.MoveToAttribute(index);
                            // Namespace declarations are not data here; the connector navigates by local
                            // name, and copying them as attributes would corrupt the element name table.
                            if (string.Equals(subtree.Prefix, "xmlns", StringComparison.Ordinal)
                                || string.Equals(subtree.LocalName, "xmlns", StringComparison.Ordinal))
                            {
                                continue;
                            }

                            current.SetAttributeValue(XName.Get(subtree.LocalName, subtree.NamespaceURI), subtree.Value);
                        }

                        subtree.MoveToElement();
                    }

                    var isEmpty = subtree.IsEmptyElement;
                    if (open.Count == 0)
                    {
                        root ??= current;
                    }
                    else
                    {
                        open.Peek().Add(current);
                    }

                    if (!isEmpty)
                    {
                        open.Push(current);
                    }

                    break;
                }

                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                {
                    if (open.Count > 0)
                    {
                        open.Peek().Add(subtree.Value);
                    }

                    break;
                }

                case XmlNodeType.EndElement:
                {
                    if (open.Count > 0)
                    {
                        open.Pop();
                    }

                    break;
                }
            }
        }

        element = root;
        return root is not null;
    }

    private static Stream AsStream(StatementSourceDocument document)
        => System.Runtime.InteropServices.MemoryMarshal.TryGetArray(document.Content, out var segment) && segment.Array is not null
            ? new MemoryStream(segment.Array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(document.Content.ToArray(), writable: false);

    private static XmlReaderSettings SecureSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

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
