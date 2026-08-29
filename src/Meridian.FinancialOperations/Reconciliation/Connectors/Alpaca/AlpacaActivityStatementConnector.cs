using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Execution.Sdk;

namespace Meridian.FinancialOperations.Reconciliation.Connectors.Alpaca;

/// <summary>
/// Alpaca account statement connector. Fetches activity (fills + cash transactions +
/// dividend corporate actions) and portfolio state (positions + cash balance) through the
/// registered Alpaca brokerage gateway, retains the combined snapshot as a JSON document,
/// and parses that document into canonical records — so scheduled fetches, ad-hoc fetches,
/// and re-imports of retained snapshots all share one parse path. Degrades to file-only
/// when no Alpaca gateway is registered in the host.
/// </summary>
public sealed class AlpacaActivityStatementConnector : IFetchingStatementConnector
{
    public const string ConnectorId = "alpaca-activity";
    private const string ProviderId = "alpaca";

    private readonly StatementMappingProfileCatalog _catalog;
    private readonly StatementIngressLimits _limits;
    private readonly IBrokerageActivitySync? _activitySync;
    private readonly IBrokeragePortfolioSync? _portfolioSync;

    public AlpacaActivityStatementConnector(
        StatementMappingProfileCatalog catalog,
        IEnumerable<IBrokerageActivitySync> activitySyncs,
        IEnumerable<IBrokeragePortfolioSync> portfolioSyncs,
        StatementIngressLimits? limits = null)
    {
        _catalog = catalog;
        _limits = limits ?? StatementIngressLimits.Default;
        _activitySync = activitySyncs.FirstOrDefault(static sync =>
            string.Equals(sync.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase));
        _portfolioSync = portfolioSyncs.FirstOrDefault(static sync =>
            string.Equals(sync.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase));
        Descriptor = new StatementConnectorDescriptor(
            ConnectorId,
            "Alpaca account activity",
            [".json"],
            SupportsFileImport: true,
            SupportsRemoteFetch: _activitySync is not null || _portfolioSync is not null,
            RequiresMappingProfile: false,
            DefaultProfileId: StatementBuiltInProfiles.AlpacaActivityV1ProfileId);
    }

    public StatementConnectorDescriptor Descriptor { get; }

    public bool CanHandle(StatementSourceDocument document)
    {
        var extension = Path.GetExtension(document.FileName);
        if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var span = document.Content.Span;
        var head = Encoding.UTF8.GetString(span.Length > 1024 ? span[..1024] : span);
        return head.Contains("\"providerId\"", StringComparison.OrdinalIgnoreCase)
            && head.Contains(ProviderId, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<StatementSourceDocument> FetchAsync(StatementFetchRequest request, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExternalAccountId);
        if (request.Since.HasValue
            && request.UntilExclusive.HasValue
            && request.UntilExclusive.Value <= request.Since.Value)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.UntilExclusive),
                request.UntilExclusive,
                "The exclusive statement upper bound must be later than the lower bound.");
        }

        if (request.UntilExclusive.HasValue
            && request.Datasets.HasFlag(StatementFetchDatasets.Positions)
            && _portfolioSync is not null)
        {
            throw new NotSupportedException(
                "Alpaca cannot produce a historical portfolio snapshot at an exact statement-period end. Request bounded activity only.");
        }

        if (_activitySync is null && _portfolioSync is null)
        {
            throw new NotSupportedException("No Alpaca brokerage gateway is registered; remote fetch is unavailable.");
        }

        BrokerageActivitySnapshotDto? activity = null;
        if (request.Datasets.HasFlag(StatementFetchDatasets.Activity) && _activitySync is not null)
        {
            activity = request.UntilExclusive.HasValue
                ? await _activitySync
                    .GetActivitySnapshotAsync(
                        request.ExternalAccountId,
                        request.Since,
                        request.UntilExclusive,
                        ct)
                    .ConfigureAwait(false)
                : await _activitySync
                    .GetActivitySnapshotAsync(request.ExternalAccountId, request.Since, ct)
                    .ConfigureAwait(false);
            EnsureMatchingAccount(request.ExternalAccountId, activity.AccountId, "activity");
        }

        BrokeragePortfolioSnapshotDto? portfolio = null;
        if (request.Datasets.HasFlag(StatementFetchDatasets.Positions) && _portfolioSync is not null)
        {
            portfolio = await _portfolioSync.GetPortfolioSnapshotAsync(request.ExternalAccountId, ct).ConfigureAwait(false);
            EnsureMatchingAccount(request.ExternalAccountId, portfolio.Account.AccountId, "portfolio");
        }

        var retrievedAt = activity?.RetrievedAt ?? portfolio?.RetrievedAt ?? DateTimeOffset.UtcNow;
        var snapshot = new AlpacaStatementSnapshot(ProviderId, request.ExternalAccountId, retrievedAt, activity, portfolio);
        var json = JsonSerializer.Serialize(snapshot, AlpacaStatementSnapshotJsonContext.Default.AlpacaStatementSnapshot);
        var fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"alpaca-activity-{SanitizeAccountForFileName(request.ExternalAccountId)}-{retrievedAt:yyyyMMddHHmmss}.json");
        return new StatementSourceDocument(
            fileName,
            Encoding.UTF8.GetBytes(json),
            request.MappingProfileId,
            request.ExternalAccountId);
    }

    public Task<StatementParseResult> ParseAsync(StatementSourceDocument document, CancellationToken ct = default)
    {
        var issues = new List<StatementParseIssue>();

        // Refuse before deserializing, as every other statement connector does. This one carried no
        // ingress limits at all: the Deserialize below builds the whole snapshot object graph from a
        // caller-supplied document, so a direct in-process caller - one that never passes through
        // StatementImportService, which does check the cap - could size the parse itself.
        if (document.Content.Length > _limits.MaxDocumentBytes)
        {
            issues.Add(_limits.DocumentTooLarge(document.Content.Length));
            return Task.FromResult(EmptyResult(document.MappingProfileId, issues));
        }

        AlpacaStatementSnapshot? snapshot = null;
        try
        {
            snapshot = JsonSerializer.Deserialize(
                document.Content.Span,
                AlpacaStatementSnapshotJsonContext.Default.AlpacaStatementSnapshot);
        }
        catch (JsonException ex)
        {
            issues.Add(StatementParseIssue.Error("INVALID_SNAPSHOT", $"The file is not a valid Alpaca statement snapshot: {ex.Message}"));
        }

        if (snapshot is null)
        {
            if (issues.Count == 0)
            {
                issues.Add(StatementParseIssue.Error("INVALID_SNAPSHOT", "The file is not a valid Alpaca statement snapshot."));
            }

            return Task.FromResult(EmptyResult(document.MappingProfileId, issues));
        }

        var expectedAccount = document.ExternalAccountId?.Trim();
        var snapshotAccount = snapshot.AccountId?.Trim();
        var activityAccount = snapshot.Activity?.AccountId?.Trim();
        var portfolioAccount = snapshot.Portfolio?.Account.AccountId?.Trim();
        if ((!string.IsNullOrWhiteSpace(expectedAccount)
             && !AccountsMatch(expectedAccount, snapshotAccount))
            || (!string.IsNullOrWhiteSpace(activityAccount)
                && !AccountsMatch(snapshotAccount, activityAccount))
            || (!string.IsNullOrWhiteSpace(portfolioAccount)
                && !AccountsMatch(snapshotAccount, portfolioAccount)))
        {
            issues.Add(StatementParseIssue.Error(
                "ACCOUNT_SCOPE_MISMATCH",
                "The Alpaca snapshot account does not match the requested external account."));
            return Task.FromResult(EmptyResult(document.MappingProfileId, issues));
        }

        return ParseSnapshotAsync(document, snapshot, issues, ct);
    }

    private async Task<StatementParseResult> ParseSnapshotAsync(
        StatementSourceDocument document,
        AlpacaStatementSnapshot snapshot,
        List<StatementParseIssue> issues,
        CancellationToken ct)
    {
        var profileId = string.IsNullOrWhiteSpace(document.MappingProfileId)
            ? Descriptor.DefaultProfileId!
            : document.MappingProfileId.Trim();
        var profile = await CatalogProfileAsync(profileId, ct).ConfigureAwait(false);
        if (profile is null)
        {
            issues.Add(StatementParseIssue.Error("PROFILE_NOT_FOUND", $"Mapping profile '{profileId}' is not registered."));
            return EmptyResult(profileId, issues);
        }

        var account = string.IsNullOrWhiteSpace(snapshot.AccountId)
            ? document.ExternalAccountId ?? string.Empty
            : snapshot.AccountId;
        var activityCodeMap = StatementRecordMapper.BuildActivityCodeMap(profile);
        var reportedUnknownCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var records = new List<StatementCanonicalRecord>();
        var rowNumber = 0;

        var richActivities = snapshot.Activity?.Activities;
        if (richActivities is not null)
        {
            foreach (var activity in richActivities)
            {
                rowNumber++;
                records.Add(MapRichActivity(account, activity));
            }
        }
        else
        {
            foreach (var fill in snapshot.Activity?.Fills ?? [])
            {
                rowNumber++;
                var signedQuantity = fill.Side == OrderSide.Sell ? -Math.Abs(fill.Quantity) : Math.Abs(fill.Quantity);
                records.Add(new StatementCanonicalRecord(
                    StatementRecordKind.Transaction,
                    account,
                    fill.Symbol,
                    signedQuantity,
                    fill.Price,
                    -signedQuantity * fill.Price,
                    "trade",
                    DateOnly.FromDateTime(fill.FilledAt.UtcDateTime),
                    Currency: null,
                    FeesCommission: fill.Commission,
                    ExternalTransactionId: fill.FillId,
                    ActivityCategory: BrokerageActivityCategory.Trade.ToString(),
                    ActivitySubtype: BrokerageActivitySubtype.TradeFill.ToString(),
                    OrderId: fill.OrderId));
            }

            foreach (var cash in snapshot.Activity?.CashTransactions ?? [])
            {
                rowNumber++;
                var canonicalActivity = ResolveActivity(cash.TransactionType, activityCodeMap, profile, rowNumber, issues, reportedUnknownCodes);

                // Checked after ResolveActivity, which is what appends the warning: checking before it
                // would leave the last row's diagnostic uncounted, with no later iteration to catch it.
                if (issues.Count > _limits.MaxDiagnostics)
                {
                    issues.Add(_limits.TooManyDiagnostics());
                    return EmptyResult(document.MappingProfileId, issues);
                }
                records.Add(new StatementCanonicalRecord(
                    StatementRecordKindResolver.Resolve(canonicalActivity),
                    account,
                    cash.Symbol ?? string.Empty,
                    0m,
                    0m,
                    cash.Amount,
                    StatementRecordMapper.ToArtifactActivityType(StatementRecordKindResolver.Resolve(canonicalActivity)),
                    DateOnly.FromDateTime(cash.PostedAt.UtcDateTime),
                    Currency: string.IsNullOrWhiteSpace(cash.Currency) ? null : cash.Currency.ToUpperInvariant(),
                    ExternalTransactionId: cash.TransactionId,
                    ProviderActivityCode: cash.TransactionType,
                    Description: cash.Description));
            }

            foreach (var corporateAction in snapshot.Activity?.CorporateActions ?? [])
            {
                if (corporateAction.Amount is not { } amount)
                {
                    continue;
                }

                rowNumber++;
                var canonicalActivity = ResolveActivity(corporateAction.EventType, activityCodeMap, profile, rowNumber, issues, reportedUnknownCodes);

                // Checked after ResolveActivity, which is what appends the warning: checking before it
                // would leave the last row's diagnostic uncounted, with no later iteration to catch it.
                if (issues.Count > _limits.MaxDiagnostics)
                {
                    issues.Add(_limits.TooManyDiagnostics());
                    return EmptyResult(document.MappingProfileId, issues);
                }
                var kind = StatementRecordKindResolver.Resolve(canonicalActivity);
                records.Add(new StatementCanonicalRecord(
                    kind,
                    account,
                    corporateAction.Symbol ?? string.Empty,
                    corporateAction.Quantity ?? 0m,
                    0m,
                    amount,
                    StatementRecordMapper.ToArtifactActivityType(kind),
                    corporateAction.EffectiveDate ?? corporateAction.ExDate ?? DateOnly.FromDateTime(snapshot.RetrievedAt.UtcDateTime),
                    Currency: string.IsNullOrWhiteSpace(corporateAction.Currency) ? null : corporateAction.Currency.ToUpperInvariant(),
                    ExternalTransactionId: corporateAction.EventId,
                    ActivityCategory: BrokerageActivityCategory.CorporateAction.ToString(),
                    ProviderActivityCode: corporateAction.EventType,
                    Description: corporateAction.Description));
            }
        }

        var snapshotDate = DateOnly.FromDateTime(snapshot.RetrievedAt.UtcDateTime);
        foreach (var position in snapshot.Portfolio?.Positions ?? [])
        {
            rowNumber++;
            records.Add(new StatementCanonicalRecord(
                StatementRecordKind.Position,
                account,
                position.Symbol,
                position.Quantity,
                position.MarketPrice,
                position.MarketValue,
                "position",
                snapshotDate,
                Currency: string.IsNullOrWhiteSpace(position.Currency) ? null : position.Currency!.ToUpperInvariant(),
                ExternalTransactionId: position.PositionId));
        }

        if (snapshot.Portfolio?.Balance is { } balance)
        {
            records.Add(new StatementCanonicalRecord(
                StatementRecordKind.CashBalance,
                account,
                string.Empty,
                0m,
                0m,
                balance.Cash,
                "cash",
                snapshotDate,
                Currency: string.IsNullOrWhiteSpace(balance.Currency) ? null : balance.Currency.ToUpperInvariant()));
        }

        if (records.Count == 0)
        {
            issues.Add(StatementParseIssue.Warning(
                "NO_RECORDS",
                "The Alpaca snapshot contains no fills, cash transactions, corporate actions, positions, or balances."));
        }

        if (string.IsNullOrWhiteSpace(account))
        {
            issues.Add(StatementParseIssue.Error("ROW_MISSING_ACCOUNT", "The Alpaca snapshot does not identify an account."));
        }

        var detectedColumns = new[]
        {
            "accountId", "symbol", "quantity", "price", "amount", "activityType", "date", "currency", "commission", "externalId",
            "activityCategory", "activitySubtype", "providerActivityCode", "relatedTransactionId", "orderId", "description"
        };
        return new StatementParseResult(
            ConnectorId,
            profile.ProfileId,
            detectedColumns,
            StatementColumnConfidenceScorer.MapColumns(detectedColumns, profile),
            records,
            issues,
            new StatementFormatFingerprint(
                Sha256Digest.Compute(document.Content.Span),
                detectedColumns.Select(static column => column.ToLowerInvariant()).ToArray(),
                "json"),
            AccountSnapshots: snapshot.Portfolio?.AccountSnapshot is { } accountSnapshot ? [accountSnapshot] : [],
            ActivityEvents: richActivities ?? [],
            ActivityCursors: snapshot.Activity?.Cursor is { } cursor ? [cursor] : [],
            TaxLots: snapshot.Portfolio?.TaxLots ?? [],
            BorrowPositions: snapshot.Portfolio?.BorrowPositions ?? []);
    }

    private static StatementCanonicalRecord MapRichActivity(
        string account,
        BrokerageActivityEventDto activity)
    {
        var kind = activity.Category switch
        {
            BrokerageActivityCategory.Fee => StatementRecordKind.Fee,
            BrokerageActivityCategory.Dividend => StatementRecordKind.Dividend,
            _ => StatementRecordKind.Transaction
        };
        var quantity = activity.Quantity ?? 0m;
        var price = activity.Price ?? 0m;
        var cashAmount = activity.NetAmount;
        if (kind == StatementRecordKind.Transaction &&
            activity.Category == BrokerageActivityCategory.Trade &&
            cashAmount == 0m &&
            activity.Quantity.HasValue &&
            activity.Price.HasValue)
        {
            cashAmount = -quantity * price;
        }

        decimal? commission = null;
        if (activity.Metadata?.TryGetValue("commission", out var commissionText) == true &&
            decimal.TryParse(commissionText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedCommission))
        {
            commission = parsedCommission;
        }

        return new StatementCanonicalRecord(
            Kind: kind,
            Account: account,
            Symbol: activity.Symbol ?? string.Empty,
            Quantity: quantity,
            Price: price,
            CashAmount: cashAmount,
            ActivityType: StatementRecordMapper.ToArtifactActivityType(kind),
            TradeDate: DateOnly.FromDateTime(activity.EffectiveAt.UtcDateTime),
            Currency: string.IsNullOrWhiteSpace(activity.Currency) ? null : activity.Currency.ToUpperInvariant(),
            FeesCommission: commission,
            ExternalTransactionId: activity.EventId,
            ActivityCategory: activity.Category.ToString(),
            ActivitySubtype: activity.Subtype.ToString(),
            ProviderActivityCode: activity.ProviderCode,
            RelatedTransactionId: activity.RelatedEventId,
            OrderId: activity.OrderId,
            Description: activity.Description);
    }

    private static string ResolveActivity(
        string sourceType,
        IReadOnlyDictionary<string, string> activityCodeMap,
        StatementMappingProfileDocument profile,
        int rowNumber,
        List<StatementParseIssue> issues,
        HashSet<string> reportedUnknownCodes)
    {
        if (activityCodeMap.TryGetValue(sourceType, out var canonical))
        {
            return canonical;
        }

        if (reportedUnknownCodes.Add(sourceType))
        {
            issues.Add(StatementParseIssue.Warning(
                "UNKNOWN_ACTIVITY_CODE",
                $"Activity code '{sourceType}' is not mapped in profile '{profile.ProfileId}'; rows with it are treated as transactions. Add the code to the profile's activity codes to classify it.",
                rowNumber,
                "ActivityType"));
        }

        return sourceType;
    }

    private Task<StatementMappingProfileDocument?> CatalogProfileAsync(string profileId, CancellationToken ct)
        => _catalog.FindAsync(profileId, ct);

    private static void EnsureMatchingAccount(string expectedAccountId, string? actualAccountId, string dataset)
    {
        if (!AccountsMatch(expectedAccountId, actualAccountId))
        {
            throw new InvalidDataException(
                $"The Alpaca {dataset} snapshot account does not match the requested external account.");
        }
    }

    private static bool AccountsMatch(string? expectedAccountId, string? actualAccountId)
        => !string.IsNullOrWhiteSpace(expectedAccountId)
           && !string.IsNullOrWhiteSpace(actualAccountId)
           && string.Equals(
               expectedAccountId.Trim(),
               actualAccountId.Trim(),
               StringComparison.OrdinalIgnoreCase);

    private static string SanitizeAccountForFileName(string account)
    {
        var builder = new StringBuilder(account.Length);
        foreach (var character in account)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        }

        return builder.Length == 0 ? "account" : builder.ToString();
    }

    private static StatementParseResult EmptyResult(string? profileId, IReadOnlyList<StatementParseIssue> issues)
        => new(ConnectorId, profileId, [], [], [], issues, new StatementFormatFingerprint(string.Empty, [], "json"));
}
