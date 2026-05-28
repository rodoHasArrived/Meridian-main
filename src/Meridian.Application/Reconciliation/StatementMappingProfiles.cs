using System.Globalization;

namespace Meridian.Application.Reconciliation;

public enum StatementCanonicalField
{
    Account,
    SecurityIdentifier,
    Quantity,
    Price,
    CashAmount,
    ActivityType,
    TradeDate,
    SettlementDate,
    Currency,
    FeesCommission,
    ExternalTransactionId
}

public sealed record StatementFieldMapping(
    StatementCanonicalField CanonicalField,
    string SourceColumn,
    bool Required = true);

public sealed record StatementTransactionCodeMapping(
    string SourceCode,
    string CanonicalActivityType);

public sealed record StatementMappingProfile(
    string ProfileId,
    string DisplayName,
    IReadOnlyList<StatementFieldMapping> FieldMappings,
    IReadOnlyList<StatementTransactionCodeMapping> TransactionCodeMappings)
{
    public StatementFieldMapping? FindField(StatementCanonicalField field) =>
        FieldMappings.FirstOrDefault(mapping => mapping.CanonicalField == field);

    public string MapActivityType(string activityType)
    {
        var mapped = TransactionCodeMappings.FirstOrDefault(mapping =>
            string.Equals(mapping.SourceCode, activityType, StringComparison.OrdinalIgnoreCase));
        return mapped?.CanonicalActivityType ?? activityType;
    }
}

public sealed class StatementMappingProfileRegistry
{
    public const string CanonicalCsvV1ProfileId = "canonical-csv-v1";
    public const string SampleBrokerCsvV1ProfileId = "sample-broker-csv-v1";

    private readonly Dictionary<string, StatementMappingProfile> _profiles;

    public StatementMappingProfileRegistry(IEnumerable<StatementMappingProfile> profiles)
    {
        _profiles = profiles.ToDictionary(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase);
    }

    public static StatementMappingProfileRegistry Defaults { get; } = new(CreateDefaultProfiles());

    public StatementMappingProfile Resolve(string? profileId)
    {
        var effectiveProfileId = string.IsNullOrWhiteSpace(profileId) ? CanonicalCsvV1ProfileId : profileId.Trim();
        if (_profiles.TryGetValue(effectiveProfileId, out var profile))
        {
            return profile;
        }

        throw new NotSupportedException($"Statement mapping profile '{profileId}' is not registered.");
    }

    public StatementMappingProfile ResolveForSourceKind(string normalizedSourceKind, string? profileId = null)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            return Resolve(profileId);
        }

        return Resolve(normalizedSourceKind switch
        {
            "sample-broker" => SampleBrokerCsvV1ProfileId,
            _ => CanonicalCsvV1ProfileId
        });
    }

    public IReadOnlyList<StatementMappingProfile> ListProfiles() =>
        _profiles.Values.OrderBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyList<StatementMappingProfile> CreateDefaultProfiles() =>
    [
        new StatementMappingProfile(
            CanonicalCsvV1ProfileId,
            "Canonical CSV v1",
            [
                new(StatementCanonicalField.Account, "account"),
                new(StatementCanonicalField.SecurityIdentifier, "symbol"),
                new(StatementCanonicalField.Quantity, "quantity"),
                new(StatementCanonicalField.Price, "price"),
                new(StatementCanonicalField.CashAmount, "cashAmount"),
                new(StatementCanonicalField.ActivityType, "activityType"),
                new(StatementCanonicalField.TradeDate, "tradeDate"),
                new(StatementCanonicalField.SettlementDate, "settlementDate", Required: false),
                new(StatementCanonicalField.Currency, "currency", Required: false),
                new(StatementCanonicalField.FeesCommission, "feesCommission", Required: false),
                new(StatementCanonicalField.ExternalTransactionId, "externalTransactionId", Required: false)
            ],
            [
                new("position", "position"),
                new("cash", "cash"),
                new("cashbalance", "cashbalance"),
                new("trade", "trade"),
                new("fee", "fee"),
                new("dividend", "dividend"),
                new("div", "dividend")
            ]),
        new StatementMappingProfile(
            SampleBrokerCsvV1ProfileId,
            "Sample broker CSV v1",
            [
                new(StatementCanonicalField.Account, "BrokerAccount"),
                new(StatementCanonicalField.SecurityIdentifier, "Ticker"),
                new(StatementCanonicalField.Quantity, "Units"),
                new(StatementCanonicalField.Price, "ExecutionPrice"),
                new(StatementCanonicalField.CashAmount, "NetCash"),
                new(StatementCanonicalField.ActivityType, "TxnCode"),
                new(StatementCanonicalField.TradeDate, "TradeDate"),
                new(StatementCanonicalField.SettlementDate, "SettleDate", Required: false),
                new(StatementCanonicalField.Currency, "CCY", Required: false),
                new(StatementCanonicalField.FeesCommission, "Commission", Required: false),
                new(StatementCanonicalField.ExternalTransactionId, "BrokerTransactionId", Required: false)
            ],
            [
                new("POS", "position"),
                new("CASH", "cash"),
                new("BUY", "trade"),
                new("SELL", "trade"),
                new("FEE", "fee"),
                new("DIV", "dividend")
            ])
    ];
}

internal sealed class StatementMappedCsvRow
{
    private readonly IReadOnlyDictionary<string, string> _valuesByColumn;

    public StatementMappedCsvRow(StatementMappingProfile profile, IReadOnlyDictionary<string, string> valuesByColumn)
    {
        Profile = profile;
        _valuesByColumn = valuesByColumn;
    }

    public StatementMappingProfile Profile { get; }

    public string GetRequired(StatementCanonicalField field, int rowNumber)
    {
        var value = GetOptional(field);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"Statement row {rowNumber} is missing required mapped field '{field}'.");
        }

        return value;
    }

    public string? GetOptional(StatementCanonicalField field)
    {
        var mapping = Profile.FindField(field);
        if (mapping is null || !_valuesByColumn.TryGetValue(mapping.SourceColumn, out var value))
        {
            return null;
        }

        return value;
    }

    public decimal GetRequiredDecimal(StatementCanonicalField field, int rowNumber) =>
        decimal.Parse(GetRequired(field, rowNumber), NumberStyles.Number, CultureInfo.InvariantCulture);

    public DateOnly GetRequiredDate(StatementCanonicalField field, int rowNumber) =>
        DateOnly.Parse(GetRequired(field, rowNumber), CultureInfo.InvariantCulture);

    public Dictionary<string, string> ToCanonicalSnapshot()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in Profile.FieldMappings)
        {
            if (_valuesByColumn.TryGetValue(mapping.SourceColumn, out var value))
            {
                snapshot[mapping.CanonicalField.ToString()] = value;
            }
        }

        return snapshot;
    }
}
