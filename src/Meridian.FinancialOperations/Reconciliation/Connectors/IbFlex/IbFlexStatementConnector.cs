using System.Text;
using System.Xml;
using System.Xml.Linq;
using Meridian.Contracts.Integrity;
using Meridian.DataIntegration.Credentials;
using Meridian.Execution.Sdk;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.IbFlex;

/// <summary>
/// Interactive Brokers Flex Query (XML) statement connector. Reads the Trades,
/// CashTransactions, and OpenPositions sections of a FlexQueryResponse — every section is
/// optional because operators configure which sections their Flex query emits. Cash
/// transaction types are classified through the data-driven activity-code map in the
/// <c>ib-flex-v1</c> profile, so a new IB cash type is an operator profile edit, not a
/// code change.
/// </summary>
public sealed class IbFlexStatementConnector : IFetchingStatementConnector
{
    public const string ConnectorId = "ib-flex";

    private const string FlexRootElement = "FlexQueryResponse";

    private readonly StatementMappingProfileCatalog _catalog;
    private readonly IProviderCredentialStore? _credentialStore;
    private readonly IIbFlexWebServiceClient? _webServiceClient;
    private readonly StatementIngressLimits _limits;

    /// <param name="limits">
    /// The shared ingress bound. This connector previously carried its own private 100,000-row
    /// ceiling while every sibling connector took these limits, so a deployment that raised
    /// <see cref="StatementIngressLimits.MaxRecords"/> - which the module README documents as the one
    /// place a cap is raised - still had a legitimate Flex report refused at row 100,001 by a number
    /// it could not configure. Both bounds count the same thing, retained rows, so they are now one
    /// bound. Note this raises the default Flex ceiling from 100,000 to the shared 250,000; a
    /// deployment that wants the old ceiling sets MaxRecords to 100,000.
    /// </param>
    public IbFlexStatementConnector(
        StatementMappingProfileCatalog catalog,
        IProviderCredentialStore? credentialStore = null,
        IIbFlexWebServiceClient? webServiceClient = null,
        StatementIngressLimits? limits = null)
    {
        _catalog = catalog;
        _credentialStore = credentialStore;
        _webServiceClient = webServiceClient;
        _limits = limits ?? StatementIngressLimits.Default;
        Descriptor = new StatementConnectorDescriptor(
            ConnectorId,
            "Interactive Brokers Flex Report (XML)",
            [".xml"],
            SupportsFileImport: true,
            SupportsRemoteFetch: credentialStore is not null && webServiceClient is not null,
            RequiresMappingProfile: false,
            DefaultProfileId: StatementBuiltInProfiles.IbFlexV1ProfileId);
    }

    public StatementConnectorDescriptor Descriptor { get; }

    public async Task<StatementSourceDocument> FetchAsync(
        StatementFetchRequest request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExternalAccountId);
        if (_credentialStore is null || _webServiceClient is null)
            throw new NotSupportedException("IB Flex Web Service is not registered in this host.");

        var credentials = await _credentialStore.ReadForProviderAsync(ConnectorId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("IB Flex token and query ID are not configured.");
        var token = credentials.Get("Token");
        var queryId = credentials.Get("QueryId");
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(queryId))
            throw new InvalidOperationException("IB Flex token and query ID are required before scheduled fetch can run.");

        var content = await _webServiceClient.FetchStatementAsync(token, queryId, ct).ConfigureAwait(false);
        var retrievedAt = DateTimeOffset.UtcNow;
        var fileName = $"ib-flex-{SanitizeAccountForFileName(request.ExternalAccountId)}-{retrievedAt:yyyyMMddHHmmss}.xml";
        return new StatementSourceDocument(
            fileName,
            content,
            request.MappingProfileId,
            request.ExternalAccountId);
    }

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
        var profile = await _catalog.FindAsync(profileId, ct).ConfigureAwait(false);
        if (profile is null)
        {
            issues.Add(StatementParseIssue.Error("PROFILE_NOT_FOUND", $"Mapping profile '{profileId}' is not registered."));
            return EmptyResult(profileId, issues);
        }

        if (document.Content.Length > _limits.MaxDocumentBytes)
        {
            issues.Add(StatementParseIssue.Error(
                "STATEMENT_TOO_LARGE",
                $"The Flex report exceeds the {_limits.MaxDocumentBytes}-byte limit."));
            return EmptyResult(profileId, issues);
        }

        // Bound the document before materializing it. MaxCharactersInDocument limits the characters read,
        // not the object graph built from them, and an XElement/XAttribute per node costs far more than
        // its textual form - so a permitted sub-20 MiB payload of many tiny elements expanded into a much
        // larger tree with nothing to stop it: `retained` is not initialized until after the load, and
        // MaxParseNodes was never charged on this path.
        //
        // This is the original PRD-010 complaint - "builds a whole XDocument" - which was fixed for
        // camt.053 and left standing here. A streaming rewrite is the thorough answer, but every section
        // below navigates via Descendants/Attribute/Section, so that is a large refactor out of
        // proportion to the bound. A pre-scan buys the property that matters: refuse before allocating.
        // Two passes over an in-memory buffer are cheap, and the first pass builds no objects.
        var payload = document.Content.ToArray();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            Async = true,
            MaxCharactersInDocument = _limits.MaxDocumentBytes,
            MaxCharactersFromEntities = 0
        };

        XDocument xml;
        try
        {
            await using (var scanStream = new MemoryStream(payload, writable: false))
            using (var scanReader = XmlReader.Create(scanStream, settings))
            {
                var nodes = 0;
                while (await scanReader.ReadAsync().ConfigureAwait(false))
                {
                    ct.ThrowIfCancellationRequested();

                    // The element AND its attributes. ReadAsync visits the element node only, so a
                    // document putting hundreds of thousands of attributes on one valid element stayed far
                    // below this budget while XDocument.LoadAsync still materialized an XAttribute plus a
                    // name and value string for every one. Camt053StatementConnector.TryReadBoundedSubtree
                    // has charged AttributeCount since it was written; this pre-scan is newer and did not
                    // reuse that accounting.
                    // Depth as well as count. camt's scan loops have always checked reader.Depth; this
                    // pre-scan was written without it, so a compact but extremely deep document passed the
                    // node budget and was still materialized past the configured nesting ceiling.
                    if (scanReader.Depth > _limits.MaxNestingDepth)
                    {
                        issues.Add(_limits.NestingTooDeep());
                        return EmptyResult(profileId, issues);
                    }

                    nodes += 1 + scanReader.AttributeCount;
                    if (nodes > _limits.MaxParseNodes)
                    {
                        issues.Add(_limits.TooManyNodes());
                        return EmptyResult(profileId, issues);
                    }
                }
            }

            await using var stream = new MemoryStream(payload, writable: false);
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
        var accountSnapshots = new List<BrokerageAccountSnapshotDto>();
        var activities = new List<BrokerageActivityEventDto>();
        var taxLots = new List<BrokerageTaxLotSnapshotDto>();
        var borrowPositions = new List<BrokerageBorrowPositionSnapshotDto>();
        var rowNumber = 0;
        // Every object this parse retains, charged before it is materialized. rowNumber stays as the row
        // ordinal AddRecord needs, but it cannot be the bound: seven of these loops append BOTH a
        // canonical record and an activity event per iteration while charging one, so the in-parser guard
        // passed at roughly half the real retention and left the service's TotalRetainedRows to catch it -
        // after every object had already been allocated, which is exactly what an in-parser guard exists
        // to prevent. One counter charged per append replaces the earlier two-counter split, which could
        // only ever bound the row kinds someone remembered to add to it.
        var retained = 0;
        var sectionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var statement in statements)
        {
            ct.ThrowIfCancellationRequested();
            var statementAccountId = (string?)statement.Attribute("accountId");

            foreach (var trade in Section(statement, "Trades", "Trade"))
            {
                rowNumber++;
                retained += 2;
                if (retained > _limits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
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
                activities.Add(BuildTradeActivity(trade, statementAccountId, profile));
            }

            foreach (var cash in Section(statement, "CashTransactions", "CashTransaction"))
            {
                rowNumber++;
                retained += 2;
                if (retained > _limits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
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
                activities.Add(BuildCashActivity(cash, statementAccountId, profile, activityCodeMap));
            }

            foreach (var position in Section(statement, "OpenPositions", "OpenPosition"))
            {
                rowNumber++;
                retained += 1;
                if (retained > _limits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
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

            var accountInformationElements = Descendants(statement, "AccountInformation").ToArray();
            var cashReportElements = Descendants(statement, "CashReportCurrency").ToArray();
            var marginReportElements = Descendants(
                    statement,
                    "MarginReport",
                    "MarginReportCurrency",
                    "MarginReportData",
                    "MarginSummary",
                    "MarginRequirement")
                .Where(IsMarginEvidenceElement)
                .ToArray();

            var accountSnapshotAnchors = accountInformationElements.Length > 0
                ? accountInformationElements
                : marginReportElements.Length > 0
                    ? [marginReportElements[0]]
                    : cashReportElements.Length > 0
                        ? [cashReportElements[0]]
                        : [];
            foreach (var accountInformation in accountSnapshotAnchors)
            {
                // Snapshots are retained evidence too, and BuildAccountSnapshot rescans the statement's
                // descendants for cash and margin evidence on every call - so an unbounded snapshot count
                // is quadratic in CPU as well as linear in retained DTOs. Charging them before the call
                // bounds both.
                retained += 1;
                if (retained > _limits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                    return EmptyResult(profileId, issues);
                }

                CountSection(sectionCounts, "AccountInformation");
                CollectAttributeNames(accountInformation, detectedColumns);
                accountSnapshots.Add(BuildAccountSnapshot(statement, accountInformation, statementAccountId, profile));
            }

            foreach (var marginReport in marginReportElements)
            {
                CountSection(sectionCounts, "MarginReport");
                CollectAttributeNames(marginReport, detectedColumns);
            }

            foreach (var cashReport in cashReportElements)
            {
                rowNumber++;
                CountSection(sectionCounts, "CashReport");
                CollectAttributeNames(cashReport, detectedColumns);
                // Charged on the append; a cash-report row that builds nothing retains nothing.
                if (BuildCashReportRecord(statement, cashReport, statementAccountId, profile) is { } record)
                {
                    retained += 1;
                    if (retained > _limits.MaxRecords)
                    {
                        issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                        return EmptyResult(profileId, issues);
                    }

                    records.Add(record);
                }
            }

            foreach (var interest in Descendants(statement, "InterestDetail", "InterestAccrual"))
            {
                rowNumber++;
                retained += 2;
                if (retained > _limits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                    return EmptyResult(profileId, issues);
                }

                CountSection(sectionCounts, interest.Name.LocalName.EndsWith("Detail", StringComparison.OrdinalIgnoreCase)
                    ? "InterestDetails"
                    : "InterestAccruals");
                CollectAttributeNames(interest, detectedColumns);
                var activity = BuildInterestActivity(interest, statementAccountId, profile);
                activities.Add(activity);
                records.Add(ToCanonicalRecord(statementAccountId, activity, StatementRecordKind.Fee));
            }

            foreach (var borrowFee in Descendants(statement, "BorrowFeeDetail"))
            {
                rowNumber++;
                retained += 2;
                if (retained > _limits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                    return EmptyResult(profileId, issues);
                }

                CountSection(sectionCounts, "BorrowFeeDetails");
                CollectAttributeNames(borrowFee, detectedColumns);
                var activity = BuildBorrowFeeActivity(borrowFee, statementAccountId, profile);
                activities.Add(activity);
                records.Add(ToCanonicalRecord(statementAccountId, activity, StatementRecordKind.Fee));
            }

            foreach (var commission in Descendants(statement, "CommissionDetail"))
            {
                // Commission, open-lot and securities-borrow rows retain an activity or position DTO each
                // and are serialized into canonical-evidence.json regardless of never producing a
                // canonical record.
                retained += 1;
                if (retained > _limits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                    return EmptyResult(profileId, issues);
                }

                CountSection(sectionCounts, "CommissionDetails");
                CollectAttributeNames(commission, detectedColumns);
                activities.Add(BuildCommissionActivity(commission, statementAccountId, profile));
            }

            foreach (var corporateAction in Descendants(statement, "CorporateAction"))
            {
                rowNumber++;
                retained += 2;
                if (retained > _limits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                    return EmptyResult(profileId, issues);
                }

                CountSection(sectionCounts, "CorporateActions");
                CollectAttributeNames(corporateAction, detectedColumns);
                var activity = BuildCorporateActionActivity(corporateAction, statementAccountId, profile);
                activities.Add(activity);
                records.Add(ToCanonicalRecord(statementAccountId, activity, StatementRecordKind.Transaction));
            }

            foreach (var transfer in Descendants(statement, "Transfer"))
            {
                rowNumber++;
                retained += 2;
                if (retained > _limits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                    return EmptyResult(profileId, issues);
                }

                CountSection(sectionCounts, "Transfers");
                CollectAttributeNames(transfer, detectedColumns);
                var activity = BuildTransferActivity(transfer, statementAccountId, profile);
                activities.Add(activity);
                records.Add(ToCanonicalRecord(statementAccountId, activity, StatementRecordKind.Transaction));
            }

            foreach (var optionEae in Descendants(statement, "OptionEAE"))
            {
                if (!optionEae.HasAttributes)
                    continue;
                rowNumber++;
                retained += 2;
                if (retained > _limits.MaxRecords)
                {
                    issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                    return EmptyResult(profileId, issues);
                }

                CountSection(sectionCounts, "OptionEAE");
                CollectAttributeNames(optionEae, detectedColumns);
                var activity = BuildOptionLifecycleActivity(optionEae, statementAccountId, profile);
                activities.Add(activity);
                records.Add(ToCanonicalRecord(statementAccountId, activity, StatementRecordKind.Transaction));
            }

            foreach (var openLot in Descendants(statement, "OpenLot"))
            {
                CountSection(sectionCounts, "OpenLots");
                CollectAttributeNames(openLot, detectedColumns);
                // Charged on the append, not on the candidate. An OpenLot that BuildTaxLot rejects retains
                // nothing, and billing it let a document of empty elements consume the whole allowance and
                // then be refused for rows it never kept.
                if (BuildTaxLot(openLot, statementAccountId, profile) is { } taxLot)
                {
                    retained += 1;
                    if (retained > _limits.MaxRecords)
                    {
                        issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                        return EmptyResult(profileId, issues);
                    }

                    taxLots.Add(taxLot);
                }
            }

            foreach (var borrowed in Descendants(statement, "SecurityBorrowed", "SecuritiesBorrowed", "SecurityLent", "SecuritiesLent"))
            {
                if (!borrowed.HasAttributes)
                    continue;

                CountSection(sectionCounts, "SecuritiesBorrowedLent");
                CollectAttributeNames(borrowed, detectedColumns);
                // Charged on the append; a borrow element that builds no position retains nothing.
                if (BuildBorrowPosition(borrowed, statementAccountId, profile) is { } borrowPosition)
                {
                    retained += 1;
                    if (retained > _limits.MaxRecords)
                    {
                        issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                        return EmptyResult(profileId, issues);
                    }

                    borrowPositions.Add(borrowPosition);
                }
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
        var highWatermark = activities
            .Select(static activity => activity.EffectiveAt)
            .Where(static value => value != DateTimeOffset.UnixEpoch)
            .Concat(accountSnapshots.Select(static snapshot => snapshot.AsOf)
                .Where(static value => value != DateTimeOffset.UnixEpoch))
            .DefaultIfEmpty()
            .Max();
        // The cursor is retained evidence like everything else and was appended after the loops without
        // ever being charged. It is exactly one row when any statement parsed, so reserve it here rather
        // than let the budget be off by one at the boundary.
        if (statements.Length > 0)
        {
            retained += 1;
            if (retained > _limits.MaxRecords)
            {
                issues.Add(StatementParseIssue.Error("ROW_LIMIT_EXCEEDED", $"The Flex report exceeds the {_limits.MaxRecords}-row limit."));
                return EmptyResult(profileId, issues);
            }
        }

        var activityCursors = statements.Length == 0
            ? []
            : new[]
            {
                new BrokerageActivityCursorDto(
                    LastEventId: activities.OrderBy(static activity => activity.EffectiveAt).LastOrDefault()?.EventId,
                    HighWatermark: highWatermark == default ? null : highWatermark,
                    PageCount: 1,
                    SourceRecordCount: activities.Count,
                    IsComplete: true)
            };
        return new StatementParseResult(
            ConnectorId,
            profile.ProfileId,
            detected,
            StatementColumnConfidenceScorer.MapColumns(detected, profile),
            records,
            issues,
            new StatementFormatFingerprint(
                Sha256Digest.Compute(document.Content.Span),
                detected.Select(static column => column.ToLowerInvariant()).ToArray(),
                "xml"),
            AccountSnapshots: accountSnapshots,
            ActivityEvents: activities,
            ActivityCursors: activityCursors,
            TaxLots: taxLots,
            BorrowPositions: borrowPositions);
    }

    private static BrokerageActivityEventDto BuildTradeActivity(
        XElement trade,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
    {
        var quantity = DecimalAttribute(trade, profile, "quantity");
        var side = AttributeAny(trade, "buySell", "side");
        if (quantity.HasValue && string.Equals(side, "SELL", StringComparison.OrdinalIgnoreCase))
            quantity = -Math.Abs(quantity.Value);

        return BuildActivity(
            trade,
            statementAccountId,
            profile,
            providerCode: "Trade",
            BrokerageActivityCategory.Trade,
            BrokerageActivitySubtype.TradeFill,
            amountNames: ["netCash", "proceeds"],
            quantity: quantity,
            price: DecimalAttribute(trade, profile, "tradePrice", "price"),
            idNames: ["tradeID", "transactionID"],
            orderId: AttributeAny(trade, "orderID", "ibOrderID"),
            relatedEventId: AttributeAny(trade, "relatedTradeID", "origTradeID"),
            description: AttributeAny(trade, "description", "assetCategory"),
            option: BuildOptionTerms(trade, lifecycleAction: "Trade", profile));
    }

    private static BrokerageActivityEventDto BuildCashActivity(
        XElement cash,
        string? statementAccountId,
        StatementMappingProfileDocument profile,
        IReadOnlyDictionary<string, string> activityCodeMap)
    {
        var providerCode = Attribute(cash, "type") ?? "CashTransaction";
        activityCodeMap.TryGetValue(providerCode, out var canonicalActivity);
        var amount = DecimalAttribute(cash, profile, "amount") ?? 0m;
        var (category, subtype) = canonicalActivity?.ToLowerInvariant() switch
        {
            "fee" => (BrokerageActivityCategory.Fee, BrokerageActivitySubtype.Fee),
            "dividend" or "div" => (BrokerageActivityCategory.Dividend, BrokerageActivitySubtype.CashDividend),
            // The profile maps cash movements to the canonical "transaction" activity (so reconciliation
            // routes them to the ledger-transaction lane); they remain deposits/withdrawals here.
            "cash" or "cashbalance" or "transaction" when amount < 0m => (BrokerageActivityCategory.Cash, BrokerageActivitySubtype.CashWithdrawal),
            "cash" or "cashbalance" or "transaction" => (BrokerageActivityCategory.Cash, BrokerageActivitySubtype.CashDeposit),
            _ => (BrokerageActivityCategory.Cash, BrokerageActivitySubtype.Other)
        };
        return BuildActivity(
            cash,
            statementAccountId,
            profile,
            providerCode,
            category,
            subtype,
            amountNames: ["amount"],
            quantity: null,
            price: null,
            idNames: ["transactionID", "activityID"],
            description: AttributeAny(cash, "description", "type"));
    }

    private static BrokerageAccountSnapshotDto BuildAccountSnapshot(
        XElement statement,
        XElement account,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
    {
        var accountId = Attribute(account, "accountId") ?? statementAccountId ?? string.Empty;
        var baseCurrency = AttributeAny(account, "baseCurrency", "currency") ?? "USD";
        var cashReport = Descendants(statement, "CashReportCurrency")
            .Where(element => MatchesAccount(element, accountId))
            .OrderByDescending(element => string.Equals(Attribute(element, "currency"), baseCurrency, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        var marginReport = Descendants(
                statement,
                "MarginReport",
                "MarginReportCurrency",
                "MarginReportData",
                "MarginSummary",
                "MarginRequirement")
            .Where(element => MatchesAccount(element, accountId) && IsMarginEvidenceElement(element))
            .FirstOrDefault();
        var accountType = AttributeAny(account, "accountType", "type")
            ?? (marginReport is null ? null : AttributeAny(marginReport, "accountType", "type"))
            ?? string.Empty;
        var marginRegime = accountType.Contains("portfolio", StringComparison.OrdinalIgnoreCase)
            ? BrokerageMarginRegime.PortfolioMargin
            : accountType.Contains("margin", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(accountType, "M", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(accountType, "RegT", StringComparison.OrdinalIgnoreCase)
                ? BrokerageMarginRegime.RegulationT
                : accountType.Contains("cash", StringComparison.OrdinalIgnoreCase)
                    ? BrokerageMarginRegime.Cash
                    : BrokerageMarginRegime.Unknown;
        var equity = DecimalAttribute(account, profile, "netLiquidationValue", "netLiquidation", "equityWithLoanValue")
            ?? (marginReport is null ? null : DecimalAttribute(marginReport, profile, "netLiquidationValue", "netLiquidation", "equityWithLoanValue"))
            ?? 0m;
        var maintenanceMargin = DecimalAttribute(account, profile, "maintenanceMarginRequirement", "maintenanceMargin", "currentMaintenanceMargin")
            ?? (marginReport is null ? null : DecimalAttribute(marginReport, profile, "maintenanceMarginRequirement", "maintenanceMargin", "currentMaintenanceMargin"));
        var reportedExcess = DecimalAttribute(account, profile, "excessLiquidity", "availableFunds", "currentExcessLiquidity")
            ?? (marginReport is null ? null : DecimalAttribute(marginReport, profile, "excessLiquidity", "availableFunds", "currentExcessLiquidity"));
        var cash = DecimalAttribute(account, profile, "cashBalance", "totalCashValue")
            ?? (cashReport is null ? null : DecimalAttribute(cashReport, profile, "endingCash", "endingCashForStatement", "endingSettledCash"))
            ?? 0m;

        return new BrokerageAccountSnapshotDto(
            ProviderId: ConnectorId,
            AccountId: accountId,
            AsOf: EffectiveAt(account, statement, profile),
            Currency: baseCurrency,
            Status: AttributeAny(account, "accountStatus", "status") ?? "Reported",
            MarginRegime: marginRegime,
            Cash: cash,
            Equity: equity,
            BuyingPower: DecimalAttribute(account, profile, "buyingPower")
                ?? (marginReport is null ? null : DecimalAttribute(marginReport, profile, "buyingPower", "currentAvailableFunds"))
                ?? 0m,
            SettledCash: DecimalAttribute(account, profile, "settledCash")
                ?? (cashReport is null ? null : DecimalAttribute(cashReport, profile, "endingSettledCash")),
            UnsettledCash: DecimalAttribute(account, profile, "unsettledCash")
                ?? (cashReport is null ? null : DecimalAttribute(cashReport, profile, "endingUnsettledCash")),
            LongMarketValue: DecimalAttribute(account, profile, "longMarketValue", "stockMarketValue")
                ?? (marginReport is null ? null : DecimalAttribute(marginReport, profile, "longMarketValue", "stockMarketValue")),
            ShortMarketValue: DecimalAttribute(account, profile, "shortMarketValue")
                ?? (marginReport is null ? null : DecimalAttribute(marginReport, profile, "shortMarketValue")),
            RegTBuyingPower: DecimalAttribute(account, profile, "regTBuyingPower")
                ?? (marginReport is null ? null : DecimalAttribute(marginReport, profile, "regTBuyingPower")),
            InitialMargin: DecimalAttribute(account, profile, "initialMarginRequirement", "initialMargin", "currentInitialMargin")
                ?? (marginReport is null ? null : DecimalAttribute(marginReport, profile, "initialMarginRequirement", "initialMargin", "currentInitialMargin")),
            MaintenanceMargin: maintenanceMargin,
            ExcessLiquidity: reportedExcess ?? (maintenanceMargin.HasValue ? equity - maintenanceMargin.Value : null),
            SpecialMemorandumAccount: DecimalAttribute(account, profile, "sma")
                ?? (marginReport is null ? null : DecimalAttribute(marginReport, profile, "sma")),
            MarginLoan: DecimalAttribute(account, profile, "marginLoan", "debitCashBalance")
                ?? (marginReport is null ? null : DecimalAttribute(marginReport, profile, "marginLoan", "debitCashBalance")),
            Multiplier: DecimalAttribute(account, profile, "multiplier"),
            SourceAttributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["accountType"] = accountType,
                ["sourceAuthority"] = "ProviderReported",
                ["cashSection"] = cashReport?.Name.LocalName ?? "Unavailable",
                ["marginSection"] = marginReport?.Name.LocalName ?? "Unavailable"
            });
    }

    private static StatementCanonicalRecord? BuildCashReportRecord(
        XElement statement,
        XElement cashReport,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
    {
        var amount = DecimalAttribute(cashReport, profile, "endingCash", "endingCashForStatement", "endingSettledCash");
        if (!amount.HasValue)
            return null;
        return new StatementCanonicalRecord(
            StatementRecordKind.CashBalance,
            Attribute(cashReport, "accountId") ?? statementAccountId ?? string.Empty,
            string.Empty,
            0m,
            0m,
            amount.Value,
            "cash",
            DateOnly.FromDateTime(EffectiveAt(cashReport, statement, profile).UtcDateTime),
            Currency: Attribute(cashReport, "currency"),
            ActivityCategory: BrokerageActivityCategory.Cash.ToString(),
            ActivitySubtype: "EndOfPeriodBalance",
            ProviderActivityCode: "CashReport");
    }

    private static BrokerageActivityEventDto BuildInterestActivity(
        XElement element,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
    {
        var description = AttributeAny(element, "description", "type", "currency") ?? "Interest";
        var subtype = description.Contains("margin", StringComparison.OrdinalIgnoreCase) ||
                      description.Contains("debit", StringComparison.OrdinalIgnoreCase)
            ? BrokerageActivitySubtype.MarginInterest
            : BrokerageActivitySubtype.CreditInterest;
        return BuildActivity(
            element,
            statementAccountId,
            profile,
            "Interest",
            BrokerageActivityCategory.Interest,
            subtype,
            ["amount", "interestAmount", "accrualAmount"],
            null,
            null,
            ["transactionID", "interestID"],
            description: description);
    }

    private static BrokerageActivityEventDto BuildBorrowFeeActivity(
        XElement element,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
        => BuildActivity(
            element,
            statementAccountId,
            profile,
            "BorrowFee",
            BrokerageActivityCategory.Borrow,
            BrokerageActivitySubtype.BorrowFee,
            ["feeAmount", "amount"],
            DecimalAttribute(element, profile, "quantity"),
            null,
            ["transactionID", "borrowFeeID"],
            description: AttributeAny(element, "description", "symbol"));

    private static BrokerageActivityEventDto BuildCommissionActivity(
        XElement element,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
        => BuildActivity(
            element,
            statementAccountId,
            profile,
            "Commission",
            BrokerageActivityCategory.Fee,
            BrokerageActivitySubtype.Fee,
            ["totalCommission", "commission", "amount"],
            DecimalAttribute(element, profile, "quantity"),
            DecimalAttribute(element, profile, "tradePrice"),
            ["tradeID", "transactionID"],
            orderId: AttributeAny(element, "orderID", "ibOrderID"),
            description: AttributeAny(element, "description", "brokerExecutionCommission"));

    private static BrokerageActivityEventDto BuildCorporateActionActivity(
        XElement element,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
    {
        var description = AttributeAny(element, "description", "actionDescription", "type") ?? "Corporate action";
        var subtype = description.Contains("split", StringComparison.OrdinalIgnoreCase)
            ? BrokerageActivitySubtype.StockSplit
            : description.Contains("spin", StringComparison.OrdinalIgnoreCase)
                ? BrokerageActivitySubtype.Spinoff
                : description.Contains("merger", StringComparison.OrdinalIgnoreCase)
                    ? BrokerageActivitySubtype.Merger
                    : description.Contains("symbol", StringComparison.OrdinalIgnoreCase) || description.Contains("name change", StringComparison.OrdinalIgnoreCase)
                        ? BrokerageActivitySubtype.SymbolChange
                        : BrokerageActivitySubtype.Reorganization;
        return BuildActivity(
            element,
            statementAccountId,
            profile,
            AttributeAny(element, "type", "code") ?? "CorporateAction",
            BrokerageActivityCategory.CorporateAction,
            subtype,
            ["proceeds", "amount", "netCash"],
            DecimalAttribute(element, profile, "quantity"),
            DecimalAttribute(element, profile, "price"),
            ["transactionID", "actionID"],
            description: description);
    }

    private static BrokerageActivityEventDto BuildTransferActivity(
        XElement element,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
    {
        var symbol = Attribute(element, "symbol");
        return BuildActivity(
            element,
            statementAccountId,
            profile,
            AttributeAny(element, "type", "direction") ?? "Transfer",
            BrokerageActivityCategory.Transfer,
            string.IsNullOrWhiteSpace(symbol) ? BrokerageActivitySubtype.CashTransfer : BrokerageActivitySubtype.SecurityTransfer,
            ["cashTransfer", "amount", "positionAmount"],
            DecimalAttribute(element, profile, "quantity", "position"),
            null,
            ["transactionID", "transferID"],
            description: AttributeAny(element, "description", "type"));
    }

    private static BrokerageActivityEventDto BuildOptionLifecycleActivity(
        XElement element,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
    {
        var action = AttributeAny(element, "transactionType", "type", "action") ?? "OptionLifecycle";
        var subtype = action.Contains("assign", StringComparison.OrdinalIgnoreCase)
            ? BrokerageActivitySubtype.OptionAssignment
            : action.Contains("exerc", StringComparison.OrdinalIgnoreCase)
                ? BrokerageActivitySubtype.OptionExercise
                : BrokerageActivitySubtype.OptionExpiration;
        return BuildActivity(
            element,
            statementAccountId,
            profile,
            action,
            BrokerageActivityCategory.OptionLifecycle,
            subtype,
            ["netCash", "proceeds", "amount"],
            DecimalAttribute(element, profile, "quantity"),
            DecimalAttribute(element, profile, "tradePrice", "price"),
            ["transactionID", "tradeID"],
            description: AttributeAny(element, "description", "symbol"),
            option: BuildOptionTerms(element, action, profile));
    }

    private static BrokerageTaxLotSnapshotDto? BuildTaxLot(
        XElement element,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
    {
        var symbol = Attribute(element, "symbol");
        var quantity = DecimalAttribute(element, profile, "quantity");
        var costBasis = DecimalAttribute(element, profile, "costBasisMoney", "costBasis");
        if (string.IsNullOrWhiteSpace(symbol) || !quantity.HasValue || !costBasis.HasValue ||
            !TryDateAttribute(element, profile, out var acquiredDate, "openDateTime", "acquiredDate", "dateTime"))
        {
            return null;
        }

        var lotId = AttributeAny(element, "lotCode", "lotID", "transactionID")
            ?? $"{statementAccountId}:{symbol}:{acquiredDate:yyyyMMdd}:{quantity.Value}";
        return new BrokerageTaxLotSnapshotDto(
            LotId: lotId,
            Symbol: symbol,
            AcquiredDate: acquiredDate,
            Quantity: quantity.Value,
            CostBasis: costBasis.Value,
            Currency: Attribute(element, "currency") ?? "USD",
            UnitCost: quantity.Value == 0m ? null : costBasis.Value / quantity.Value,
            MarketValue: DecimalAttribute(element, profile, "value", "marketValue"),
            UnrealizedPnl: DecimalAttribute(element, profile, "fifoPnlUnrealized", "unrealizedPnl"),
            AccountId: Attribute(element, "accountId") ?? statementAccountId);
    }

    private static BrokerageBorrowPositionSnapshotDto? BuildBorrowPosition(
        XElement element,
        string? statementAccountId,
        StatementMappingProfileDocument profile)
    {
        var symbol = Attribute(element, "symbol");
        var quantity = DecimalAttribute(element, profile, "quantity", "position");
        if (string.IsNullOrWhiteSpace(symbol) || !quantity.HasValue)
            return null;
        var rate = DecimalAttribute(element, profile, "feeRate", "borrowRate");
        return new BrokerageBorrowPositionSnapshotDto(
            Symbol: symbol,
            Quantity: quantity.Value,
            Status: rate > 0m ? BrokerageBorrowStatus.HardToBorrow : BrokerageBorrowStatus.Unknown,
            Currency: Attribute(element, "currency") ?? "USD",
            AvailableQuantity: DecimalAttribute(element, profile, "availableQuantity"),
            BorrowRate: rate,
            DailyCost: DecimalAttribute(element, profile, "feeAmount", "dailyCost"),
            Collateral: DecimalAttribute(element, profile, "collateralAmount", "collateral"),
            RecallDate: TryDateAttribute(element, profile, out var recallDate, "recallDate") ? recallDate : null,
            AccountId: Attribute(element, "accountId") ?? statementAccountId);
    }

    private static BrokerageActivityEventDto BuildActivity(
        XElement element,
        string? statementAccountId,
        StatementMappingProfileDocument profile,
        string providerCode,
        BrokerageActivityCategory category,
        BrokerageActivitySubtype subtype,
        IReadOnlyList<string> amountNames,
        decimal? quantity,
        decimal? price,
        IReadOnlyList<string> idNames,
        string? orderId = null,
        string? relatedEventId = null,
        string? description = null,
        BrokerageOptionLifecycleSnapshotDto? option = null)
    {
        var effectiveAt = EffectiveAt(element, null, profile);
        var eventId = AttributeAny(element, [.. idNames])
            ?? $"ib-flex:{providerCode}:{effectiveAt:O}:{Attribute(element, "symbol")}:{quantity}:{price}:{DecimalAttribute(element, profile, [.. amountNames])}";
        var accountId = Attribute(element, "accountId") ?? statementAccountId ?? string.Empty;
        return new BrokerageActivityEventDto(
            EventId: eventId,
            ProviderCode: providerCode,
            Category: category,
            Subtype: subtype,
            EffectiveAt: effectiveAt,
            Currency: Attribute(element, "currency") ?? "USD",
            NetAmount: DecimalAttribute(element, profile, [.. amountNames]) ?? 0m,
            Symbol: Attribute(element, "symbol"),
            Quantity: quantity,
            Price: price,
            OrderId: orderId,
            RelatedEventId: relatedEventId,
            Description: description,
            Option: option,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["accountId"] = accountId,
                ["sourceAuthority"] = "ProviderReported"
            });
    }

    private static StatementCanonicalRecord ToCanonicalRecord(
        string? statementAccountId,
        BrokerageActivityEventDto activity,
        StatementRecordKind kind)
        => new(
            Kind: kind,
            Account: statementAccountId ?? string.Empty,
            Symbol: activity.Symbol ?? string.Empty,
            Quantity: activity.Quantity ?? 0m,
            Price: activity.Price ?? 0m,
            CashAmount: activity.NetAmount,
            ActivityType: StatementRecordMapper.ToArtifactActivityType(kind),
            TradeDate: DateOnly.FromDateTime(activity.EffectiveAt.UtcDateTime),
            Currency: activity.Currency,
            FeesCommission: kind == StatementRecordKind.Fee ? activity.NetAmount : null,
            ExternalTransactionId: activity.EventId,
            ActivityCategory: activity.Category.ToString(),
            ActivitySubtype: activity.Subtype.ToString(),
            ProviderActivityCode: activity.ProviderCode,
            RelatedTransactionId: activity.RelatedEventId,
            OrderId: activity.OrderId,
            Description: activity.Description);

    private static BrokerageOptionLifecycleSnapshotDto? BuildOptionTerms(
        XElement element,
        string lifecycleAction,
        StatementMappingProfileDocument profile)
    {
        var optionType = AttributeAny(element, "putCall", "optionType");
        var strike = DecimalAttribute(element, profile, "strike", "strikePrice");
        var hasExpiry = TryDateAttribute(element, profile, out var expiry, "expiry", "expirationDate");
        if (string.IsNullOrWhiteSpace(optionType) && !strike.HasValue && !hasExpiry)
            return null;
        return new BrokerageOptionLifecycleSnapshotDto(
            ContractId: AttributeAny(element, "conid", "contractID", "symbol") ?? string.Empty,
            UnderlyingSymbol: AttributeAny(element, "underlyingSymbol", "symbol"),
            OptionType: optionType,
            StrikePrice: strike,
            ExpirationDate: hasExpiry ? expiry : null,
            ContractMultiplier: DecimalAttribute(element, profile, "multiplier") ?? 100m,
            LifecycleAction: lifecycleAction,
            StrategyId: Attribute(element, "strategyID"),
            LegId: Attribute(element, "legID"));
    }

    private static DateTimeOffset EffectiveAt(
        XElement element,
        XElement? statement,
        StatementMappingProfileDocument profile)
    {
        if (TryDateAttribute(element, profile, out var date, "dateTime", "date", "tradeDate", "reportDate", "settleDate", "openDateTime"))
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        if (statement is not null && TryDateAttribute(statement, profile, out date, "toDate", "fromDate"))
            return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return DateTimeOffset.UnixEpoch;
    }

    private static decimal? DecimalAttribute(
        XElement element,
        StatementMappingProfileDocument profile,
        params string[] names)
        => StatementValueParser.TryParseDecimal(AttributeAny(element, names), profile, out var value) ? value : null;

    private static bool TryDateAttribute(
        XElement element,
        StatementMappingProfileDocument profile,
        out DateOnly value,
        params string[] names)
        => StatementValueParser.TryParseDate(AttributeAny(element, names), profile, out value);

    private static bool MatchesAccount(XElement element, string accountId)
    {
        var candidate = Attribute(element, "accountId");
        return string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(accountId) ||
               string.Equals(candidate, accountId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMarginEvidenceElement(XElement element)
        => AttributeAny(
            element,
            "netLiquidationValue",
            "netLiquidation",
            "equityWithLoanValue",
            "initialMarginRequirement",
            "initialMargin",
            "currentInitialMargin",
            "maintenanceMarginRequirement",
            "maintenanceMargin",
            "currentMaintenanceMargin",
            "excessLiquidity",
            "currentExcessLiquidity",
            "availableFunds",
            "buyingPower") is not null;

    private static string? AttributeAny(XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (Attribute(element, name) is { } value)
                return value;
        }
        return null;
    }

    private static IEnumerable<XElement> Descendants(XElement statement, params string[] localNames)
    {
        var names = new HashSet<string>(localNames, StringComparer.OrdinalIgnoreCase);
        return statement.Descendants().Where(element => names.Contains(element.Name.LocalName));
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

    private static string SanitizeAccountForFileName(string account)
    {
        var builder = new StringBuilder(account.Length);
        foreach (var character in account)
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        return builder.Length == 0 ? "account" : builder.ToString();
    }

    private static StatementParseResult EmptyResult(string? profileId, IReadOnlyList<StatementParseIssue> issues)
        => new(ConnectorId, profileId, [], [], [], issues, new StatementFormatFingerprint(string.Empty, [], "xml"));
}
