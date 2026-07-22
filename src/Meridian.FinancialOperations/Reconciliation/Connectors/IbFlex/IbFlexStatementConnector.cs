using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;

/// <summary>
/// Interactive Brokers Flex Query (XML) statement connector. Reads the Trades,
/// CashTransactions, and OpenPositions sections of a FlexQueryResponse — every section is
/// optional because operators configure which sections their Flex query emits. Cash
/// transaction types are classified through the data-driven activity-code map in the
/// <c>ib-flex-v1</c> profile, so a new IB cash type is an operator profile edit, not a
/// code change.
/// </summary>
public sealed class IbFlexStatementConnector(StatementMappingProfileCatalog catalog) : IStatementConnector
{
    public const string ConnectorId = "ib-flex";

    private const string FlexRootElement = "FlexQueryResponse";
    private const int MaximumStatementBytes = 32 * 1024 * 1024;
    private const int MaximumStatementRows = 100_000;

    public StatementConnectorDescriptor Descriptor { get; } = new(
        ConnectorId,
        "Interactive Brokers Flex Report (XML)",
        [".xml"],
        SupportsFileImport: true,
        SupportsRemoteFetch: false,
        RequiresMappingProfile: false,
        DefaultProfileId: StatementBuiltInProfiles.IbFlexV1ProfileId);

    public bool CanHandle(StatementSourceDocument document)
    {
        var extension = Path.GetExtension(document.FileName);
        var extensionMatches = Descriptor.FileExtensions.Any(candidate =>
            string.Equals(candidate, extension, StringComparison.OrdinalIgnoreCase));
        var span = document.Content.Span;
        var head = Encoding.UTF8.GetString(span.Length > 1024 ? span[..1024] : span);
        var looksLikeFlex = head.Contains($"<{FlexRootElement}", StringComparison.OrdinalIgnoreCase);
        return looksLikeFlex && (extensionMatches || head.TrimStart().StartsWith("<", StringComparison.Ordinal));
    }

    public async Task<StatementParseResult> ParseAsync(StatementSourceDocument document, CancellationToken ct = default)
    {
        var issues = new List<StatementParseIssue>();
        var profileId = string.IsNullOrWhiteSpace(document.MappingProfileId)
            ? Descriptor.DefaultProfileId!
            : document.MappingProfileId.Trim();
        var profile = await catalog.FindAsync(profileId, ct).ConfigureAwait(false);
        if (profile is null)
        {
            issues.Add(StatementParseIssue.Error("PROFILE_NOT_FOUND", $"Mapping profile '{profileId}' is not registered."));
            return EmptyResult(profileId, issues);
        }

        if (document.Content.Length > MaximumStatementBytes)
        {
            issues.Add(StatementParseIssue.Error(
                "STATEMENT_TOO_LARGE",
                $"The Flex report exceeds the {MaximumStatementBytes}-byte limit."));
            return EmptyResult(profileId, issues);
        }

        XDocument xml;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                Async = true,
                MaxCharactersInDocument = MaximumStatementBytes,
                MaxCharactersFromEntities = 0
            };
            await using var stream = new MemoryStream(document.Content.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, settings);
            xml = await XDocument.LoadAsync(reader, LoadOptions.None, ct).ConfigureAwait(false);
        }
        catch (XmlException ex)
        {
            issues.Add(StatementParseIssue.Error("INVALID_XML", $"The file is not well-formed XML: {ex.Message}", ex.LineNumber));
            return EmptyResult(profileId, issues);
        }

        if (xml.Root is null || !string.Equals(xml.Root.Name.LocalName, FlexRootElement, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(StatementParseIssue.Error(
                "NOT_FLEX_REPORT",
                $"Expected a <{FlexRootElement}> document; found <{xml.Root?.Name.LocalName ?? "empty"}>. Export the statement as an IB Flex Query XML report."));
            return EmptyResult(profileId, issues);
        }

        var statements = xml.Descendants().Where(static element =>
            string.Equals(element.Name.LocalName, "FlexStatement", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (statements.Length == 0)
        {
            issues.Add(StatementParseIssue.Warning("NO_FLEX_STATEMENTS", "The Flex report contains no FlexStatement sections."));
        }

        var activityCodeMap = StatementRecordMapper.BuildActivityCodeMap(profile);
        var reportedUnknownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var detectedColumns = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var records = new List<StatementCanonicalRecord>();
        var rowNumber = 0;
        var sectionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var statement in statements)
        {
            ct.ThrowIfCancellationRequested();
            var statementAccountId = (string?)statement.Attribute("accountId");

            foreach (var trade in Section(statement, "Trades", "Trade"))
            {
                rowNumber++;
                if (rowNumber > MaximumStatementRows)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {MaximumStatementRows}-row limit."));
                    return EmptyResult(profileId, issues);
                }
                CountSection(sectionCounts, "Trades");
                CollectAttributeNames(trade, detectedColumns);
                var values = new Dictionary<StatementCanonicalField, string>
                {
                    [StatementCanonicalField.Account] = Attribute(trade, "accountId") ?? statementAccountId ?? string.Empty,
                    [StatementCanonicalField.SecurityIdentifier] = Attribute(trade, "symbol") ?? string.Empty,
                    [StatementCanonicalField.Quantity] = Attribute(trade, "quantity") ?? string.Empty,
                    [StatementCanonicalField.Price] = Attribute(trade, "tradePrice") ?? string.Empty,
                    [StatementCanonicalField.CashAmount] = Attribute(trade, "netCash") ?? Attribute(trade, "proceeds") ?? string.Empty,
                    [StatementCanonicalField.ActivityType] = "trade",
                    [StatementCanonicalField.TradeDate] = Attribute(trade, "tradeDate") ?? Attribute(trade, "dateTime") ?? string.Empty,
                    [StatementCanonicalField.SettlementDate] = Attribute(trade, "settleDateTarget") ?? string.Empty,
                    [StatementCanonicalField.Currency] = Attribute(trade, "currency") ?? string.Empty,
                    [StatementCanonicalField.FeesCommission] = Attribute(trade, "ibCommission") ?? string.Empty,
                    [StatementCanonicalField.ExternalTransactionId] = Attribute(trade, "tradeID") ?? Attribute(trade, "transactionID") ?? string.Empty
                };
                AddRecord(records, values, profile, activityCodeMap, rowNumber, issues, reportedUnknownCodes);
            }

            foreach (var cash in Section(statement, "CashTransactions", "CashTransaction"))
            {
                rowNumber++;
                if (rowNumber > MaximumStatementRows)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {MaximumStatementRows}-row limit."));
                    return EmptyResult(profileId, issues);
                }
                CountSection(sectionCounts, "CashTransactions");
                CollectAttributeNames(cash, detectedColumns);
                var values = new Dictionary<StatementCanonicalField, string>
                {
                    [StatementCanonicalField.Account] = Attribute(cash, "accountId") ?? statementAccountId ?? string.Empty,
                    [StatementCanonicalField.SecurityIdentifier] = Attribute(cash, "symbol") ?? string.Empty,
                    [StatementCanonicalField.CashAmount] = Attribute(cash, "amount") ?? string.Empty,
                    [StatementCanonicalField.ActivityType] = Attribute(cash, "type") ?? string.Empty,
                    [StatementCanonicalField.TradeDate] = Attribute(cash, "dateTime") ?? Attribute(cash, "reportDate") ?? string.Empty,
                    [StatementCanonicalField.Currency] = Attribute(cash, "currency") ?? string.Empty,
                    [StatementCanonicalField.ExternalTransactionId] = Attribute(cash, "transactionID") ?? string.Empty
                };
                AddRecord(records, values, profile, activityCodeMap, rowNumber, issues, reportedUnknownCodes);
            }

            foreach (var position in Section(statement, "OpenPositions", "OpenPosition"))
            {
                rowNumber++;
                if (rowNumber > MaximumStatementRows)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {MaximumStatementRows}-row limit."));
                    return EmptyResult(profileId, issues);
                }
                CountSection(sectionCounts, "OpenPositions");
                CollectAttributeNames(position, detectedColumns);
                var values = new Dictionary<StatementCanonicalField, string>
                {
                    [StatementCanonicalField.Account] = Attribute(position, "accountId") ?? statementAccountId ?? string.Empty,
                    [StatementCanonicalField.SecurityIdentifier] = Attribute(position, "symbol") ?? string.Empty,
                    [StatementCanonicalField.Quantity] = Attribute(position, "position") ?? string.Empty,
                    [StatementCanonicalField.Price] = Attribute(position, "markPrice") ?? string.Empty,
                    [StatementCanonicalField.CashAmount] = Attribute(position, "positionValue") ?? string.Empty,
                    [StatementCanonicalField.ActivityType] = "position",
                    [StatementCanonicalField.TradeDate] = Attribute(position, "reportDate") ?? string.Empty,
                    [StatementCanonicalField.Currency] = Attribute(position, "currency") ?? string.Empty
                };
                AddRecord(records, values, profile, activityCodeMap, rowNumber, issues, reportedUnknownCodes);
            }
        }

        // An advisor Flex report can carry several accounts across FlexStatement sections, but a
        // statement run reconciles a single account and the matcher normalizes every row to the run's
        // one external account. Committing a multi-account report would compare one account's rows
        // against another account's Meridian records, so reject it: split into one document per account.
        var distinctAccounts = records
            .Select(static record => record.Account)
            .Where(static account => !string.IsNullOrWhiteSpace(account))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (distinctAccounts.Length > 1)
        {
            issues.Add(StatementParseIssue.Error(
                "IBFLEX_MULTIPLE_ACCOUNTS",
                $"The Flex report contains records for {distinctAccounts.Length} different accounts, but a statement run reconciles a single account. Split the report into one document per account before importing."));
            return EmptyResult(profileId, issues);
        }

        if (records.Count == 0 && statements.Length > 0)
        {
            issues.Add(StatementParseIssue.Warning(
                "NO_RECORDS",
                "The Flex report yielded no trades, cash transactions, or open positions. Check that the Flex query includes the Trades, Cash Transactions, or Open Positions sections."));
        }
        else if (sectionCounts.Count > 0)
        {
            issues.Add(StatementParseIssue.Info(
                "FLEX_SECTIONS",
                $"Flex sections imported: {string.Join(", ", sectionCounts.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(static pair => $"{pair.Key}={pair.Value}"))}."));
        }

        var detected = detectedColumns.ToArray();
        return new StatementParseResult(
            ConnectorId,
            profile.ProfileId,
            detected,
            StatementColumnConfidenceScorer.MapColumns(detected, profile),
            records,
            issues,
            new StatementFormatFingerprint(
                Convert.ToHexString(SHA256.HashData(document.Content.Span)).ToLowerInvariant(),
                detected.Select(static column => column.ToLowerInvariant()).ToArray(),
                "xml"));
    }

    private static void AddRecord(
        List<StatementCanonicalRecord> records,
        Dictionary<StatementCanonicalField, string> values,
        StatementMappingProfileDocument profile,
        IReadOnlyDictionary<string, string> activityCodeMap,
        int rowNumber,
        List<StatementParseIssue> issues,
        HashSet<string> reportedUnknownCodes)
    {
        var record = StatementRecordMapper.MapRecord(values, profile, activityCodeMap, rowNumber, issues, reportedUnknownCodes);
        if (record is not null)
        {
            records.Add(record);
        }
    }

    private static IEnumerable<XElement> Section(XElement statement, string sectionName, string elementName)
        => statement.Elements()
            .Where(element => string.Equals(element.Name.LocalName, sectionName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(section => section.Elements()
                .Where(element => string.Equals(element.Name.LocalName, elementName, StringComparison.OrdinalIgnoreCase)));

    private static string? Attribute(XElement element, string name)
    {
        var value = element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void CollectAttributeNames(XElement element, ISet<string> columns)
    {
        foreach (var attribute in element.Attributes())
        {
            columns.Add(attribute.Name.LocalName);
        }
    }

    private static void CountSection(Dictionary<string, int> counts, string section)
        => counts[section] = counts.TryGetValue(section, out var current) ? current + 1 : 1;

    private static StatementParseResult EmptyResult(string? profileId, IReadOnlyList<StatementParseIssue> issues)
        => new(ConnectorId, profileId, [], [], [], issues, new StatementFormatFingerprint(string.Empty, [], "xml"));
}
