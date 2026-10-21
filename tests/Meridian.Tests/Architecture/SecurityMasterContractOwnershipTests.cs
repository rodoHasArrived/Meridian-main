using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Tests.Architecture;

public sealed class SecurityMasterContractOwnershipTests
{
    [Fact]
    public void Application_DoesNotRedeclareCanonicalSecurityMasterContracts()
    {
        var canonicalContracts = new[]
        {
            typeof(ISecurityMasterService),
            typeof(ISecurityMasterQueryService),
            typeof(ISecurityMasterAmender)
        };
        var applicationContracts = typeof(SecurityMasterService).Assembly.GetExportedTypes()
            .Where(type => type.IsInterface)
            .Select(type => type.Name);

        applicationContracts.Should().NotIntersectWith(canonicalContracts.Select(type => type.Name),
            "all Security Master callers must resolve the same Contracts-owned service types");
    }

    [Theory]
    [InlineData(typeof(Meridian.Execution.SecurityMasterGate))]
    [InlineData(typeof(Meridian.Backtesting.CorporateActionAdjustmentService))]
    public void ExecutionAndSimulation_DoNotImportApplication(Type runtimeType)
    {
        runtimeType.Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name)
            .Should().NotContain("Meridian.Application",
                "execution and simulation must compose against shared contracts without the application implementation");
    }

    [Fact]
    public void AggregateQuery_ExposesCanonicalConsumerContracts()
    {
        typeof(ISecurityMasterQueryService).GetInterfaces().Should().Contain(
            [typeof(ISecurityIdentityQueryService), typeof(ISecurityTradingParametersQueryService), typeof(ISecurityCorporateActionsQueryService)]);

        var canonicalAssembly = typeof(ISecurityMasterQueryService).Assembly;
        Assert.Same(canonicalAssembly, typeof(ISecurityResolver).Assembly);
        Assert.Same(canonicalAssembly, typeof(ILivePositionCorporateActionAdjuster).Assembly);
        Assert.Same(canonicalAssembly, typeof(PositionCorporateActionAdjustment).Assembly);
    }
}
