using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.FixedIncome;

/// <summary>Supported source bytes read only from an independently accepted account Evidence Vault document.</summary>
public sealed record BondPerformanceTransactionsDocument(
    int SchemaVersion, string Kind, string AccountId, string BookId,
    DateOnly PeriodStart, DateOnly PeriodEnd, long Revision, string SourceDomain, string SourceEntityId,
    [property: JsonRequired] bool CompleteTradePopulation,
    [property: JsonRequired] bool CompleteCashPopulation,
    IReadOnlyList<BondPerformanceSourceTrade> TradeCash, IReadOnlyList<string> PaymentVersionIds)
{
    [JsonRequired] public string HoldingBasis { get; init; } = null!;
    [JsonRequired] public Guid OpeningHoldingsBatchId { get; init; }
    [JsonRequired] public Guid ClosingHoldingsBatchId { get; init; }
}

public sealed record BondPerformanceSourceTrade(string TransactionId, Guid SecurityId,
    DateOnly CashDate, [property: JsonRequired] decimal Amount, string Currency)
{
    [JsonRequired] public string Kind { get; init; } = null!;
    [JsonRequired] public decimal OriginalFaceDelta { get; init; }
    public string? PaymentVersionId { get; init; }
}

public sealed record BondPerformanceTransactionPopulationDto(
    string PopulationId, AccountCashForecastScopeDto Scope, BondPerformanceTransactionsDocument Document,
    RetainedEvidenceIdentityDto Evidence, DateTimeOffset RecordedAtUtc, string RecordedBy);

public sealed record BondPerformanceTransactionImportRequest(string EvidenceUri, string DocumentId);

public interface IBondPerformanceTransactionPopulationStore
{
    Task<BondPerformanceTransactionPopulationDto> AppendAsync(BondPerformanceTransactionPopulationDto population, CancellationToken ct = default);
    Task<IReadOnlyList<BondPerformanceTransactionPopulationDto>> ReadAsync(AccountCashForecastScopeDto scope, CancellationToken ct = default);
}

public static class BondPerformanceTransactionRules
{
    public static void Validate(BondPerformanceTransactionPopulationDto population)
    {
        ArgumentNullException.ThrowIfNull(population);
        var source = population.Document;
        if (population.Scope is null || string.IsNullOrWhiteSpace(population.Scope.TenantId) || string.IsNullOrWhiteSpace(population.Scope.CompanyId)
            || string.IsNullOrWhiteSpace(population.Scope.AccountId) || string.IsNullOrWhiteSpace(population.Scope.BookId)
            || population.RecordedAtUtc == default || population.RecordedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(population.RecordedBy)
            || !RetainedEvidenceIdentityValidator.IsComplete(population.Evidence)
            || population.Evidence.SubjectType != "account" || population.Evidence.SubjectId != population.Scope.AccountId
            || population.Evidence.RetainedAtUtc > population.RecordedAtUtc
            || source is null || source.SchemaVersion != 1 || source.Kind != "bondPerformanceTransactions"
            || source.AccountId != population.Scope.AccountId || source.BookId != population.Scope.BookId
            || source.PeriodStart == default || source.PeriodEnd <= source.PeriodStart || source.Revision < 1
            || source.Revision != population.Evidence.EvidenceVersion || source.PeriodEnd != population.Evidence.EffectiveDate
            || source.PeriodEnd > DateOnly.FromDateTime(population.RecordedAtUtc.UtcDateTime)
            || string.IsNullOrWhiteSpace(source.SourceDomain) || string.IsNullOrWhiteSpace(source.SourceEntityId)
            || source.SourceDomain != population.Evidence.SourceSystem || source.SourceEntityId != population.Evidence.SourceReference
            || source.HoldingBasis != "Settled" || source.OpeningHoldingsBatchId == Guid.Empty || source.ClosingHoldingsBatchId == Guid.Empty
            || source.TradeCash is null || source.PaymentVersionIds is null || source.TradeCash.Count > 10000
            || source.TradeCash.Any(row => row is null || string.IsNullOrWhiteSpace(row.TransactionId) || row.SecurityId == Guid.Empty
                || row.CashDate <= source.PeriodStart || row.CashDate > source.PeriodEnd
                || row.Kind is not ("Buy" or "Sell" or "PrincipalRedemption") || row.OriginalFaceDelta == 0m
                || row.Kind == "Buy" && row.OriginalFaceDelta <= 0m || row.Kind == "Sell" && row.OriginalFaceDelta >= 0m
                || row.Kind == "PrincipalRedemption" && (row.Amount != 0m || string.IsNullOrWhiteSpace(row.PaymentVersionId)
                    || !source.PaymentVersionIds.Contains(row.PaymentVersionId, StringComparer.Ordinal))
                || row.Currency is null || row.Currency.Length != 3 || !row.Currency.All(char.IsAsciiLetter) || row.Currency != row.Currency.ToUpperInvariant())
            || source.TradeCash.Select(row => row.TransactionId).Distinct(StringComparer.Ordinal).Count() != source.TradeCash.Count
            || source.PaymentVersionIds.Any(string.IsNullOrWhiteSpace)
            || source.PaymentVersionIds.Distinct(StringComparer.Ordinal).Count() != source.PaymentVersionIds.Count)
            throw new ArgumentException("A retained transaction population requires exact account/book ownership, reviewed source identity, explicit coverage, unique signed transactions and valid dates/currencies.");
        if (population.PopulationId != Identity(population.Scope, population.Evidence))
            throw new InvalidDataException("Transaction population identity differs from its retained account source evidence.");
    }

    public static string Identity(AccountCashForecastScopeDto scope, RetainedEvidenceIdentityDto evidence)
        => Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new[] { scope.TenantId, scope.CompanyId, scope.AccountId, scope.BookId,
            evidence.EvidenceId, evidence.ContentHashSha256 }));
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(BondPerformanceTransactionsDocument))]
[JsonSerializable(typeof(BondPerformanceTransactionPopulationDto))]
[JsonSerializable(typeof(BondPerformanceTransactionPopulationDto[]))]
[JsonSerializable(typeof(BondPerformanceTransactionImportRequest))]
public partial class BondPerformanceTransactionsJsonContext : JsonSerializerContext;
