using FluentAssertions;
using Meridian.Backtesting.Engine;
using Meridian.Contracts.SecurityMaster;
using Meridian.Execution.PaperMatching;
using Meridian.Storage.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Tests.Architecture;

public sealed class SecurityMasterConstructorCompatibilityTests
{
#pragma warning disable CS0618 // These assertions bind the published compatibility type identities.
    [Theory]
    [InlineData(typeof(Meridian.Application.SecurityMaster.ISecurityResolver))]
    [InlineData(typeof(Meridian.Application.SecurityMaster.CorporateActions.ILivePositionCorporateActionAdjuster))]
    [InlineData(typeof(Meridian.Application.SecurityMaster.CorporateActions.PositionCorporateActionAdjustment))]
    public void FormerApplicationContracts_ResolveThroughTheirOriginalAssemblyQualifiedNames(Type legacyType)
    {
        Type.GetType($"{legacyType.FullName}, Meridian.Application", throwOnError: true).Should().BeSameAs(legacyType);
        legacyType.Assembly.Should().Be(typeof(ISecurityMasterQueryService).Assembly);
    }

    [Fact]
    public void ResolverAndAdjusterConsumers_RetainTheirPublishedConstructorAndReturnSignatures()
    {
        var legacyResolver = typeof(Meridian.Application.SecurityMaster.ISecurityResolver);
        var legacyAdjuster = typeof(Meridian.Application.SecurityMaster.CorporateActions.ILivePositionCorporateActionAdjuster);
        typeof(Meridian.Execution.SecurityMasterGate).GetConstructor(
            [legacyResolver, typeof(ILogger<Meridian.Execution.SecurityMasterGate>)]).Should().NotBeNull();
        typeof(Meridian.Backtesting.CorporateActionAdjustmentService).GetConstructor(
            [typeof(ISecurityMasterQueryService), legacyResolver,
                typeof(ILogger<Meridian.Backtesting.CorporateActionAdjustmentService>),
                typeof(Meridian.Instruments.AssetOperations.IFactorPaydownProjectionService)]).Should().NotBeNull();
        foreach (var accountInput in new[] { typeof(decimal), typeof(IReadOnlyList<Meridian.Execution.Services.AccountDefinition>) })
        {
            var constructor = typeof(Meridian.Execution.Services.PaperTradingPortfolio).GetConstructor(
                [accountInput, typeof(Meridian.Ledger.Ledger), legacyAdjuster, typeof(Meridian.Contracts.Api.PositionLotSelectionMethod)]);
            constructor.Should().NotBeNull();
            constructor!.GetParameters().Skip(1).Should().OnlyContain(parameter => parameter.IsOptional);
        }
        var adjustment = typeof(Meridian.Backtesting.CorporateActionAdjustmentService).GetMethod("AdjustPositionAsync",
            [typeof(string), typeof(decimal), typeof(decimal), typeof(DateTimeOffset), typeof(CancellationToken)]);
        adjustment!.ReturnType.Should().Be(typeof(Task<Meridian.Application.SecurityMaster.CorporateActions.PositionCorporateActionAdjustment>));
        legacyAdjuster.IsAssignableFrom(typeof(Meridian.Backtesting.CorporateActionAdjustmentService)).Should().BeTrue();
    }
#pragma warning restore CS0618

    [Theory]
    [InlineData(typeof(BacktestEngine))]
    [InlineData(typeof(Meridian.Execution.PaperTradingGateway))]
    [InlineData(typeof(Meridian.Execution.Adapters.PaperTradingGateway))]
    public void AggregateQueryConstructor_RetainsPublishedClrSignatureAndDefaults(Type runtimeType)
    {
        var logger = typeof(ILogger<>).MakeGenericType(runtimeType);
        var parameterTypes = runtimeType == typeof(BacktestEngine)
            ? new[] { logger, typeof(StorageCatalogService), typeof(ISecurityMasterQueryService),
                typeof(Meridian.Backtesting.ICorporateActionAdjustmentService), typeof(Meridian.Backtesting.IBacktestPreflightService) }
            : [logger, typeof(ISecurityMasterQueryService), typeof(Meridian.Execution.Adapters.PaperTradingGatewayOptions),
                typeof(Meridian.Execution.Interfaces.ILiveFeedAdapter), typeof(PaperTradingCostOptions)];

        var constructor = runtimeType.GetConstructor(parameterTypes);

        constructor.Should().NotBeNull("previously compiled consumers bind this exact constructor signature");
        var requiredCount = runtimeType == typeof(BacktestEngine) ? 2 : 1;
        foreach (var parameter in constructor!.GetParameters().Skip(requiredCount))
        {
            parameter.IsOptional.Should().BeTrue();
            parameter.DefaultValue.Should().BeNull();
        }
    }
}
