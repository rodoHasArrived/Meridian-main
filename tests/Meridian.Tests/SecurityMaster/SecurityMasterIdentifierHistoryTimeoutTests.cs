using FluentAssertions;
using Meridian.Storage.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class SecurityMasterIdentifierHistoryTimeoutTests
{
    [Theory]
    [InlineData(1_000_000, 30)]
    [InlineData(1_000_001, 31)]
    [InlineData(1_000_001, 120)]
    [InlineData(1_000_001, 0)]
    public void ApplyConditions_AcceptSmallBackfillOrRaisedTimeout(long rows, int timeout)
    {
        var act = () => SecurityMasterMigrationRunner.CheckIdentifierHistoryTimeout(rows, timeout);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(30)]
    public void ApplyConditions_LargeBackfillRefusesDefaultOrLowerTimeout(int timeout)
    {
        var act = () => SecurityMasterMigrationRunner.CheckIdentifierHistoryTimeout(1_000_001, timeout);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Raise Command Timeout above 30 seconds*");
    }
}
