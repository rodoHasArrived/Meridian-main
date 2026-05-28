namespace Meridian.Application.Reconciliation;

public sealed record StatementToleranceProfile(
    string ProfileId,
    int Version,
    IReadOnlyList<CashToleranceRule> CashRules,
    IReadOnlyList<PositionToleranceRule> PositionRules,
    IReadOnlyList<TransactionToleranceRule> TransactionRules)
{
    public const string DefaultProfileId = "statement-default";
    public const int DefaultProfileVersion = 1;

    public static StatementToleranceProfile Default { get; } = new(
        DefaultProfileId,
        DefaultProfileVersion,
        [new CashToleranceRule("cash-default-absolute-v1", 0.01m, null, TimeSpan.FromMinutes(5))],
        [new PositionToleranceRule("position-default-v1", 0.0001m, 0m, 0m)],
        [new TransactionToleranceRule("transaction-default-v1", 0.01m, TimeSpan.FromDays(0), 0m)]);
}

public sealed record CashToleranceRule(
    string RuleId,
    decimal AbsoluteCashTolerance,
    decimal? BasisPointCashTolerance,
    TimeSpan SettlementDateTolerance)
{
    public bool Allows(decimal expectedAmountBase, decimal actualAmountBase, TimeSpan settlementDateDelta, out decimal allowedTolerance)
    {
        var absolute = Math.Abs(AbsoluteCashTolerance);
        var bps = BasisPointCashTolerance is { } basisPoints
            ? Math.Abs(expectedAmountBase) * Math.Abs(basisPoints) / 10_000m
            : 0m;
        allowedTolerance = Math.Max(absolute, bps);
        return Math.Abs(actualAmountBase - expectedAmountBase) <= allowedTolerance
            && settlementDateDelta.Duration() <= SettlementDateTolerance.Duration();
    }
}

public sealed record PositionToleranceRule(
    string RuleId,
    decimal QuantityTolerance,
    decimal MarketValueTolerance,
    decimal PriceTolerance)
{
    public bool Allows(decimal expectedQuantity, decimal actualQuantity, decimal expectedMarketValue, decimal actualMarketValue, decimal expectedPrice, decimal actualPrice) =>
        Math.Abs(actualQuantity - expectedQuantity) <= Math.Abs(QuantityTolerance)
        && Math.Abs(actualMarketValue - expectedMarketValue) <= Math.Abs(MarketValueTolerance)
        && Math.Abs(actualPrice - expectedPrice) <= Math.Abs(PriceTolerance);
}

public sealed record TransactionToleranceRule(
    string RuleId,
    decimal AbsoluteCashTolerance,
    TimeSpan SettlementDateTolerance,
    decimal PriceTolerance)
{
    public bool Allows(decimal expectedAmount, decimal actualAmount, TimeSpan settlementDateDelta, decimal expectedPrice, decimal actualPrice) =>
        Math.Abs(actualAmount - expectedAmount) <= Math.Abs(AbsoluteCashTolerance)
        && settlementDateDelta.Duration() <= SettlementDateTolerance.Duration()
        && Math.Abs(actualPrice - expectedPrice) <= Math.Abs(PriceTolerance);
}

public interface IStatementToleranceProfileProvider
{
    ValueTask<StatementToleranceProfile> GetProfileAsync(string profileId, CancellationToken ct = default);
}

public sealed class InMemoryStatementToleranceProfileProvider : IStatementToleranceProfileProvider
{
    private readonly IReadOnlyDictionary<string, StatementToleranceProfile> _profiles;

    public InMemoryStatementToleranceProfileProvider(IEnumerable<StatementToleranceProfile>? profiles = null)
    {
        var registeredProfiles = profiles ?? new[] { StatementToleranceProfile.Default };
        _profiles = registeredProfiles
            .GroupBy(static profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(static profile => profile.Version).First(), StringComparer.OrdinalIgnoreCase);
    }

    public ValueTask<StatementToleranceProfile> GetProfileAsync(string profileId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ct.ThrowIfCancellationRequested();

        if (_profiles.TryGetValue(profileId, out var profile))
        {
            return ValueTask.FromResult(profile);
        }

        throw new KeyNotFoundException($"Statement tolerance profile '{profileId}' was not registered.");
    }
}
