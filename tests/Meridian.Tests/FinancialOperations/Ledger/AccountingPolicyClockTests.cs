using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations.Ledger;

/// <summary>
/// Boundary tests for the undated policy query, which reads the clock to decide "as of when".
/// </summary>
/// <remarks>
/// These are the tests the clock injection buys (#2615). Before it, a test for "an undated query
/// resolves as of today" could only compute its expectation from <c>DateTime.UtcNow</c> and compare
/// that against production code doing the same read — which passes whether or not the logic is
/// right, and fails intermittently when the two reads straddle midnight. Pinning the clock makes the
/// behaviour assertable instead of accidental.
/// </remarks>
public sealed class AccountingPolicyClockTests
{
    /// <summary>A clock frozen at one instant, so a date boundary can be landed on exactly.</summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static AccountingPolicyService At(int year, int month, int day, int hour = 12, int minute = 0, int second = 0)
        => new(new FixedTimeProvider(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero)));

    [Fact]
    public async Task UndatedQuery_ClockBeforeAnyPolicyIsEffective_FindsNothing()
    {
        // The seeded policies begin on 1900-01-01, so a clock before that must resolve nothing.
        // This is the assertion that makes the whole suite falsifiable: if the service still read
        // the system clock, it would resolve a policy here instead of throwing.
        var service = At(1899, 12, 31);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResolvePolicyAsync(new AccountingPolicyQuery()));
    }

    [Fact]
    public async Task UndatedQuery_ClockOnTheFirstEffectiveDay_Resolves()
    {
        // The other side of the same boundary: 1900-01-01 is inclusive (EffectiveFrom <= date).
        var service = At(1900, 01, 01, hour: 0, minute: 0, second: 0);

        var resolved = await service.ResolvePolicyAsync(new AccountingPolicyQuery());

        Assert.NotNull(resolved);
    }

    [Fact]
    public async Task UndatedQuery_LastInstantBeforeTheFirstEffectiveDay_StillFindsNothing()
    {
        // 23:59:59.999 on the day before — the case that silently flips when a test's own clock read
        // lands a millisecond later than the production one. Only a fixed clock can pin it.
        var service = new AccountingPolicyService(
            new FixedTimeProvider(new DateTimeOffset(1899, 12, 31, 23, 59, 59, 999, TimeSpan.Zero)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ResolvePolicyAsync(new AccountingPolicyQuery()));
    }

    [Fact]
    public async Task ExplicitEffectiveDate_IsUnaffectedByTheClock()
    {
        // The injection must not change the dated path: a caller supplying EffectiveDate should get
        // the same policy no matter what the clock says, including a clock that would itself resolve
        // nothing.
        var effectiveDate = new DateOnly(2029, 03, 15);
        var query = new AccountingPolicyQuery(EffectiveDate: effectiveDate);

        var fromAncientClock = await At(1899, 12, 31).ResolvePolicyAsync(query);
        var fromFutureClock = await At(2035, 12, 31).ResolvePolicyAsync(query);

        Assert.Equal(fromAncientClock.PolicyId, fromFutureClock.PolicyId);
        Assert.Equal(fromAncientClock.Version, fromFutureClock.Version);
    }

    [Fact]
    public async Task DefaultConstructor_StillUsesTheSystemClock()
    {
        // The parameter is optional so the twenty-odd existing call sites keep compiling; this pins
        // that the default is TimeProvider.System rather than something inert.
        var service = new AccountingPolicyService();

        var resolved = await service.ResolvePolicyAsync(new AccountingPolicyQuery());

        Assert.NotNull(resolved);
    }
}
