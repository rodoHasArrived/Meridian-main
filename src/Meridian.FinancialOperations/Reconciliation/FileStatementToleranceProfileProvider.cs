using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Meridian.FinancialOperations.Reconciliation;

/// <summary>
/// Loads the production <see cref="IStatementToleranceProfileProvider"/> from an operator-maintained
/// profile table under the deployment data root (<c>reconciliation/tolerance-profiles.json</c>). This
/// is the seam that lets a statement run configured with a non-default <c>ToleranceProfileId</c>
/// resolve the operator's thresholds instead of throwing and silently falling back to the defaults.
/// </summary>
/// <remarks>
/// The built-in <see cref="StatementToleranceProfile.Default"/> is always included, so
/// <see cref="StatementToleranceProfile.DefaultProfileId"/> always resolves. A missing, empty, or
/// malformed file yields a default-only provider, and an unknown profile id still fails closed at the
/// workflow so the recorded profile is the profile actually applied. Settlement-date tolerances are
/// expressed in whole or fractional days. The file format is:
/// <code>
/// {
///   "profiles": [
///     {
///       "profileId": "custom", "version": 1,
///       "cashRules": [ { "ruleId": "c1", "absoluteCashTolerance": 0.05, "basisPointCashTolerance": null, "settlementDateToleranceDays": 1 } ],
///       "positionRules": [ { "ruleId": "p1", "quantityTolerance": 0.0001, "marketValueTolerance": 0, "priceTolerance": 0 } ],
///       "transactionRules": [ { "ruleId": "t1", "absoluteCashTolerance": 0.05, "settlementDateToleranceDays": 1, "priceTolerance": 0 } ]
///     }
///   ]
/// }
/// </code>
/// </remarks>
public static class FileStatementToleranceProfileProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string RelativePath = "reconciliation/tolerance-profiles.json";

    public static IStatementToleranceProfileProvider Load(string dataRoot, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var path = Path.Combine(dataRoot, "reconciliation", "tolerance-profiles.json");
        if (!File.Exists(path))
        {
            return new InMemoryStatementToleranceProfileProvider();
        }

        try
        {
            var document = JsonSerializer.Deserialize<ToleranceProfileFile>(File.ReadAllText(path), JsonOptions);
            var operatorProfiles = (document?.Profiles ?? [])
                .Where(static profile => profile is not null && !string.IsNullOrWhiteSpace(profile.ProfileId))
                .Select(static profile => Map(profile!));
            // Always include the built-in default so the default profile id resolves even alongside
            // operator profiles; the provider keeps the highest version per id if an operator overrides it.
            return new InMemoryStatementToleranceProfileProvider(
                operatorProfiles.Append(StatementToleranceProfile.Default));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or FormatException)
        {
            // Fail safe to the default-only provider; an unknown profile id then fails closed at the
            // workflow rather than silently applying default thresholds under a different recorded id.
            logger?.LogWarning(
                ex,
                "Failed to load statement tolerance profiles from {RelativePath}; using the default profile only.",
                RelativePath);
            return new InMemoryStatementToleranceProfileProvider();
        }
    }

    private static StatementToleranceProfile Map(ToleranceProfileEntry profile) => new(
        profile.ProfileId,
        profile.Version <= 0 ? 1 : profile.Version,
        (profile.CashRules ?? [])
            .Where(static rule => rule is not null)
            .Select(static rule => new CashToleranceRule(
                rule!.RuleId ?? string.Empty,
                rule.AbsoluteCashTolerance,
                rule.BasisPointCashTolerance,
                TimeSpan.FromDays(rule.SettlementDateToleranceDays)))
            .ToArray(),
        (profile.PositionRules ?? [])
            .Where(static rule => rule is not null)
            .Select(static rule => new PositionToleranceRule(
                rule!.RuleId ?? string.Empty,
                rule.QuantityTolerance,
                rule.MarketValueTolerance,
                rule.PriceTolerance))
            .ToArray(),
        (profile.TransactionRules ?? [])
            .Where(static rule => rule is not null)
            .Select(static rule => new TransactionToleranceRule(
                rule!.RuleId ?? string.Empty,
                rule.AbsoluteCashTolerance,
                TimeSpan.FromDays(rule.SettlementDateToleranceDays),
                rule.PriceTolerance))
            .ToArray());

    private sealed record ToleranceProfileFile(IReadOnlyList<ToleranceProfileEntry?>? Profiles);

    private sealed record ToleranceProfileEntry(
        string ProfileId,
        int Version,
        IReadOnlyList<CashRuleEntry?>? CashRules,
        IReadOnlyList<PositionRuleEntry?>? PositionRules,
        IReadOnlyList<TransactionRuleEntry?>? TransactionRules);

    private sealed record CashRuleEntry(
        string? RuleId,
        decimal AbsoluteCashTolerance,
        decimal? BasisPointCashTolerance,
        double SettlementDateToleranceDays);

    private sealed record PositionRuleEntry(
        string? RuleId,
        decimal QuantityTolerance,
        decimal MarketValueTolerance,
        decimal PriceTolerance);

    private sealed record TransactionRuleEntry(
        string? RuleId,
        decimal AbsoluteCashTolerance,
        double SettlementDateToleranceDays,
        decimal PriceTolerance);
}
