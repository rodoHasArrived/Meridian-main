using FluentAssertions;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Architecture;

public sealed class ReportingReadModelBoundaryTests
{
    [Theory]
    [InlineData(typeof(ReportingIncomeComparisonService))]
    [InlineData(typeof(ReportAccessQueryContext))]
    [InlineData(typeof(ReportAccessPolicyEvaluator))]
    [InlineData(typeof(LedgerDisposalTaxReadService))]
    [InlineData(typeof(LedgerReportingPartnersCapitalSource))]
    [InlineData(typeof(ReportingLedgerPopulationSnapshot))]
    [InlineData(typeof(ReportAmountBindingBuilder))]
    [InlineData(typeof(ReportPeriodRange))]
    public void LeafReader_IsOwnedByIndependentReportingAssemblyAndRetainsLegacyIdentity(Type reader)
    {
        reader.Assembly.GetName().Name.Should().Be("Meridian.Reporting.ReadModels");
        Type.GetType($"{reader.FullName}, Meridian.Ui.Shared", throwOnError: true).Should().Be(reader);
    }

    [Fact]
    public void ReaderAssembly_DoesNotReferenceWorkstationHostOrProviderImplementations()
    {
        var dependencies = typeof(ReportingIncomeComparisonService).Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name).ToArray();

        dependencies.Should().NotContain("Meridian.Ui.Shared");
        dependencies.Should().NotContain("Meridian.Ui.Services");
        dependencies.Should().NotContain("Meridian.Application");
        dependencies.Should().NotContain("Meridian.Infrastructure");
        dependencies.Should().NotContain("Meridian.Wpf");
    }
}
