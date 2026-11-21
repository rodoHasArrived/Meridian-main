using Meridian.Execution;
using Meridian.Execution.Sdk;
using Meridian.Risk;
using Meridian.Storage;
using Meridian.Storage.Export;
using Xunit;

namespace Meridian.Tests.Architecture;

public sealed class ReviewBoundaryTests
{
    [Fact]
    public void StoragePrimitives_DoNotReferenceAccountingOrReportingOwners()
    {
        var references = typeof(StorageOptions).Assembly.GetReferencedAssemblies().Select(item => item.Name);
        Assert.DoesNotContain("Meridian.Ledger", references);
        Assert.DoesNotContain("Meridian.Reporting", references);
    }

    [Fact]
    public void AccountingExports_CompileInAdapterAssemblyRatherThanStoragePrimitives()
    {
        Assert.Equal("Meridian.Storage.Accounting", typeof(LedgerExportRequest).Assembly.GetName().Name);
        Assert.Equal(typeof(LedgerExportRequest).Assembly, typeof(AnalysisExportServiceLedgerExtensions).Assembly);
        Assert.NotEqual(typeof(AnalysisExportService).Assembly, typeof(LedgerExportRequest).Assembly);
    }

    [Fact]
    public void Risk_DependsOnExecutionContractsRatherThanConcreteRuntime()
    {
        var references = typeof(CompositeRiskValidator).Assembly.GetReferencedAssemblies().Select(item => item.Name);
        Assert.Contains("Meridian.Execution.Sdk", references);
        Assert.DoesNotContain("Meridian.Execution", references);
    }

}
