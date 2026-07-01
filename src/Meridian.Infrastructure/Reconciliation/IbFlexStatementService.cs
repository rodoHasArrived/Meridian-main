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

        XDocument document;
        try
        {
            document = await LoadDocumentAsync(request.SourcePath, ct).ConfigureAwait(false);
        }
        catch (XmlException ex)
        {
            errors.Add($"Source file is not well-formed XML: {ex.Message}");
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

        if (rowCount == 0)
        {
            errors.Add("Flex report contains no Trade, OpenPosition, or CashTransaction rows; "
                + "include those sections in the Flex Query definition.");
        }

        return new BrokerStatementValidationResult(errors.Count == 0, errors, rowCount);
    }

    public async Task<BrokerStatementImportResult> ImportAsync(
        BrokerStatementImportRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fileBytes = await File.ReadAllBytesAsync(request.SourcePath, ct).ConfigureAwait(false);
        var sourceFileHash = string.IsNullOrWhiteSpace(request.SourceFileHash)
            ? Convert.ToHexString(SHA256.HashData(fileBytes))
            : request.SourceFileHash.Trim().ToUpperInvariant();
        var duplicateKey = StatementDuplicateKey.Create(
            request.FundAccountId,
            request.StatementPeriodStart,
            request.StatementPeriodEnd,
            sourceFileHash);

        if (await store.ImportExistsByDuplicateKeyAsync(duplicateKey, ct).ConfigureAwait(false))
            throw new InvalidOperationException(
                "Statement already imported (fund account, statement period, and source file hash match).");

        var document = await LoadDocumentAsync(request.SourcePath, ct).ConfigureAwait(false);
        if (!string.Equals(document.Root?.Name.LocalName, "FlexQueryResponse", StringComparison.Ordinal))
            throw new InvalidDataException("Root element is not FlexQueryResponse; not an IB Flex Query report.");

        var importId = duplicateKey;
        var normalizedRequest = request.WithSourceFileHash(sourceFileHash);
        var rows = ParseRows(document, importId).ToList();

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
            DuplicateKey = duplicateKey
        };

        await store.SaveImportAsync(import, rows, ct).ConfigureAwait(false);
        return new BrokerStatementImportResult(import, rows);
    }

    private static async Task<XDocument> LoadDocumentAsync(string path, CancellationToken ct)
    {
        // DTD processing stays disabled: Flex reports never carry DTDs, and prohibiting them
        // blocks XXE-style payloads in operator-supplied files.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            Async = true,
            CloseInput = true
        };

        using var reader = XmlReader.Create(File.OpenRead(path), settings);
        return await XDocument.LoadAsync(reader, LoadOptions.None, ct).ConfigureAwait(false);
    }

    private static IEnumerable<CanonicalStatementRow> ParseRows(XDocument document, string importId)
    {
        var rowNumber = 0;
        foreach (var statement in document.Root!.Descendants("FlexStatement"))
        {
            var statementAccount = (string?)statement.Attribute("accountId") ?? string.Empty;
            var statementToDate = ParseFlexDate((string?)statement.Attribute("toDate"));

            foreach (var trade in statement.Descendants("Trade"))
            {
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
                    HashElement(trade));
            }

            foreach (var position in statement.Descendants("OpenPosition"))
            {
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
                    HashElement(position));
            }

            foreach (var cash in statement.Descendants("CashTransaction"))
            {
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
                    HashElement(cash));
            }
        }
    }

    private static string Account(XElement element, string statementAccount) =>
        (string?)element.Attribute("accountId") is { Length: > 0 } account ? account : statementAccount;

    /// <summary>
    /// Maps a Flex CashTransaction <c>type</c> to the canonical activity type so downstream
    /// matching applies fee handling to fee-like rows ("Other Fees", "Advisor Fees", …) instead
    /// of cash tolerance rules. All other cash transaction types (dividends, deposits,
    /// withholding tax, interest) stay canonical <c>cash</c>.
    /// </summary>
    public static string MapCashTransactionActivity(string? flexType) =>
        flexType is not null && flexType.Contains("fee", StringComparison.OrdinalIgnoreCase)
            ? "fee"
            : "cash";

    private static decimal ParseDecimal(XElement element, string attribute)
    {
        var raw = (string?)element.Attribute(attribute);
        return string.IsNullOrWhiteSpace(raw)
            ? 0m
            : decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static decimal ParseFirstDecimal(XElement element, params string[] attributes)
    {
        foreach (var attribute in attributes)
        {
            var raw = (string?)element.Attribute(attribute);
            if (!string.IsNullOrWhiteSpace(raw))
                return decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture);
        }

        return 0m;
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

        if (string.Equals(Path.GetExtension(request.SourcePath), ".xml", StringComparison.OrdinalIgnoreCase))
            return ibFlexService;

        return csvService;
    }
}
