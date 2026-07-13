using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed class StatementReconciliationService
{
    private readonly StatementBreakClassifier _breakClassifier = new();
    private readonly StatementMappingProfileRegistry _mappingProfiles;

    public StatementReconciliationService(StatementMappingProfileRegistry? mappingProfiles = null)
    {
        _mappingProfiles = mappingProfiles ?? StatementMappingProfileRegistry.Defaults;
    }

    public Task<string> ValidateAsync(string sourceKind, string sourcePath, CancellationToken ct) =>
        ValidateAsync(sourceKind, sourcePath, mappingProfileId: null, ct: ct);

    public Task<string> ValidateAsync(string sourceKind, string sourcePath, string? mappingProfileId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        var profileId = mappingProfileId;
        if (UsesFlexProcessing(normalizedSourceKind, sourcePath))
        {
            // Flex reports are XML; the canonical CSV header check does not apply. Validate the
            // document shape instead so the workflow rejects non-Flex files before import.
            ValidateFlexDocument(sourcePath);
            profileId = string.IsNullOrWhiteSpace(profileId)
                ? StatementMappingProfileRegistry.IbFlexV1ProfileId
                : profileId;
        }
        else if (UsesCanonicalSchema(normalizedSourceKind, profileId))
        {
            var profile = ValidateStatementHeader(normalizedSourceKind, sourcePath, profileId);
            profileId = profile.ProfileId;
        }

        var profileSuffix = string.IsNullOrWhiteSpace(profileId) ? string.Empty : $" using mapping profile '{profileId}'";
        return Task.FromResult($"Statement source '{normalizedSourceKind}:{sourcePath}' passed local file accessibility checks{profileSuffix}.");
    }

    public Task<NormalizedStatementImportResult> ImportAsync(string sourceKind, string sourcePath, CancellationToken ct) =>
        ImportAsync(sourceKind, sourcePath, mappingProfileId: null, ct: ct);

    public async Task<NormalizedStatementImportResult> ImportAsync(string sourceKind, string sourcePath, string? mappingProfileId, CancellationToken ct)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        ct.ThrowIfCancellationRequested();
        if (UsesFlexProcessing(normalizedSourceKind, sourcePath))
        {
            return await ReadFlexStatementImportAsync(normalizedSourceKind, sourcePath, ct).ConfigureAwait(false);
        }

        if (UsesCanonicalSchema(normalizedSourceKind, mappingProfileId))
        {
            return await ReadNormalizedStatementImportAsync(normalizedSourceKind, sourcePath, mappingProfileId, ct).ConfigureAwait(false);
        }

        var content = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
        var id = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}|{content}");
        var sourceRows = File.ReadLines(sourcePath)
            .Skip(1)
            .Select((line, index) => CreateSourceRowReference(id, index + 1, line, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sourceKind"] = normalizedSourceKind,
                ["sourcePath"] = sourcePath,
                ["rawLine"] = line
            }))
            .ToList();

        return new NormalizedStatementImportResult(id, normalizedSourceKind, sourcePath, sourceRows.Count, [], [], [], [], sourceRows);
    }

    public Task<(string ImportId, int MatchCount, int UnresolvedCount)> ReconcileAsync(string sourceKind, string sourcePath, CancellationToken ct) =>
        ReconcileAsync(sourceKind, sourcePath, mappingProfileId: null, ct: ct);

    public Task<(string ImportId, int MatchCount, int UnresolvedCount)> ReconcileAsync(string sourceKind, string sourcePath, string? mappingProfileId, CancellationToken ct)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        ct.ThrowIfCancellationRequested();
        var intake = CreateExternalStatementCases(normalizedSourceKind, sourcePath, mappingProfileId);
        return Task.FromResult((intake.ImportId, intake.MatchCount, intake.Cases.Count));
    }

    public Task<ExternalStatementCaseIntakeResult> CreateExternalStatementCasesAsync(
        string sourceKind,
        string sourcePath,
        CancellationToken ct = default) =>
        CreateExternalStatementCasesAsync(sourceKind, sourcePath, mappingProfileId: null, ct: ct);

    public Task<ExternalStatementCaseIntakeResult> CreateExternalStatementCasesAsync(
        string sourceKind,
        string sourcePath,
        string? mappingProfileId,
        CancellationToken ct = default)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(CreateExternalStatementCases(normalizedSourceKind, sourcePath, mappingProfileId));
    }

    private static string ValidateSourceAccess(string sourceKind, string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourceKind))
            throw new ArgumentException("Statement source kind is required.", nameof(sourceKind));

        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Statement source path is required.", nameof(sourcePath));

        var normalizedSourceKind = sourceKind.Trim().ToLowerInvariant();
        if (IsIbFlexSourceKind(normalizedSourceKind))
        {
            normalizedSourceKind = IbFlexSourceKind;
        }
        else if (!string.Equals(normalizedSourceKind, "local", StringComparison.Ordinal)
            && !string.Equals(normalizedSourceKind, "broker", StringComparison.Ordinal)
            && !string.Equals(normalizedSourceKind, "custodian", StringComparison.Ordinal)
            && !string.Equals(normalizedSourceKind, "sample-broker", StringComparison.Ordinal))
        {
            throw new NotSupportedException($"Statement source kind '{sourceKind}' is not supported. Use 'local', 'broker', 'custodian', 'sample-broker', or 'ib-flex'.");
        }

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Statement source file '{sourcePath}' was not found.", sourcePath);

        return normalizedSourceKind;
    }

    private const string IbFlexSourceKind = "ib-flex";

    private static bool IsIbFlexSourceKind(string normalizedSourceKind) =>
        normalizedSourceKind is "ib-flex" or "ibflex" or "ibkr" or "interactive-brokers" or "interactivebrokers";

    // Mirrors RoutingBrokerStatementService: an explicit Flex source kind always uses Flex
    // processing, and a canonical broker/custodian kind with an .xml file routes to Flex too,
    // so the workflow's validation stage agrees with where the import router will send the file.
    private static bool UsesFlexProcessing(string normalizedSourceKind, string sourcePath) =>
        IsIbFlexSourceKind(normalizedSourceKind)
        || (RequiresCanonicalStatementSchema(normalizedSourceKind)
            && string.Equals(Path.GetExtension(sourcePath), ".xml", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Validates that <paramref name="sourcePath"/> is a well-formed IB Flex Query document
    /// (root element <c>FlexQueryResponse</c>) without loading the full report.
    /// </summary>
    private static void ValidateFlexDocument(string sourcePath)
    {
        var settings = new System.Xml.XmlReaderSettings
        {
            DtdProcessing = System.Xml.DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = true
        };

        try
        {
            using var reader = System.Xml.XmlReader.Create(File.OpenRead(sourcePath), settings);
            reader.MoveToContent();
            if (!string.Equals(reader.LocalName, "FlexQueryResponse", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Statement source '{sourcePath}' is not an IB Flex Query report (root element '{reader.LocalName}').");
            }
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidDataException($"Statement source '{sourcePath}' is not well-formed XML: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads an IB Flex report into normalized statement rows (one per Trade, OpenPosition,
    /// and CashTransaction element) so case intake matches Flex statements with the same
    /// engine as canonical CSV rows. Fee-like cash transactions keep fee row semantics via
    /// <see cref="Infrastructure.Reconciliation.IbFlexBrokerStatementService.MapCashTransactionActivity"/>.
    /// </summary>
    private static IReadOnlyList<NormalizedStatementRow> ReadFlexStatementRows(
        string importId,
        string normalizedSourceKind,
        string sourcePath,
        string content)
    {
        var document = System.Xml.Linq.XDocument.Parse(content);
        var rows = new List<NormalizedStatementRow>();
        var rowNumber = 0;

        foreach (var statement in document.Descendants("FlexStatement"))
        {
            var statementToDate = Infrastructure.Reconciliation.IbFlexBrokerStatementService
                .ParseFlexDate((string?)statement.Attribute("toDate"));

            foreach (var element in statement.Descendants()
                         .Where(static e => e.Name.LocalName is "Trade" or "OpenPosition" or "CashTransaction"))
            {
                rowNumber++;
                var (activityType, quantity, amount, date) = element.Name.LocalName switch
                {
                    "Trade" => (
                        "trade",
                        FlexDecimal(element, "quantity"),
                        FlexFirstDecimal(element, "netCash", "proceeds") is var cash && cash != 0m
                            ? cash
                            : FlexDecimal(element, "quantity") * FlexDecimal(element, "tradePrice"),
                        FlexFirstDate(element, ["tradeDate"], statementToDate)),
                    "OpenPosition" => (
                        "position",
                        FlexDecimal(element, "position"),
                        FlexFirstDecimal(element, "positionValue") is var value && value != 0m
                            ? value
                            : FlexDecimal(element, "position") * FlexFirstDecimal(element, "markPrice", "costBasisPrice"),
                        FlexFirstDate(element, ["reportDate"], statementToDate)),
                    _ => (
                        Infrastructure.Reconciliation.IbFlexBrokerStatementService
                            .MapCashTransactionActivity((string?)element.Attribute("type")),
                        0m,
                        FlexDecimal(element, "amount"),
                        FlexFirstDate(element, ["dateTime", "reportDate", "settleDate"], statementToDate))
                };

                var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["importId"] = importId,
                    ["sourceKind"] = normalizedSourceKind,
                    ["sourcePath"] = sourcePath,
                    ["elementName"] = element.Name.LocalName,
                    ["activityType"] = activityType,
                    ["rowNumber"] = rowNumber.ToString(CultureInfo.InvariantCulture)
                };
                foreach (var attribute in element.Attributes())
                {
                    snapshot[attribute.Name.LocalName] = attribute.Value;
                }

                var elementText = element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
                rows.Add(new NormalizedStatementRow(
                    $"{importId}:{rowNumber}",
                    ToStatementRowKind(activityType),
                    (string?)element.Attribute("symbol") ?? string.Empty,
                    quantity,
                    amount,
                    new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    (string?)element.Attribute("currency") ?? "USD",
                    DeterministicFingerprint.Compute($"{importId}|{rowNumber}|{elementText}"),
                    snapshot));
            }
        }

        return rows;
    }

    private static decimal FlexDecimal(System.Xml.Linq.XElement element, string attribute)
    {
        var raw = (string?)element.Attribute(attribute);
        return string.IsNullOrWhiteSpace(raw)
            ? 0m
            : decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture);
    }

    private static decimal FlexFirstDecimal(System.Xml.Linq.XElement element, params string[] attributes)
    {
        foreach (var attribute in attributes)
        {
            var raw = (string?)element.Attribute(attribute);
            if (!string.IsNullOrWhiteSpace(raw))
                return decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture);
        }

        return 0m;
    }

    private static DateOnly FlexFirstDate(System.Xml.Linq.XElement element, string[] attributes, DateOnly? fallback)
    {
        foreach (var attribute in attributes)
        {
            if (Infrastructure.Reconciliation.IbFlexBrokerStatementService
                    .ParseFlexDate((string?)element.Attribute(attribute)) is { } parsed)
                return parsed;
        }

        return fallback ?? throw new InvalidDataException(
            "Flex row has no parseable date attribute and the statement has no toDate fallback.");
    }

    /// <summary>
    /// Reads an IB Flex report into source-row references (one per Trade, OpenPosition, and
    /// CashTransaction element, with the element's attributes as the raw snapshot) so the
    /// checkpointed ingestion stage reports real row counts. Canonical row construction for
    /// the workflow path is owned by the Flex-aware broker statement importer.
    /// </summary>
    private static async Task<NormalizedStatementImportResult> ReadFlexStatementImportAsync(
        string normalizedSourceKind,
        string sourcePath,
        CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
        var importId = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}|{content}");

        var document = System.Xml.Linq.XDocument.Parse(content);
        var sourceRows = new List<StatementSourceRowReference>();
        var rowNumber = 0;
        foreach (var element in document.Descendants()
                     .Where(static e => e.Name.LocalName is "Trade" or "OpenPosition" or "CashTransaction"))
        {
            ct.ThrowIfCancellationRequested();
            rowNumber++;
            var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sourceKind"] = normalizedSourceKind,
                ["sourcePath"] = sourcePath,
                ["elementName"] = element.Name.LocalName
            };
            foreach (var attribute in element.Attributes())
            {
                snapshot[attribute.Name.LocalName] = attribute.Value;
            }

            sourceRows.Add(CreateSourceRowReference(
                importId,
                rowNumber,
                element.ToString(System.Xml.Linq.SaveOptions.DisableFormatting),
                snapshot));
        }

        return new NormalizedStatementImportResult(importId, normalizedSourceKind, sourcePath, sourceRows.Count, [], [], [], [], sourceRows);
    }

    private static bool RequiresCanonicalStatementSchema(string normalizedSourceKind) =>
        string.Equals(normalizedSourceKind, "broker", StringComparison.Ordinal)
        || string.Equals(normalizedSourceKind, "custodian", StringComparison.Ordinal)
        || string.Equals(normalizedSourceKind, "sample-broker", StringComparison.Ordinal);

    // A source is parsed through the canonical, mapping-profile-driven path when its kind always
    // requires the canonical schema, OR when the caller explicitly selects a mapping profile.
    // The latter makes operator-supplied ('local') statements reconcilable via a chosen profile,
    // while a 'local' source with no profile keeps its lenient raw-passthrough behavior.
    private static bool UsesCanonicalSchema(string normalizedSourceKind, string? mappingProfileId) =>
        RequiresCanonicalStatementSchema(normalizedSourceKind)
        || !string.IsNullOrWhiteSpace(mappingProfileId);

    private ExternalStatementCaseIntakeResult CreateExternalStatementCases(string normalizedSourceKind, string sourcePath, string? mappingProfileId = null)
    {
        // Flex XML never parses through the canonical CSV path, regardless of any mapping
        // profile the caller selected; its rows are read from the XML sections and matched
        // with the same engine so CLI/orchestrator intake reports real match/unresolved counts.
        if (UsesFlexProcessing(normalizedSourceKind, sourcePath))
        {
            var flexContent = File.ReadAllText(sourcePath);
            var flexImportId = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}|{flexContent}");
            var flexRows = ReadFlexStatementRows(flexImportId, normalizedSourceKind, sourcePath, flexContent);
            var (flexMatches, flexCases) = MatchRows(flexRows);
            return new ExternalStatementCaseIntakeResult(
                flexImportId,
                normalizedSourceKind,
                sourcePath,
                flexRows.Count,
                flexMatches.Count,
                flexCases);
        }

        if (!UsesCanonicalSchema(normalizedSourceKind, mappingProfileId))
        {
            var content = File.ReadAllText(sourcePath);
            var importId = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}|{content}");
            return new ExternalStatementCaseIntakeResult(importId, normalizedSourceKind, sourcePath, 0, 0, []);
        }

        var rows = ReadCanonicalStatementRows(normalizedSourceKind, sourcePath, mappingProfileId);
        var (matches, cases) = MatchRows(rows);
        // Compute the import id from the resolved profile and file content, identical to the import
        // path (and to ReadCanonicalStatementRows for non-empty files), so import and reconcile/intake
        // refer to the same run even for a valid but empty (header-only) statement.
        var profile = _mappingProfiles.ResolveForSourceKind(normalizedSourceKind, mappingProfileId);
        var canonicalContent = File.ReadAllText(sourcePath);
        var canonicalImportId = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{profile.ProfileId}|{sourcePath}|{canonicalContent}");
        return new ExternalStatementCaseIntakeResult(
            canonicalImportId,
            normalizedSourceKind,
            sourcePath,
            rows.Count,
            matches.Count,
            cases);
    }

    private async Task<NormalizedStatementImportResult> ReadNormalizedStatementImportAsync(
        string normalizedSourceKind,
        string sourcePath,
        string? mappingProfileId,
        CancellationToken ct)
    {
        var profile = ValidateStatementHeader(normalizedSourceKind, sourcePath, mappingProfileId);
        var header = File.ReadLines(sourcePath).First().Split(',', StringSplitOptions.TrimEntries);

        var content = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
        var importId = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{profile.ProfileId}|{sourcePath}|{content}");
        var positions = new List<StatementPosition>();
        var cashBalances = new List<StatementCashBalance>();
        var transactions = new List<StatementTransaction>();
        var securities = new List<StatementSecurityReference>();
        var sourceRows = new List<StatementSourceRowReference>();
        var lines = File.ReadLines(sourcePath).Skip(1);
        var rowNumber = 1;
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var p = ParseCanonicalStatementLine(line, rowNumber);
            sourceRows.Add(p.SourceRow);
            if (!string.IsNullOrWhiteSpace(p.Security.UnresolvedIdentifier) || !string.IsNullOrWhiteSpace(p.Security.SecurityId))
            {
                securities.Add(p.Security);
            }

            switch (p.RowKind)
            {
                case StatementRowKind.Position:
                    positions.Add(new StatementPosition(
                        importId,
                        rowNumber,
                        p.SourceRow.SourceRowHash,
                        p.RawSnapshot,
                        p.AccountId,
                        p.ExternalAccountId,
                        p.Security.SecurityId,
                        p.Security.UnresolvedIdentifier,
                        p.Currency,
                        p.Quantity,
                        p.Price,
                        p.MarketValue,
                        p.TradeDate,
                        p.SettlementDate));
                    break;
                case StatementRowKind.CashBalance:
                    cashBalances.Add(new StatementCashBalance(
                        importId,
                        rowNumber,
                        p.SourceRow.SourceRowHash,
                        p.RawSnapshot,
                        p.AccountId,
                        p.ExternalAccountId,
                        p.Currency,
                        p.Amount,
                        p.TradeDate,
                        p.SettlementDate));
                    break;
                default:
                    transactions.Add(new StatementTransaction(
                        importId,
                        rowNumber,
                        p.SourceRow.SourceRowHash,
                        p.RawSnapshot,
                        p.AccountId,
                        p.ExternalAccountId,
                        p.Security.SecurityId,
                        p.Security.UnresolvedIdentifier,
                        p.Currency,
                        p.Quantity,
                        p.Price,
                        p.MarketValue,
                        p.TradeDate,
                        p.SettlementDate,
                        p.Amount,
                        p.FeesCommission,
                        p.TransactionType,
                        p.ExternalReference));
                    break;
            }
        }

        return new NormalizedStatementImportResult(importId, normalizedSourceKind, sourcePath, sourceRows.Count, positions, cashBalances, transactions, securities, sourceRows);

        ParsedStatementLine ParseCanonicalStatementLine(string line, int currentRowNumber)
        {
            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            var requiredColumnCount = RequiredColumnCount(profile, header);
            if (parts.Length < requiredColumnCount)
            {
                throw new InvalidDataException($"Statement row {currentRowNumber} has {parts.Length} columns; expected at least {requiredColumnCount} required columns for mapping profile '{profile.ProfileId}'.");
            }

            var mapped = new StatementMappedCsvRow(profile, BuildColumnMap(header, parts));
            var account = mapped.GetRequired(StatementCanonicalField.Account, currentRowNumber);
            // Classify the row kind from the mapped (canonical) activity, but keep the original source
            // activity code for the transaction so direction-bearing codes (e.g. BUY/SELL, which a
            // profile may both map to "trade") are not lost for downstream matching/classification.
            var sourceActivityType = mapped.GetRequired(StatementCanonicalField.ActivityType, currentRowNumber);
            var activityType = profile.MapActivityType(sourceActivityType);
            var rowKind = ToStatementRowKind(activityType);
            // Position rows must carry a security identifier so the matching engine can compare
            // them against internal positions by security; a blank one is a mapping error, not a
            // matchable position. Account-level cash/fee/dividend and other activity rows may omit
            // it, matching the prior positional importer.
            var symbol = rowKind == StatementRowKind.Position
                ? mapped.GetRequired(StatementCanonicalField.SecurityIdentifier, currentRowNumber)
                : mapped.GetOptional(StatementCanonicalField.SecurityIdentifier) ?? string.Empty;
            var quantity = mapped.GetRequiredDecimal(StatementCanonicalField.Quantity, currentRowNumber);
            var price = mapped.GetRequiredDecimal(StatementCanonicalField.Price, currentRowNumber);
            var cashAmount = mapped.GetRequiredDecimal(StatementCanonicalField.CashAmount, currentRowNumber);
            var tradeDate = mapped.GetRequiredDate(StatementCanonicalField.TradeDate, currentRowNumber);
            var settlementDate = mapped.GetOptionalDate(StatementCanonicalField.SettlementDate);
            var currency = mapped.GetOptional(StatementCanonicalField.Currency) ?? "USD";
            var marketValue = mapped.GetOptionalDecimal(StatementCanonicalField.MarketValue) ?? price * quantity;
            var amount = mapped.GetOptionalDecimal(StatementCanonicalField.Amount) ?? (cashAmount == 0m ? marketValue : cashAmount);
            var feesCommission = mapped.GetOptionalDecimal(StatementCanonicalField.FeesCommission) ?? 0m;
            var externalReference = mapped.GetOptional(StatementCanonicalField.ExternalReference)
                ?? mapped.GetOptional(StatementCanonicalField.ExternalTransactionId);
            var securityId = mapped.GetOptional(StatementCanonicalField.SecurityId);
            var unresolvedIdentifier = mapped.GetOptional(StatementCanonicalField.UnresolvedIdentifier)
                ?? (string.IsNullOrWhiteSpace(symbol) ? null : symbol);
            var accountId = mapped.GetOptional(StatementCanonicalField.AccountId) ?? account;
            var externalAccountId = mapped.GetOptional(StatementCanonicalField.ExternalAccountId) ?? account;

            var snapshot = mapped.ToCanonicalSnapshot();
            snapshot["importId"] = importId;
            snapshot["sourceKind"] = normalizedSourceKind;
            snapshot["sourcePath"] = sourcePath;
            snapshot["mappingProfileId"] = profile.ProfileId;
            snapshot["account"] = account;
            snapshot["symbol"] = symbol;
            snapshot["activityType"] = activityType;
            snapshot["tradeDate"] = tradeDate.ToString("O");
            snapshot["rowNumber"] = currentRowNumber.ToString(CultureInfo.InvariantCulture);
            snapshot["rawLine"] = line;
            var sourceRow = CreateSourceRowReference(importId, currentRowNumber, line, snapshot);
            var security = new StatementSecurityReference(importId, currentRowNumber, sourceRow.SourceRowHash, snapshot, securityId, unresolvedIdentifier, currency);

            return new ParsedStatementLine(
                sourceRow,
                snapshot,
                security,
                accountId,
                externalAccountId,
                currency,
                quantity,
                price,
                marketValue,
                tradeDate,
                settlementDate,
                amount,
                feesCommission,
                rowKind,
                sourceActivityType,
                externalReference);
        }
    }

    private IReadOnlyList<NormalizedStatementRow> ReadCanonicalStatementRows(string normalizedSourceKind, string sourcePath, string? mappingProfileId = null)
    {
        var profile = ValidateStatementHeader(normalizedSourceKind, sourcePath, mappingProfileId);

        var content = File.ReadAllText(sourcePath);
        var importId = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{profile.ProfileId}|{sourcePath}|{content}");
        var rows = new List<NormalizedStatementRow>();
        var header = File.ReadLines(sourcePath).First().Split(',', StringSplitOptions.TrimEntries);
        var lines = File.ReadLines(sourcePath).Skip(1);
        var rowNumber = 1;
        foreach (var line in lines)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            var requiredColumnCount = RequiredColumnCount(profile, header);
            if (parts.Length < requiredColumnCount)
            {
                throw new InvalidDataException($"Statement row {rowNumber} has {parts.Length} columns; expected at least {requiredColumnCount} required columns for mapping profile '{profile.ProfileId}'.");
            }

            var mapped = new StatementMappedCsvRow(profile, BuildColumnMap(header, parts));
            var account = mapped.GetRequired(StatementCanonicalField.Account, rowNumber);
            var activityType = profile.MapActivityType(mapped.GetRequired(StatementCanonicalField.ActivityType, rowNumber));
            var rowKind = ToStatementRowKind(activityType);
            // Position rows must carry a security identifier (see import path); account-level
            // cash/fee/dividend and other activity rows may omit it. Kept consistent across both paths.
            var symbol = rowKind == StatementRowKind.Position
                ? mapped.GetRequired(StatementCanonicalField.SecurityIdentifier, rowNumber)
                : mapped.GetOptional(StatementCanonicalField.SecurityIdentifier) ?? string.Empty;
            var quantity = mapped.GetRequiredDecimal(StatementCanonicalField.Quantity, rowNumber);
            var price = mapped.GetRequiredDecimal(StatementCanonicalField.Price, rowNumber);
            var cashAmount = mapped.GetRequiredDecimal(StatementCanonicalField.CashAmount, rowNumber);
            var tradeDate = mapped.GetRequiredDate(StatementCanonicalField.TradeDate, rowNumber);
            var settlementDate = mapped.GetOptional(StatementCanonicalField.SettlementDate);
            var currency = mapped.GetOptional(StatementCanonicalField.Currency) ?? "USD";
            var feesCommission = mapped.GetOptional(StatementCanonicalField.FeesCommission);
            var externalTransactionId = mapped.GetOptional(StatementCanonicalField.ExternalTransactionId);
            // Economic amount, derived consistently with the import path: prefer an explicit mapped
            // amount, then a non-zero cash amount, then market value (or price * quantity), so a row
            // carrying its value in the optional amount column is not classified as zero on intake.
            var amount = mapped.GetOptionalDecimal(StatementCanonicalField.Amount)
                ?? (cashAmount == 0m
                    ? mapped.GetOptionalDecimal(StatementCanonicalField.MarketValue) ?? price * quantity
                    : cashAmount);
            var rowFingerprint = DeterministicFingerprint.Compute($"{importId}|{rowNumber}|{line}");
            var rawSnapshot = mapped.ToCanonicalSnapshot();
            rawSnapshot["importId"] = importId;
            rawSnapshot["sourceKind"] = normalizedSourceKind;
            rawSnapshot["sourcePath"] = sourcePath;
            rawSnapshot["mappingProfileId"] = profile.ProfileId;
            rawSnapshot["account"] = account;
            rawSnapshot["symbol"] = symbol;
            rawSnapshot["activityType"] = activityType;
            rawSnapshot["tradeDate"] = tradeDate.ToString("O");
            rawSnapshot["rowNumber"] = rowNumber.ToString(CultureInfo.InvariantCulture);
            rawSnapshot["rawLine"] = line;
            if (!string.IsNullOrWhiteSpace(settlementDate))
                rawSnapshot["settlementDate"] = settlementDate;
            if (!string.IsNullOrWhiteSpace(currency))
                rawSnapshot["currency"] = currency;
            if (!string.IsNullOrWhiteSpace(feesCommission))
                rawSnapshot["feesCommission"] = feesCommission;
            if (!string.IsNullOrWhiteSpace(externalTransactionId))
                rawSnapshot["externalTransactionId"] = externalTransactionId;

            rows.Add(new NormalizedStatementRow(
                $"{importId}:{rowNumber}",
                rowKind,
                symbol,
                quantity,
                amount,
                new DateTimeOffset(tradeDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                currency,
                rowFingerprint,
                rawSnapshot));
        }

        return rows;
    }


    private StatementMappingProfile ValidateStatementHeader(string normalizedSourceKind, string sourcePath, string? mappingProfileId = null)
    {
        var profile = _mappingProfiles.ResolveForSourceKind(normalizedSourceKind, mappingProfileId);
        var header = File.ReadLines(sourcePath).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header))
        {
            throw new InvalidDataException("Statement source file is empty.");
        }

        var actual = header.Split(',', StringSplitOptions.TrimEntries);
        EnsureUniqueStatementHeaderColumns(actual, profile.ProfileId);
        if (profile.ProfileId.Equals(StatementMappingProfileRegistry.CanonicalCsvV1ProfileId, StringComparison.OrdinalIgnoreCase)
            && !CanonicalCsvHeaderPrefixMatches(actual))
        {
            throw new InvalidDataException("Statement source must use the canonical external statement header: account,symbol,quantity,price,cashAmount,activityType,tradeDate.");
        }

        var actualColumns = actual.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingColumns = profile.FieldMappings
            .Where(mapping => mapping.Required && !actualColumns.Contains(mapping.SourceColumn))
            .Select(mapping => mapping.SourceColumn)
            .ToArray();
        if (missingColumns.Length > 0)
        {
            throw new InvalidDataException($"Statement source is missing required columns for mapping profile '{profile.ProfileId}': {string.Join(", ", missingColumns)}.");
        }

        return profile;
    }

    private static void EnsureUniqueStatementHeaderColumns(string[] header, string profileId)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new List<string>();
        foreach (var column in header.Where(column => !string.IsNullOrWhiteSpace(column)))
        {
            if (!seen.Add(column) && !duplicates.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                duplicates.Add(column);
            }
        }

        if (duplicates.Count > 0)
        {
            throw new InvalidDataException($"Statement source contains duplicate columns for mapping profile '{profileId}': {string.Join(", ", duplicates)}.");
        }
    }

    private static bool CanonicalCsvHeaderPrefixMatches(string[] actual)
    {
        var canonicalColumns = StatementMappingProfileRegistry.Defaults
            .Resolve(StatementMappingProfileRegistry.CanonicalCsvV1ProfileId)
            .FieldMappings
            .Where(mapping => mapping.Required)
            .Select(mapping => mapping.SourceColumn)
            .ToArray();
        return actual.Length >= canonicalColumns.Length
            && canonicalColumns.SequenceEqual(actual.Take(canonicalColumns.Length), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> BuildColumnMap(string[] header, string[] parts)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            if (!map.TryAdd(header[i], i < parts.Length ? parts[i] : string.Empty))
            {
                throw new InvalidDataException($"Statement source contains duplicate column '{header[i]}'.");
            }
        }

        return map;
    }

    // The number of leading columns a data row must contain to cover every required mapped field.
    // Optional trailing columns may be omitted by an individual row (BuildColumnMap pads them empty);
    // such rows default their optional values rather than being rejected for being shorter than the
    // full header.
    private static int RequiredColumnCount(StatementMappingProfile profile, string[] header)
    {
        var requiredColumns = profile.FieldMappings
            .Where(mapping => mapping.Required)
            .Select(mapping => mapping.SourceColumn)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var lastRequiredIndex = -1;
        for (var i = 0; i < header.Length; i++)
        {
            if (requiredColumns.Contains(header[i]))
            {
                lastRequiredIndex = i;
            }
        }

        return lastRequiredIndex + 1;
    }

    private static StatementSourceRowReference CreateSourceRowReference(
        string importId,
        int rowNumber,
        string line,
        IReadOnlyDictionary<string, string> rawSnapshot)
        => new(importId, rowNumber, DeterministicFingerprint.Compute($"{importId}|{rowNumber}|{line}"), rawSnapshot);

    private static StatementRowKind ToStatementRowKind(string activityType)
    {
        if (activityType.Equals("position", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRowKind.Position;
        }

        if (activityType.Equals("cash", StringComparison.OrdinalIgnoreCase)
            || activityType.Equals("cashbalance", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRowKind.CashBalance;
        }

        if (activityType.Equals("fee", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRowKind.Fee;
        }

        if (activityType.Equals("dividend", StringComparison.OrdinalIgnoreCase)
            || activityType.Equals("div", StringComparison.OrdinalIgnoreCase))
        {
            return StatementRowKind.Dividend;
        }

        return StatementRowKind.Transaction;
    }

    /// <summary>
    /// Matches normalized statement rows into match links and reconciliation cases. Position rows
    /// are matched by the shared <see cref="StatementMatchingEngine"/> against the supplied
    /// internal portfolio positions (none by default for file-only intake); rows the engine cannot
    /// match at exact or tolerance tier fall through to break classification and casework, the
    /// same as every other row kind.
    /// </summary>
    public (IReadOnlyList<ReconciliationMatchLink> Matches, IReadOnlyList<ReconciliationCase> Cases) MatchRows(
        IReadOnlyList<NormalizedStatementRow> rows,
        IReadOnlyList<InternalPortfolioPosition>? internalPositions = null)
    {
        var matches = new List<ReconciliationMatchLink>();
        var cases = new List<ReconciliationCase>();
        var positionMatchLinks = MatchPositionRows(rows, internalPositions ?? []);

        foreach (var row in rows)
        {
            if (row.Kind == StatementRowKind.Position &&
                positionMatchLinks.TryGetValue(row.RowId, out var positionMatch))
            {
                matches.Add(positionMatch);
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            var classification = _breakClassifier.Classify(StatementBreakClassificationRequest.FromStatementRow(row));
            if (!classification.ShouldCreateCaseQueueItem)
            {
                continue;
            }

            var explanation = BuildBreakExplanation(row);
            var attachment = BuildStatementAttachment(row, explanation.EvidenceLinks.FirstOrDefault());
            var evidenceRef = $"statement-row:{row.RowId}";
            cases.Add(new ReconciliationCase(
                $"case:{row.RowId}",
                row.RawSnapshot.GetValueOrDefault("importId", row.Fingerprint),
                "Open",
                explanation.Summary,
                0.35m,
                classification.WorkflowRationale,
                now,
                [new ReconciliationCaseHistoryEntry(DateTimeOffset.UtcNow, "None", "Open", "Case created from statement reconciliation service.") { EvidenceId = evidenceRef }])
            {
                EvidenceReferences = [evidenceRef],
                Owner = "fund-ops",
                Priority = ToCasePriority(classification.Severity),
                DueAtUtc = now.AddBusinessDays(2),
                Disposition = "NeedsInvestigation",
                AgingDays = 0,
                Attachments = [attachment],
                BreakExplanation = explanation,
                CommentThreads =
                [
                    new ReconciliationCaseCommentThread(
                        "statement-intake",
                        "External statement intake",
                        [
                            new Meridian.Domain.Reconciliation.ReconciliationCaseComment(
                                Guid.NewGuid().ToString("N"),
                                $"{explanation.Summary} Suggested next action: {explanation.SuggestedNextAction} Classification action: {classification.RecommendedActionText}.",
                                "system",
                                now)
                        ])
                ],
                AuditEvents =
                [
                    new ReconciliationCaseAuditEvent(
                        Guid.NewGuid().ToString("N"),
                        "ExternalStatementCaseCreated",
                        now,
                        "system",
                        $"Case created for {classification.BreakType} with {classification.Severity} severity and evidence {attachment.AttachmentId}.")
                ]
            });
        }

        return (matches, cases);
    }

    /// <summary>
    /// Runs statement position rows through <see cref="StatementMatchingEngine"/> and returns a
    /// match link per row that matched at exact or tolerance tier, keyed by row id. Candidate and
    /// unmatched engine results intentionally produce no link so those rows become break cases.
    /// </summary>
    private static Dictionary<string, ReconciliationMatchLink> MatchPositionRows(
        IReadOnlyList<NormalizedStatementRow> rows,
        IReadOnlyList<InternalPortfolioPosition> internalPositions)
    {
        var statementPositions = rows
            .Where(static row => row.Kind == StatementRowKind.Position)
            .Select(static row => new NormalizedStatementPosition(
                row.RowId,
                row.RawSnapshot.GetValueOrDefault("account", "unknown-account"),
                row.Symbol,
                DateOnly.FromDateTime(row.EffectiveAtUtc.UtcDateTime),
                row.Quantity,
                row.Amount == 0m ? null : row.Amount,
                row.RowId))
            .ToArray();

        var links = new Dictionary<string, ReconciliationMatchLink>(StringComparer.OrdinalIgnoreCase);
        if (statementPositions.Length == 0)
        {
            return links;
        }

        // Fail closed if the default profile ever ships without a position rule: no rule means no
        // engine matches, so every position row surfaces as a break case for operator review.
        var positionRule = StatementToleranceProfile.Default.PositionRules.FirstOrDefault();
        if (positionRule is null)
        {
            return links;
        }

        var result = new StatementMatchingEngine().Run(new StatementMatchingRequest(
            statementPositions,
            StatementCashBalances: [],
            StatementTransactions: [],
            InternalPositions: internalPositions,
            InternalCashBalances: [],
            InternalLedgerTransactions: [],
            new StatementMatchingToleranceProfile(
                PositionQuantity: positionRule.QuantityTolerance,
                PositionMarketValue: positionRule.MarketValueTolerance,
                CashBalance: 0m,
                TransactionQuantity: 0m,
                TransactionNetAmount: 0m)));

        foreach (var match in result.Results)
        {
            if (match.Kind != StatementMatchKind.Position ||
                match.MatchTier is not (StatementMatchTier.Exact or StatementMatchTier.Tolerance) ||
                match.BrokerEvidenceReference is null)
            {
                continue;
            }

            links[match.BrokerEvidenceReference] = new ReconciliationMatchLink(
                match.BrokerEvidenceReference,
                match.InternalEvidenceReference,
                null,
                null,
                null,
                null,
                null,
                match.MatchTier == StatementMatchTier.Exact ? "high" : "medium",
                match.Explanation)
            {
                ToleranceProfileId = StatementToleranceProfile.DefaultProfileId,
                ToleranceProfileVersion = StatementToleranceProfile.DefaultProfileVersion,
                ToleranceRuleId = match.RuleIds.FirstOrDefault()
            };
        }

        return links;
    }

    private static ReconciliationCaseAttachment BuildStatementAttachment(
        NormalizedStatementRow row,
        string? evidenceRoute)
    {
        var importId = row.RawSnapshot.GetValueOrDefault("importId", row.Fingerprint);
        var rowNumber = row.RawSnapshot.GetValueOrDefault("rowNumber", row.RowId);
        var sourceKind = row.RawSnapshot.GetValueOrDefault("sourceKind", "external");
        return new ReconciliationCaseAttachment(
            AttachmentId: $"statement-row:{importId}:{rowNumber}",
            EvidenceKind: "ExternalStatementRow",
            SourceSystem: sourceKind,
            SourceReference: row.RowId,
            ContentHash: row.Fingerprint,
            Route: evidenceRoute,
            AttachedAtUtc: DateTimeOffset.UtcNow);
    }

    private static ReconciliationBreakExplanation BuildBreakExplanation(NormalizedStatementRow row)
    {
        var sourceKind = row.RawSnapshot.GetValueOrDefault("sourceKind", "external");
        var account = row.RawSnapshot.GetValueOrDefault("account", "unknown-account");
        var rowNumber = row.RawSnapshot.GetValueOrDefault("rowNumber", row.RowId);
        var importId = row.RawSnapshot.GetValueOrDefault("importId", row.Fingerprint);
        var activityType = row.RawSnapshot.GetValueOrDefault("activityType", row.Kind.ToString());
        var evidenceRoute = $"/api/workstation/reconciliation/statement-runs/{Uri.EscapeDataString(importId)}#row-{Uri.EscapeDataString(rowNumber)}";

        var (probableCause, ledgerImpact, suggestedNextAction, signoffRole) = row.Kind switch
        {
            StatementRowKind.Position => (
                "External position could not be matched to a retained internal portfolio position within tolerance.",
                $"Position and market-value balances may be misstated by {FormatAmount(row.Amount, row.Currency)} for {DisplaySymbol(row)}.",
                "Compare the statement position with the retained internal position snapshot and attach the position-reconciliation evidence before resolving.",
                "Fund operations"),
            StatementRowKind.CashBalance => (
                "External cash balance could not be matched to a retained internal cash snapshot within tolerance.",
                $"Cash ledger may need a balance adjustment or missing bank/custodian sync review for account {account}.",
                "Compare the statement cash balance with the latest internal cash ledger and attach the cash-sync evidence packet.",
                "Fund accounting"),
            StatementRowKind.Dividend => (
                "External dividend activity has no deterministic ledger income or receivable match.",
                $"Dividend income or receivable postings for {DisplaySymbol(row)} may be missing, duplicated, or dated outside the statement window.",
                "Review Security Master corporate-action evidence, expected dividend journal preview, and broker activity before resolving.",
                "Controller"),
            StatementRowKind.Fee => (
                "External fee activity has no matching expense or cash movement in the ledger.",
                $"Expense and cash accounts may be understated by {FormatAmount(row.Amount, row.Currency)}.",
                "Map the broker fee to the fund expense policy, draft the journal impact, and attach approval evidence.",
                "Fund accounting"),
            _ => (
                "External transaction has no deterministic order, fill, ledger, or cash movement match.",
                $"Ledger, position, and cash balances may be misstated by {FormatAmount(row.Amount, row.Currency)} for {DisplaySymbol(row)}.",
                "Link the source order/fill/session evidence or create a correcting journal candidate before sign-off.",
                "Fund operations")
        };

        return new ReconciliationBreakExplanation(
            Summary: $"{HumanizeKind(row.Kind)} break from {sourceKind} statement row {rowNumber}.",
            SourceSystems: [sourceKind, "Meridian ledger", "Meridian positions"],
            ProbableCause: probableCause,
            LedgerImpact: ledgerImpact,
            SuggestedNextAction: suggestedNextAction,
            RequiredSignoffRole: signoffRole,
            EvidenceLinks: [evidenceRoute, $"statement-row:{importId}:{rowNumber}", $"statement-hash:{row.Fingerprint}"]);
    }

    private static string DisplaySymbol(NormalizedStatementRow row) =>
        string.IsNullOrWhiteSpace(row.Symbol) ? "cash activity" : row.Symbol;

    private static string FormatAmount(decimal amount, string currency) =>
        $"{currency} {amount:G29}";

    private static string HumanizeKind(StatementRowKind kind) =>
        kind switch
        {
            StatementRowKind.CashBalance => "Cash balance",
            StatementRowKind.Position => "Position",
            _ => kind.ToString()
        };

    private static string ToCasePriority(ReconciliationBreakSeverity severity) => severity switch
    {
        ReconciliationBreakSeverity.High or ReconciliationBreakSeverity.Critical => "High",
        ReconciliationBreakSeverity.Medium => "Normal",
        _ => "Low"
    };

    private sealed record ParsedStatementLine(
        StatementSourceRowReference SourceRow,
        IReadOnlyDictionary<string, string> RawSnapshot,
        StatementSecurityReference Security,
        string AccountId,
        string ExternalAccountId,
        string Currency,
        decimal Quantity,
        decimal Price,
        decimal MarketValue,
        DateOnly TradeDate,
        DateOnly? SettlementDate,
        decimal Amount,
        decimal FeesCommission,
        StatementRowKind RowKind,
        string TransactionType,
        string? ExternalReference);
}

public sealed record ExternalStatementCaseIntakeResult(
    string ImportId,
    string SourceKind,
    string SourcePath,
    int RowCount,
    int MatchCount,
    IReadOnlyList<ReconciliationCase> Cases);

internal static class DateTimeOffsetBusinessDayExtensions
{
    public static DateTimeOffset AddBusinessDays(this DateTimeOffset value, int days)
    {
        var result = value;
        var remaining = days;
        while (remaining > 0)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                remaining--;
            }
        }

        return result;
    }
}

public static class DeterministicFingerprint
{
    public static string Compute(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
