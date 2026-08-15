using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Npgsql;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private static DateTimeOffset ReadUtcDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        var value = reader.GetDateTime(ordinal);
        if (value.Kind == DateTimeKind.Unspecified)
        {
            value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        return new DateTimeOffset(value.ToUniversalTime(), TimeSpan.Zero);
    }

    private static JournalEntryMetadata DeserializeMetadata(string json)
        => JsonSerializer.Deserialize<JournalEntryMetadata>(json, JsonOptions) ?? new JournalEntryMetadata();

    private static LedgerEntryCurrency? ReadLegCurrency(
        NpgsqlDataReader reader,
        int transactionCurrencyOrdinal,
        int functionalCurrencyOrdinal,
        int transactionDebitOrdinal,
        int transactionCreditOrdinal,
        int fxRateOrdinal)
    {
        if (reader.IsDBNull(transactionCurrencyOrdinal)
            || reader.IsDBNull(functionalCurrencyOrdinal)
            || reader.IsDBNull(transactionDebitOrdinal)
            || reader.IsDBNull(transactionCreditOrdinal)
            || reader.IsDBNull(fxRateOrdinal))
        {
            return null;
        }

        return new LedgerEntryCurrency(
            reader.GetString(transactionCurrencyOrdinal),
            reader.GetString(functionalCurrencyOrdinal),
            reader.GetDecimal(transactionDebitOrdinal),
            reader.GetDecimal(transactionCreditOrdinal),
            reader.GetDecimal(fxRateOrdinal));
    }

    private static object SerializeAdjustmentApproval(LedgerAdjustmentApprovalMetadataDto? approval)
        => approval is null ? DBNull.Value : JsonSerializer.Serialize(approval, JsonOptions);

    private static object SerializeLineDimensions(LedgerLineDimensionSet? dimensions)
    {
        var canonical = CanonicalizeLineDimensions(dimensions);
        return canonical is null ? DBNull.Value : JsonSerializer.Serialize(canonical, JsonOptions);
    }

    private static LedgerLineDimensionSet DeserializeLineDimensions(string json)
    {
        var dimensions = JsonSerializer.Deserialize<LedgerLineDimensionSet>(json, JsonOptions)
           ?? throw new LedgerValidationException("Stored ledger line dimensions are invalid.");
        return CanonicalizeLineDimensions(dimensions) ?? new LedgerLineDimensionSet();
    }

    internal static string? BuildLineDimensionContainmentJson(LedgerLineDimensionSet? dimensions)
    {
        dimensions = CanonicalizeLineDimensions(dimensions);
        if (dimensions is null)
        {
            return null;
        }

        var values = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["externalGlDimensions"] = dimensions.ExternalGlDimensions
        };
        AddDimension(values, "fundId", dimensions.FundId);
        AddDimension(values, "entityId", dimensions.EntityId);
        AddDimension(values, "sleeveId", dimensions.SleeveId);
        AddDimension(values, "strategyId", dimensions.StrategyId);
        AddDimension(values, "investorId", dimensions.InvestorId);
        AddDimension(values, "capitalAccountId", dimensions.CapitalAccountId);
        if (dimensions.InstrumentId.HasValue)
        {
            values["instrumentId"] = dimensions.InstrumentId.Value;
        }

        if (dimensions.PositionId.HasValue)
        {
            values["positionId"] = dimensions.PositionId.Value;
        }

        AddDimension(values, "taxLotId", dimensions.TaxLotId);
        AddDimension(values, "costCenterId", dimensions.CostCenterId);
        AddDimension(values, "counterpartyId", dimensions.CounterpartyId);
        AddDimension(values, "organizationId", dimensions.OrganizationId);
        AddDimension(values, "portfolioId", dimensions.PortfolioId);
        AddDimension(values, "bookId", dimensions.BookId);
        AddDimension(values, "accountId", dimensions.AccountId);
        AddDimension(values, "customerId", dimensions.CustomerId);
        AddDimension(values, "vendorId", dimensions.VendorId);
        AddDimension(values, "projectId", dimensions.ProjectId);

        if (values["externalGlDimensions"] is IReadOnlyDictionary<string, string> { Count: 0 })
        {
            values.Remove("externalGlDimensions");
        }

        return JsonSerializer.Serialize(values, JsonOptions);
    }

    internal static string BuildJournalEntryQueryFilterSql(
        string journalEntriesTable,
        string journalLegsTable,
        string accountingPeriodsTable,
        LedgerJournalEntryQuery? query = null)
    {
        var sql = $"""

            where je.journal_entry_id in (
                select distinct je_filter.journal_entry_id
                from {journalEntriesTable} je_filter
                join {journalLegsTable} jl_filter on jl_filter.journal_entry_id = je_filter.journal_entry_id
                join {accountingPeriodsTable} p_filter on p_filter.period_id = je_filter.period_id
                where 1 = 1
            """;

        return query?.SourceEventId.HasValue == true
            ? sql + " and je_filter.source_event_id = @source_event_id"
            : sql;
    }

    private static void AddDimension(IDictionary<string, object> values, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[name] = value.Trim();
        }
    }

    internal static LedgerLineDimensionSet? CanonicalizeLineDimensions(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var externalGlDimensions = NormalizeExternalGlDimensions(dimensions.ExternalGlDimensions);
        var canonical = new LedgerLineDimensionSet(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: externalGlDimensions,
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId
        };

        return HasAnyLineDimension(canonical) ? canonical : null;
    }

    private static IReadOnlyDictionary<string, string> NormalizeExternalGlDimensions(IReadOnlyDictionary<string, string> dimensions)
        => dimensions
            .Select(static pair => new
            {
                Key = NormalizeOptional(pair.Key),
                Value = NormalizeOptional(pair.Value)
            })
            .Where(static pair => pair.Key is not null && pair.Value is not null)
            .GroupBy(static pair => pair.Key!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.First().Key!, static group => group.First().Value!, StringComparer.OrdinalIgnoreCase);

    private static bool HasAnyLineDimension(LedgerLineDimensionSet dimensions)
        => !string.IsNullOrWhiteSpace(dimensions.FundId) ||
           !string.IsNullOrWhiteSpace(dimensions.EntityId) ||
           !string.IsNullOrWhiteSpace(dimensions.SleeveId) ||
           !string.IsNullOrWhiteSpace(dimensions.StrategyId) ||
           !string.IsNullOrWhiteSpace(dimensions.InvestorId) ||
           !string.IsNullOrWhiteSpace(dimensions.CapitalAccountId) ||
           dimensions.InstrumentId.HasValue ||
           dimensions.PositionId.HasValue ||
           !string.IsNullOrWhiteSpace(dimensions.TaxLotId) ||
           !string.IsNullOrWhiteSpace(dimensions.CostCenterId) ||
           !string.IsNullOrWhiteSpace(dimensions.CounterpartyId) ||
           !string.IsNullOrWhiteSpace(dimensions.OrganizationId) ||
           !string.IsNullOrWhiteSpace(dimensions.PortfolioId) ||
           !string.IsNullOrWhiteSpace(dimensions.BookId) ||
           !string.IsNullOrWhiteSpace(dimensions.AccountId) ||
           !string.IsNullOrWhiteSpace(dimensions.CustomerId) ||
           !string.IsNullOrWhiteSpace(dimensions.VendorId) ||
           !string.IsNullOrWhiteSpace(dimensions.ProjectId) ||
           dimensions.ExternalGlDimensions.Any(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value));

    private static LedgerAdjustmentApprovalMetadataDto DeserializeAdjustmentApproval(string json)
        => JsonSerializer.Deserialize<LedgerAdjustmentApprovalMetadataDto>(json, JsonOptions)
           ?? throw new LedgerValidationException("Stored adjustment approval metadata is invalid.");

    private static string RequireLineageText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LedgerValidationException($"{parameterName} is required for basis-aware ledger lineage.");
        }

        return value.Trim();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record JournalEntryBuilder(
        long GlobalSequence,
        Guid JournalEntryId,
        Guid AggregateId,
        Guid PeriodId,
        Guid? CommandId,
        Guid? CorrelationId,
        AccountingBasisKindDto AccountingBasis,
        string AccountingPolicyId,
        string AccountingPolicyVersion,
        string? RuleId,
        string? RuleVersion,
        Guid? SourceEventId,
        Guid? SourceJournalEntryId,
        LedgerPostingKindDto PostingKind,
        LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval,
        DateTimeOffset Timestamp,
        string Description,
        JournalEntryMetadata Metadata,
        DateTimeOffset CreatedAt)
    {
        public List<LedgerEntry> Lines { get; } = [];

        public LedgerJournalEntryRecord Build()
            => new(
                new JournalEntry(JournalEntryId, Timestamp, Description, Lines, Metadata),
                AggregateId,
                PeriodId,
                CommandId,
                CorrelationId,
                GlobalSequence,
                CreatedAt,
                AccountingBasis,
                AccountingPolicyId,
                AccountingPolicyVersion,
                RuleId,
                RuleVersion,
                SourceEventId,
                SourceJournalEntryId,
                PostingKind,
                AdjustmentApproval);
    }
}
