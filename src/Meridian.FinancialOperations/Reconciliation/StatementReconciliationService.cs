using System.Globalization;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;

namespace Meridian.FinancialOperations.Reconciliation;

public sealed class StatementReconciliationService
{
    private readonly StatementBreakClassifier _breakClassifier = new();
    private static readonly string[] CanonicalStatementColumns =
    [
        "account",
        "symbol",
        "quantity",
        "price",
        "cashAmount",
        "activityType",
        "tradeDate"
    ];
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
        if (RequiresCanonicalStatementSchema(normalizedSourceKind))
        {
            var profile = ValidateStatementHeader(normalizedSourceKind, sourcePath, profileId);
            profileId = profile.ProfileId;
        }

        var profileSuffix = string.IsNullOrWhiteSpace(profileId) ? string.Empty : $" using mapping profile '{profileId}'";
        return Task.FromResult($"Statement source '{normalizedSourceKind}:{sourcePath}' passed local file accessibility checks{profileSuffix}.");
    }

    public async Task<NormalizedStatementImportResult> ImportAsync(string sourceKind, string sourcePath, CancellationToken ct)
    {
        var normalizedSourceKind = ValidateSourceAccess(sourceKind, sourcePath);
        ct.ThrowIfCancellationRequested();
        if (RequiresCanonicalStatementSchema(normalizedSourceKind))
        {
            return await ReadNormalizedStatementImportAsync(normalizedSourceKind, sourcePath, ct).ConfigureAwait(false);
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
        if (!string.Equals(normalizedSourceKind, "local", StringComparison.Ordinal)
            && !string.Equals(normalizedSourceKind, "broker", StringComparison.Ordinal)
            && !string.Equals(normalizedSourceKind, "custodian", StringComparison.Ordinal)
            && !string.Equals(normalizedSourceKind, "sample-broker", StringComparison.Ordinal))
            throw new NotSupportedException($"Statement source kind '{sourceKind}' is not supported. Use 'local', 'broker', 'custodian', or 'sample-broker'.");

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Statement source file '{sourcePath}' was not found.", sourcePath);

        return normalizedSourceKind;
    }

    private static bool RequiresCanonicalStatementSchema(string normalizedSourceKind) =>
        string.Equals(normalizedSourceKind, "broker", StringComparison.Ordinal)
        || string.Equals(normalizedSourceKind, "custodian", StringComparison.Ordinal)
        || string.Equals(normalizedSourceKind, "sample-broker", StringComparison.Ordinal);

    private ExternalStatementCaseIntakeResult CreateExternalStatementCases(string normalizedSourceKind, string sourcePath, string? mappingProfileId = null)
    {
        if (!RequiresCanonicalStatementSchema(normalizedSourceKind))
        {
            var content = File.ReadAllText(sourcePath);
            var importId = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}|{content}");
            return new ExternalStatementCaseIntakeResult(importId, normalizedSourceKind, sourcePath, 0, 0, []);
        }

        var rows = ReadCanonicalStatementRows(normalizedSourceKind, sourcePath, mappingProfileId);
        var (matches, cases) = MatchRows(rows);
        return new ExternalStatementCaseIntakeResult(
            rows.Count == 0 ? DeterministicFingerprint.Compute($"{normalizedSourceKind}|{mappingProfileId}|{sourcePath}") : rows[0].RawSnapshot["importId"],
            normalizedSourceKind,
            sourcePath,
            rows.Count,
            matches.Count,
            cases);
    }

    private static async Task<NormalizedStatementImportResult> ReadNormalizedStatementImportAsync(
        string normalizedSourceKind,
        string sourcePath,
        CancellationToken ct)
    {
        ValidateCanonicalStatementHeader(sourcePath);

        var content = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
        var importId = DeterministicFingerprint.Compute($"{normalizedSourceKind}|{sourcePath}|{content}");
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

            switch (ToStatementRowKind(p.TransactionType))
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
            if (parts.Length < CanonicalStatementColumns.Length)
            {
                throw new InvalidDataException($"Statement row {currentRowNumber} has {parts.Length} columns; expected at least {CanonicalStatementColumns.Length}.");
            }

            var account = parts[0];
            var symbol = parts[1];
            var quantity = decimal.Parse(parts[2], CultureInfo.InvariantCulture);
            var price = decimal.Parse(parts[3], CultureInfo.InvariantCulture);
            var cashAmount = decimal.Parse(parts[4], CultureInfo.InvariantCulture);
            var activityType = parts[5];
            var tradeDate = DateOnly.Parse(parts[6], CultureInfo.InvariantCulture);
            var sourceRow = CreateSourceRowReference(importId, currentRowNumber, line, CreateRawSnapshot(importId, normalizedSourceKind, sourcePath, currentRowNumber, line, parts));
            var snapshot = sourceRow.RawSnapshot;
            var securityId = GetOptional(parts, 9);
            var unresolvedIdentifier = GetOptional(parts, 10) ?? (string.IsNullOrWhiteSpace(symbol) ? null : symbol);
            var currency = GetOptional(parts, 11) ?? "USD";
            var marketValue = GetOptionalDecimal(parts, 12) ?? price * quantity;
            var settlementDate = GetOptionalDate(parts, 13);
            var amount = GetOptionalDecimal(parts, 14) ?? (cashAmount == 0m ? marketValue : cashAmount);
            var feesCommission = GetOptionalDecimal(parts, 15) ?? 0m;
            var externalReference = GetOptional(parts, 16);
            var externalAccountId = GetOptional(parts, 8) ?? account;
            var accountId = GetOptional(parts, 7) ?? account;
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
                activityType,
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
            if (parts.Length < header.Length)
            {
                throw new InvalidDataException($"Statement row {rowNumber} has {parts.Length} columns; expected at least {header.Length} for mapping profile '{profile.ProfileId}'.");
            }

            var mapped = new StatementMappedCsvRow(profile, BuildColumnMap(header, parts));
            var account = mapped.GetRequired(StatementCanonicalField.Account, rowNumber);
            var activityType = profile.MapActivityType(mapped.GetRequired(StatementCanonicalField.ActivityType, rowNumber));
            var rowKind = ToStatementRowKind(activityType);
            var symbol = rowKind == StatementRowKind.CashBalance
                ? mapped.GetOptional(StatementCanonicalField.SecurityIdentifier) ?? string.Empty
                : mapped.GetRequired(StatementCanonicalField.SecurityIdentifier, rowNumber);
            var quantity = mapped.GetRequiredDecimal(StatementCanonicalField.Quantity, rowNumber);
            var price = mapped.GetRequiredDecimal(StatementCanonicalField.Price, rowNumber);
            var cashAmount = mapped.GetRequiredDecimal(StatementCanonicalField.CashAmount, rowNumber);
            var tradeDate = mapped.GetRequiredDate(StatementCanonicalField.TradeDate, rowNumber);
            var settlementDate = mapped.GetOptional(StatementCanonicalField.SettlementDate);
            var currency = mapped.GetOptional(StatementCanonicalField.Currency) ?? "USD";
            var feesCommission = mapped.GetOptional(StatementCanonicalField.FeesCommission);
            var externalTransactionId = mapped.GetOptional(StatementCanonicalField.ExternalTransactionId);
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
            if (!string.IsNullOrWhiteSpace(settlementDate)) rawSnapshot["settlementDate"] = settlementDate;
            if (!string.IsNullOrWhiteSpace(currency)) rawSnapshot["currency"] = currency;
            if (!string.IsNullOrWhiteSpace(feesCommission)) rawSnapshot["feesCommission"] = feesCommission;
            if (!string.IsNullOrWhiteSpace(externalTransactionId)) rawSnapshot["externalTransactionId"] = externalTransactionId;

            rows.Add(new NormalizedStatementRow(
                $"{importId}:{rowNumber}",
                rowKind,
                symbol,
                quantity,
                cashAmount == 0m ? price * quantity : cashAmount,
                new DateTimeOffset(tradeDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                currency,
                rowFingerprint,
                rawSnapshot));
        }

        return rows;
    }


    private static void ValidateCanonicalStatementHeader(string sourcePath)
    {
        var header = File.ReadLines(sourcePath).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header))
        {
            throw new InvalidDataException("Statement source file is empty.");
        }

        var actual = header.Split(',', StringSplitOptions.TrimEntries);
        EnsureUniqueStatementHeaderColumns(actual, StatementMappingProfileRegistry.CanonicalCsvV1ProfileId);
        if (!CanonicalCsvHeaderPrefixMatches(actual))
        {
            throw new InvalidDataException("Statement source must use the canonical external statement header: account,symbol,quantity,price,cashAmount,activityType,tradeDate.");
        }
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

    private static StatementSourceRowReference CreateSourceRowReference(
        string importId,
        int rowNumber,
        string line,
        IReadOnlyDictionary<string, string> rawSnapshot)
        => new(importId, rowNumber, DeterministicFingerprint.Compute($"{importId}|{rowNumber}|{line}"), rawSnapshot);

    private static IReadOnlyDictionary<string, string> CreateRawSnapshot(
        string importId,
        string normalizedSourceKind,
        string sourcePath,
        int rowNumber,
        string line,
        string[] parts)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["importId"] = importId,
            ["sourceKind"] = normalizedSourceKind,
            ["sourcePath"] = sourcePath,
            ["account"] = parts[0],
            ["symbol"] = parts[1],
            ["quantity"] = parts[2],
            ["price"] = parts[3],
            ["cashAmount"] = parts[4],
            ["activityType"] = parts[5],
            ["tradeDate"] = parts[6],
            ["rowNumber"] = rowNumber.ToString(),
            ["rawLine"] = line
        };

        AddOptional(snapshot, "accountId", parts, 7);
        AddOptional(snapshot, "externalAccountId", parts, 8);
        AddOptional(snapshot, "securityId", parts, 9);
        AddOptional(snapshot, "unresolvedIdentifier", parts, 10);
        AddOptional(snapshot, "currency", parts, 11);
        AddOptional(snapshot, "marketValue", parts, 12);
        AddOptional(snapshot, "settlementDate", parts, 13);
        AddOptional(snapshot, "amount", parts, 14);
        AddOptional(snapshot, "feesCommission", parts, 15);
        AddOptional(snapshot, "externalReference", parts, 16);
        return snapshot;
    }

    private static void AddOptional(Dictionary<string, string> snapshot, string key, string[] parts, int index)
    {
        var value = GetOptional(parts, index);
        if (value is not null)
        {
            snapshot[key] = value;
        }
    }

    private static string? GetOptional(string[] parts, int index)
    {
        if (parts.Length <= index || string.IsNullOrWhiteSpace(parts[index]))
        {
            return null;
        }

        return parts[index];
    }

    private static decimal? GetOptionalDecimal(string[] parts, int index) =>
        GetOptional(parts, index) is { } value ? decimal.Parse(value, CultureInfo.InvariantCulture) : null;

    private static DateOnly? GetOptionalDate(string[] parts, int index) =>
        GetOptional(parts, index) is { } value ? DateOnly.Parse(value, CultureInfo.InvariantCulture) : null;

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

    public (IReadOnlyList<ReconciliationMatchLink> Matches, IReadOnlyList<ReconciliationCase> Cases) MatchRows(
        IReadOnlyList<NormalizedStatementRow> rows)
    {
        var matches = new List<ReconciliationMatchLink>();
        var cases = new List<ReconciliationCase>();

        foreach (var row in rows)
        {
            if (row.Kind == StatementRowKind.Position && Math.Abs(row.Quantity) > 0)
            {
                matches.Add(new ReconciliationMatchLink(row.RowId, "position:auto", null, null, null, null, null, "high", "Symbol and quantity aligned within tolerance rule position-default-v1.")
                {
                    ToleranceProfileId = StatementToleranceProfile.DefaultProfileId,
                    ToleranceProfileVersion = StatementToleranceProfile.DefaultProfileVersion,
                    ToleranceRuleId = "position-default-v1"
                });
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
