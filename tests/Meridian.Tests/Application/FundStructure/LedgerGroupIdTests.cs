using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.Tests.Application.FundStructure;

public sealed class LedgerGroupIdTests
{
    [Fact]
    public void Create_TrimsWhitespace()
    {
        var groupId = LedgerGroupId.Create("  FUND-TB  ");

        Assert.Equal("FUND-TB", groupId.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fund group")]
    [InlineData("fund/group")]
    [InlineData("group*1")]
    public void Create_InvalidInput_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => LedgerGroupId.Create(input));
    }
}
