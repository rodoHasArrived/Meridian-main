using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Meridian.Domain.Reconciliation;

namespace Meridian.Infrastructure.Reconciliation;

/// <summary>
/// Imports Interactive Brokers Flex Query XML statements into canonical statement rows.
/// Supports the three Flex sections used by reconciliation: <c>Trades/Trade</c> (activity
/// type <c>trade</c>), <c>OpenPositions/OpenPosition</c> (<c>position</c>), and
/// <c>CashTransactions/CashTransaction</c> (<c>cash</c>). Follows the same duplicate-key,
/// checksum, and persistence flow as <see cref="CsvBrokerStatementService"/> so downstream
/// matching and case intake treat both sources identically.
/// </summary>
public sealed class IbFlexBrokerStatementService(ICanonicalStatementStore store) : IBrokerStatementService
{
    private const long MaximumStatementBytes = 32L * 1024 * 1024;
    private const int MaximumStatementRows = 100_000;
    private const int MaximumXmlDepth = 64;
    private const int MaximumXmlNodes = 500_000;
    private const int MaximumRowNodes = 50_000;
    private const int MaximumScalarCharacters = 64 * 1024;
    private static readonly string[] SupportedBrokerAliases =
        ["ibflex", "ib-flex", "ibkr", "interactivebrokers", "interactive-brokers"];

    /// <summary>Returns whether <paramref name="broker"/> names the IB Flex source.</summary>
    public static bool IsIbFlexSource(string? broker) =>
        broker is not null
        && SupportedBrokerAliases.Contains(broker.Trim(), StringComparer.OrdinalIgnoreCase);

    public async Task<BrokerStatementValidationResult> ValidateAsync(
        BrokerStatementImportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        if (!File.Exists(request.SourcePath))
        {
            errors.Add("Source file not found.");
            return new BrokerStatementValidationResult(false, errors, 0);
        }

        if (!File.Exists(request.EffectiveParsePath))
        {
            errors.Add("Canonical statement artifact not found.");
            return new BrokerStatementValidationResult(false, errors, 0);
        }

        XDocument document;
        try
        {
            var snapshots = await BrokerStatementSourceSnapshot
                .CaptureAsync(request, MaximumStatementBytes, ct)
                .ConfigureAwait(false);
            document = await LoadDocumentAsync(snapshots.ParseArtifact.Content, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException)
        {
            errors.Add(ex is XmlException
                ? $"Source file is not well-formed XML: {ex.Message}"
                : ex.Message);
            return new BrokerStatementValidationResult(false, errors, 0);
        }

        if (!string.Equals(document.Root?.Name.LocalName, "FlexQueryResponse", StringComparison.Ordinal))
        {
            errors.Add("Root element is not FlexQueryResponse; not an IB Flex Query report.");
            return new BrokerStatementValidationResult(false, errors, 0);
        }

        var statements = document.Root!.Descendants("FlexStatement").ToList();
        if (statements.Count == 0)
        {
            errors.Add("Flex report contains no FlexStatement elements.");
            return new BrokerStatementValidationResult(false, errors, 0);
        }

        var rowCount = statements.Sum(static statement =>
            statement.Descendants("Trade").Count()
            + statement.Descendants("OpenPosition").Count()
            + statement.Descendants("CashTransaction").Count());

        if (rowCount > MaximumStatementRows)
        {
            errors.Add($"Flex report exceeds the {MaximumStatementRows}-row limit.");
            return new BrokerStatementValidationResult(false, errors, rowCount);
        }

        if (rowCount == 0)
        {
            errors.Add("Flex report contains no Trade, OpenPosition, or CashTransaction rows; "
                + "include those sections in the Flex Query definition.");
            return new BrokerStatementValidationResult(false, errors, rowCount);
        }

        try
        {
            // Validation and import use the same field rules. Enumeration retains no second row list.
            foreach (var _ in ParseRows(document, "validation", ct))
            { }
        }
        catch (InvalidDataException ex)
        {
            errors.Add(ex.Message);
            return new BrokerStatementValidationResult(false, errors, rowCount);
        }

        var distinctAccounts = DistinctRowAccounts(document);
        if (distinctAccounts.Length > 1)
        {
            errors.Add(
                $"Flex report contains rows for {distinctAccounts.Length} different accounts; a statement run reconciles a single account. "
                + "Split the report into one document per account before importing.");
        }
        else if (distinctAccounts.Length == 1
            && !string.Equals(distinctAccounts[0], request.ExternalAccountId?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Flex report account does not match the statement run external account.");
        }

        return new BrokerStatementValidationResult(errors.Count == 0, errors, rowCount);
    }

    public async Task<BrokerStatementImportResult> ImportAsync(
        BrokerStatementImportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshots = await BrokerStatementSourceSnapshot
            .CaptureAsync(request, MaximumStatementBytes, ct)
            .ConfigureAwait(false);
        var sourceFileHash = snapshots.Source.Sha256;
        var canonicalArtifactHash = snapshots.ParseArtifact.Sha256;
        var compatibleDuplicateKeys = request.AccountingScope is null
            ? StatementDuplicateKey.CreateCompatibleKeys(
                request.FundAccountId,
                request.StatementPeriodStart,
                request.StatementPeriodEnd,
                sourceFileHash,
                canonicalArtifactHash)
            : StatementDuplicateKey.CreateCompatibleKeys(
                request.FundAccountId,
                request.StatementPeriodStart,
                request.StatementPeriodEnd,
                sourceFileHash,
                canonicalArtifactHash,
                request.AccountingScope);
        var duplicateKey = compatibleDuplicateKeys[0];

        foreach (var candidate in compatibleDuplicateKeys)
        {
            if (await store.ImportExistsByDuplicateKeyAsync(candidate, ct).ConfigureAwait(false))
            {
                throw new StatementAlreadyImportedException(candidate);
            }
        }

        var document = await LoadDocumentAsync(snapshots.ParseArtifact.Content, ct).ConfigureAwait(false);
        if (!string.Equals(document.Root?.Name.LocalName, "FlexQueryResponse", StringComparison.Ordinal))
            throw new InvalidDataException("Root element is not FlexQueryResponse; not an IB Flex Query report.");

        var importId = duplicateKey;
        var normalizedRequest = request with
        {
            SourceFileHash = sourceFileHash,
            CanonicalArtifactHash = canonicalArtifactHash
        };
        var rows = ParseRows(document, importId, ct).ToList();
        if (rows.Count > MaximumStatementRows)
        {
            throw new InvalidDataException($"Flex report exceeds the {MaximumStatementRows}-row limit.");
        }
        if (rows.Count == 0)
        {
            // A Flex query configured without the supported sections must fail loudly instead
            // of being recorded as a clean zero-row run with no breaks or cases.
            throw new InvalidDataException(
                "Flex report contains no Trade, OpenPosition, or CashTransaction rows; "
                + "include those sections in the Flex Query definition.");
        }

        // An advisor Flex report can carry several accounts, but a statement run reconciles a single
        // account and the matcher normalizes every row to the run's one external account. Committing a
        // multi-account report would match one account's rows against another account's Meridian
        // records, so reject it: the operator must split it into one document per account.
        var distinctAccounts = rows
            .Select(static row => row.Account)
            .Where(static account => !string.IsNullOrWhiteSpace(account))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctAccounts.Length > 1)
        {
            throw new InvalidDataException(
                $"Flex report contains rows for {distinctAccounts.Length} different accounts, but a statement run reconciles a single account. Split the report into one document per account before importing.");
        }
        if (distinctAccounts.Length == 1
            && !string.Equals(distinctAccounts[0], normalizedRequest.ExternalAccountId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Flex report account does not match the statement run external account.");
        }

        var import = new CanonicalStatementImport(
            importId,
            normalizedRequest.Broker,
            normalizedRequest.StatementPeriodEnd,
            DateTimeOffset.UtcNow,
            normalizedRequest.SourcePath,
            sourceFileHash,
            rows.Count,
            rows.Count)
        {
            SourceInstitution = normalizedRequest.SourceInstitution,
            FundAccountId = normalizedRequest.FundAccountId,
            ExternalAccountId = normalizedRequest.ExternalAccountId,
            StatementPeriodStart = normalizedRequest.StatementPeriodStart,
            StatementPeriodEnd = normalizedRequest.StatementPeriodEnd,
            OriginalFileName = normalizedRequest.OriginalFileName,
            MappingProfileId = normalizedRequest.MappingProfileId,
            ToleranceProfileId = normalizedRequest.ToleranceProfileId,
            ImportedBy = normalizedRequest.ImportedBy,
            SourceFileHash = sourceFileHash,
            CanonicalArtifactHash = canonicalArtifactHash,
            DuplicateKey = duplicateKey,
            AccountingScope = normalizedRequest.AccountingScope
        };

        if (!await store.TrySaveImportAsync(import, rows, ct).ConfigureAwait(false))
        {
            throw new StatementAlreadyImportedException(duplicateKey);
        }

        return new BrokerStatementImportResult(import, rows);
    }

    private static async Task<XDocument> LoadDocumentAsync(byte[] content, CancellationToken ct)
    {
        // DTD processing stays disabled: Flex reports never carry DTDs, and prohibiting them
        // blocks XXE-style payloads in operator-supplied files.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            Async = true,
            CloseInput = true,
            MaxCharactersInDocument = MaximumStatementBytes,
            MaxCharactersFromEntities = 0
        };

        await using var stream = new MemoryStream(content, writable: false);
        using var reader = new BoundedFlexReader(XmlReader.Create(stream, settings), ct);
        var document = new XDocument(new XElement("FlexQueryResponse"));
        XElement? statement = null;
        var statementDepth = -1;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element && reader.Depth == 0
                && reader.LocalName != "FlexQueryResponse")
            {
                throw new InvalidDataException("Root element is not FlexQueryResponse; not an IB Flex Query report.");
            }

            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == statementDepth)
            {
                statement = null;
                statementDepth = -1;
            }
            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI.Length != 0)
                continue;

            if (reader.LocalName == "FlexStatement")
            {
                statement = new XElement("FlexStatement",
                    new XAttribute("accountId", reader.GetAttribute("accountId") ?? string.Empty),
                    new XAttribute("toDate", reader.GetAttribute("toDate") ?? string.Empty));
                document.Root!.Add(statement);
                statementDepth = reader.Depth;
                if (reader.IsEmptyElement)
                {
                    statement = null;
                    statementDepth = -1;
                }
            }
            else if (statement is not null && IsRowName(reader.LocalName))
            {
                // Materialize only one supported row at a time. The wrapper enforces the quotas
                // during subtree reads too; unrelated sections never become an object graph.
                using var subtree = reader.ReadSubtree();
                statement.Add(await XElement.LoadAsync(subtree, LoadOptions.None, ct).ConfigureAwait(false));
            }
        }
        return document;
    }

    private static bool IsRowName(string name) => name is "Trade" or "OpenPosition" or "CashTransaction";

    private sealed class BoundedFlexReader(XmlReader inner, CancellationToken cancellationToken) : XmlReader
    {
        private int _nodes;
        private int _rows;
        private int _statementDepth = -1;
        private int _rowDepth = -1;
        private int _rowNodes;

        private bool Check(bool read)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!read)
                return false;
            var charge = 1 + inner.AttributeCount;
            _nodes = checked(_nodes + charge);
            if (_nodes > MaximumXmlNodes)
                throw new InvalidDataException($"Flex report exceeds the {MaximumXmlNodes}-node XML limit.");
            if (inner.Depth > MaximumXmlDepth)
                throw new InvalidDataException($"Flex report exceeds the {MaximumXmlDepth}-level XML nesting limit.");
            if (inner.Value.Length > MaximumScalarCharacters)
                throw new InvalidDataException("Flex report contains an oversized XML scalar value.");
            if (inner.NodeType == XmlNodeType.Element)
            {
                for (var index = 0; index < inner.AttributeCount; index++)
                    if (inner.GetAttribute(index).Length > MaximumScalarCharacters)
                        throw new InvalidDataException("Flex report contains an oversized XML attribute value.");

                if (_rowDepth >= 0 && inner.Depth > _rowDepth)
                    throw new InvalidDataException("Flex trade, position and cash rows must contain attributes rather than nested elements.");
                if (inner.NamespaceURI.Length == 0 && inner.LocalName == "FlexStatement")
                {
                    if (_statementDepth >= 0)
                        throw new InvalidDataException("FlexStatement elements cannot be nested inside another statement.");
                    if (!inner.IsEmptyElement)
                        _statementDepth = inner.Depth;
                }
                if (_statementDepth >= 0 && inner.NamespaceURI.Length == 0 && IsRowName(inner.LocalName))
                {
                    if (++_rows > MaximumStatementRows)
                        throw new InvalidDataException($"Flex report exceeds the {MaximumStatementRows}-row limit.");
                    if (!inner.IsEmptyElement)
                    {
                        _rowDepth = inner.Depth;
                        _rowNodes = 0;
                    }
                }
            }
            if (_rowDepth >= 0)
            {
                _rowNodes = checked(_rowNodes + charge);
                if (_rowNodes > MaximumRowNodes)
                    throw new InvalidDataException($"Flex row exceeds the {MaximumRowNodes}-node XML limit.");
                if (inner.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                    throw new InvalidDataException("Flex trade, position and cash rows must contain attributes rather than text values.");
                if (inner.NodeType == XmlNodeType.EndElement && inner.Depth == _rowDepth)
                    _rowDepth = -1;
            }
            if (inner.NodeType == XmlNodeType.EndElement && inner.Depth == _statementDepth)
                _statementDepth = -1;
            return true;
        }

        public override bool Read() => Check(inner.Read());
        public override async Task<bool> ReadAsync() => Check(await inner.ReadAsync().ConfigureAwait(false));
        public override int AttributeCount => inner.AttributeCount;
        public override string BaseURI => inner.BaseURI;
        public override int Depth => inner.Depth;
        public override bool EOF => inner.EOF;
        public override bool HasValue => inner.HasValue;
        public override bool IsEmptyElement => inner.IsEmptyElement;
        public override string LocalName => inner.LocalName;
        public override string NamespaceURI => inner.NamespaceURI;
        public override XmlNameTable NameTable => inner.NameTable;
        public override XmlNodeType NodeType => inner.NodeType;
        public override string Prefix => inner.Prefix;
        public override ReadState ReadState => inner.ReadState;
        public override string Value => inner.Value;
        public override Task<string> GetValueAsync() => inner.GetValueAsync();
        public override XmlReaderSettings? Settings => inner.Settings;
        public override string GetAttribute(int i) => inner.GetAttribute(i);
        public override string? GetAttribute(string name) => inner.GetAttribute(name);
        public override string? GetAttribute(string name, string? namespaceURI) => inner.GetAttribute(name, namespaceURI);
        public override string? LookupNamespace(string prefix) => inner.LookupNamespace(prefix);
        public override bool MoveToAttribute(string name) => inner.MoveToAttribute(name);
        public override bool MoveToAttribute(string name, string? ns) => inner.MoveToAttribute(name, ns);
        public override bool MoveToElement() => inner.MoveToElement();
        public override bool MoveToFirstAttribute() => inner.MoveToFirstAttribute();
        public override bool MoveToNextAttribute() => inner.MoveToNextAttribute();
        public override bool ReadAttributeValue() => inner.ReadAttributeValue();
        public override void ResolveEntity() => inner.ResolveEntity();
        public override void Close() => inner.Close();
    }

    private static IEnumerable<CanonicalStatementRow> ParseRows(XDocument document, string importId, CancellationToken ct)
    {
        var rowNumber = 0;
        foreach (var statement in document.Root!.Descendants("FlexStatement"))
        {
            var statementAccount = (string?)statement.Attribute("accountId") ?? string.Empty;
            var statementToDate = ParseFlexDate((string?)statement.Attribute("toDate"));

            foreach (var trade in statement.Descendants("Trade"))
            {
                ct.ThrowIfCancellationRequested();
                rowNumber++;
                yield return new CanonicalStatementRow(
                    importId,
                    rowNumber,
                    Account(trade, statementAccount),
                    (string?)trade.Attribute("symbol") ?? string.Empty,
                    ParseDecimal(trade, "quantity"),
                    ParseDecimal(trade, "tradePrice"),
                    ParseFirstDecimal(trade, "netCash", "proceeds"),
                    "trade",
                    RequireDate(trade, "tradeDate", statementToDate, rowNumber),
                    HashElement(trade))
                {
                    Currency = FlexCurrency(trade),
                    ExternalTransactionId = FlexIdentifier(trade, "tradeID", "transactionID", "ibOrderID")
                };
            }

            foreach (var position in statement.Descendants("OpenPosition"))
            {
                ct.ThrowIfCancellationRequested();
                rowNumber++;
                yield return new CanonicalStatementRow(
                    importId,
                    rowNumber,
                    Account(position, statementAccount),
                    (string?)position.Attribute("symbol") ?? string.Empty,
                    ParseDecimal(position, "position"),
                    ParseFirstDecimal(position, "markPrice", "costBasisPrice"),
                    0m,
                    "position",
                    RequireDate(position, "reportDate", statementToDate, rowNumber),
                    HashElement(position))
                {
                    Currency = FlexCurrency(position)
                };
            }

            foreach (var cash in statement.Descendants("CashTransaction"))
            {
                ct.ThrowIfCancellationRequested();
                rowNumber++;
                yield return new CanonicalStatementRow(
                    importId,
                    rowNumber,
                    Account(cash, statementAccount),
                    (string?)cash.Attribute("symbol") ?? string.Empty,
                    0m,
                    0m,
                    ParseDecimal(cash, "amount"),
                    MapCashTransactionActivity((string?)cash.Attribute("type")),
                    RequireFirstDate(cash, ["dateTime", "reportDate", "settleDate"], statementToDate, rowNumber),
                    HashElement(cash))
                {
                    Currency = FlexCurrency(cash),
                    ExternalTransactionId = FlexIdentifier(cash, "transactionID", "tradeID")
                };
            }
        }
    }

    private static string FlexCurrency(XElement element)
    {
        var currency = ((string?)element.Attribute("currency"))?.Trim().ToUpperInvariant();
        if (currency is null || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
            throw new InvalidDataException("Flex row requires an explicit three-letter currency code.");
        return currency;
    }

    private static string? FlexIdentifier(XElement element, params string[] attributeNames)
    {
        foreach (var attributeName in attributeNames)
        {
            if ((string?)element.Attribute(attributeName) is { Length: > 0 } value)
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string Account(XElement element, string statementAccount)
    {
        var account = ((string?)element.Attribute("accountId") ?? statementAccount).Trim();
        if (account.Length == 0)
            throw new InvalidDataException("Flex row requires account identity from the row or its statement.");
        return account;
    }

    private static string[] DistinctRowAccounts(XDocument document) =>
        document.Root!.Descendants("FlexStatement")
            .SelectMany(static statement =>
            {
                var statementAccount = (string?)statement.Attribute("accountId") ?? string.Empty;
                return statement.Descendants()
                    .Where(static element => element.Name.LocalName is "Trade" or "OpenPosition" or "CashTransaction")
                    .Select(element => Account(element, statementAccount));
            })
            .Where(static account => !string.IsNullOrWhiteSpace(account))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>
    /// Maps a Flex CashTransaction <c>type</c> to the canonical activity type. Cash transactions are
    /// ledger movements, not the account's ending cash balance, so they reconcile against ledger
    /// transactions rather than the closing balance: fee-like rows ("Other Fees", "Advisor Fees", …)
    /// become <c>fee</c>, dividends become <c>dividend</c>, and the rest (deposits, withdrawals,
    /// interest, withholding tax) become generic <c>transaction</c> rows. Canonical <c>cash</c> and
    /// <c>cashbalance</c> are reserved for balance rows so a movement is never treated as a balance.
    /// </summary>
    public static string MapCashTransactionActivity(string? flexType)
    {
        if (flexType is null)
        {
            return "transaction";
        }

        if (flexType.Contains("fee", StringComparison.OrdinalIgnoreCase))
        {
            return "fee";
        }

        return flexType.Contains("dividend", StringComparison.OrdinalIgnoreCase)
            ? "dividend"
            : "transaction";
    }

    private static decimal ParseDecimal(XElement element, string attribute)
    {
        var raw = (string?)element.Attribute(attribute);
        if (string.IsNullOrWhiteSpace(raw)
            || !decimal.TryParse(raw.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException($"Flex row requires a valid invariant decimal '{attribute}' value.");
        return value;
    }

    private static decimal ParseFirstDecimal(XElement element, params string[] attributes)
    {
        foreach (var attribute in attributes)
        {
            var raw = (string?)element.Attribute(attribute);
            if (!string.IsNullOrWhiteSpace(raw))
                return ParseDecimal(element, attribute);
        }

        throw new InvalidDataException($"Flex row requires an explicit value for one of: {string.Join(", ", attributes)}.");
    }

    private static DateOnly RequireDate(XElement element, string attribute, DateOnly? fallback, int rowNumber) =>
        ParseFlexDate((string?)element.Attribute(attribute))
        ?? fallback
        ?? throw new InvalidDataException(
            $"Flex row {rowNumber} has no parseable '{attribute}' date and the statement has no toDate fallback.");

    private static DateOnly RequireFirstDate(
        XElement element,
        string[] attributes,
        DateOnly? fallback,
        int rowNumber)
    {
        foreach (var attribute in attributes)
        {
            if (ParseFlexDate((string?)element.Attribute(attribute)) is { } parsed)
                return parsed;
        }

        return fallback ?? throw new InvalidDataException(
            $"Flex row {rowNumber} has no parseable date attribute and the statement has no toDate fallback.");
    }

    /// <summary>
    /// Parses the date formats Flex Queries emit depending on report configuration:
    /// <c>yyyyMMdd</c>, <c>yyyy-MM-dd</c>, and datetime variants with a <c>;HHmmss</c> or
    /// <c> HH:mm:ss</c> suffix (only the date part is kept).
    /// </summary>
    public static DateOnly? ParseFlexDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var datePart = raw.Split(';', ' ', 'T')[0];

        if (DateOnly.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact))
            return compact;
        if (DateOnly.TryParseExact(datePart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
            return iso;

        return null;
    }

    // Deliberately NOT routed through Sha256Digest (which lowercases): element hashes become
    // persisted per-row identities on CanonicalStatementRow, so changing the casing would
    // detach retained statement rows from their identities (#2691).
    private static string HashElement(XElement element) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(element.ToString(SaveOptions.DisableFormatting))));
}

/// <summary>
/// Routes statement imports to the format-appropriate implementation: IB Flex XML sources go
/// to <see cref="IbFlexBrokerStatementService"/>; everything else keeps the existing CSV path.
/// Routing is by broker alias first, then by an <c>.xml</c> source extension so operators who
/// pick a generic broker kind with a Flex file still land on the XML parser.
/// </summary>
public sealed class RoutingBrokerStatementService(
    CsvBrokerStatementService csvService,
    IbFlexBrokerStatementService ibFlexService) : IBrokerStatementService
{
    public Task<BrokerStatementValidationResult> ValidateAsync(
        BrokerStatementImportRequest request,
        CancellationToken ct = default)
        => Resolve(request).ValidateAsync(request, ct);

    public Task<BrokerStatementImportResult> ImportAsync(
        BrokerStatementImportRequest request,
        CancellationToken ct = default)
        => Resolve(request).ImportAsync(request, ct);

    private IBrokerStatementService Resolve(BrokerStatementImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (IbFlexBrokerStatementService.IsIbFlexSource(request.Broker))
            return ibFlexService;

        if (string.Equals(Path.GetExtension(request.EffectiveParsePath), ".xml", StringComparison.OrdinalIgnoreCase))
            return ibFlexService;

        return csvService;
    }
}
