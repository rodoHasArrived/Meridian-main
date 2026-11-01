using FluentAssertions;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.SecurityMaster;

public sealed class InstrumentTypeDescriptorMultiplierTests
{
    [Theory]
    [InlineData("Option")]
    [InlineData("Future")]
    [InlineData(" option ")]
    public void RequiresContractMultiplier_IsTrue_WhereDescriptorsDeclareAMultiplier(string assetClass)
        => InstrumentTypeDescriptorCatalog.RequiresContractMultiplier(assetClass).Should().BeTrue();

    [Theory]
    [InlineData("Equity")]
    [InlineData("Swap")]
    [InlineData("Derivative")]
    [InlineData("")]
    [InlineData(null)]
    public void RequiresContractMultiplier_IsFalse_ForClassesWithoutOne(string? assetClass)
        => InstrumentTypeDescriptorCatalog.RequiresContractMultiplier(assetClass).Should().BeFalse();
}
