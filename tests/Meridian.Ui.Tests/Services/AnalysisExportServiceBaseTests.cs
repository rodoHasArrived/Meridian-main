using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Export;
using Meridian.Ui.Services.Services;
using Xunit;

namespace Meridian.Ui.Tests.Services;

public sealed class AnalysisExportServiceTests
{
    [Fact]
    public void Instance_ReturnsNonNullSingleton()
    {
        var instance = AnalysisExportService.Instance;
        instance.Should().NotBeNull();
    }

    [Fact]
    public void Instance_ReturnsSameInstanceOnMultipleCalls()
    {
        var a = AnalysisExportService.Instance;
        var b = AnalysisExportService.Instance;
        a.Should().BeSameAs(b);
    }

    [Fact]
    public async Task GetAggregationOptionsAsync_ReturnsAllOptions()
    {
        var result = await AnalysisExportService.Instance.GetAggregationOptionsAsync();

        result.Should().NotBeEmpty();
        result.Should().Contain(a => a.Value == "Tick");
        result.Should().Contain(a => a.Value == "Minute");
        result.Should().Contain(a => a.Value == "Daily");
        result.Should().Contain(a => a.Value == "Monthly");
    }

    [Fact]
    public async Task GetExportTemplatesAsync_ReturnsTemplates()
    {
        var result = await AnalysisExportService.Instance.GetExportTemplatesAsync();

        result.Should().NotBeEmpty();
        result.Should().Contain(t => t.Name == "Academic Research");
        result.Should().Contain(t => t.Name == "Machine Learning");
        result.Should().Contain(t => t.Name == "Backtesting");
        result.Should().Contain(t => t.Name == "Order Flow Analysis");
        result.Should().Contain(t => t.Name == "Market Microstructure");
    }

    [Fact]
    public async Task ExportAsync_WithCancellation_ThrowsOnCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await AnalysisExportService.Instance.ExportAsync(
            new AnalysisExportOptions { Symbols = new List<string> { "SPY" } }, cts.Token);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task GenerateQualityReportAsync_WithCancellation_ThrowsOnCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await AnalysisExportService.Instance.GenerateQualityReportAsync(
            new QualityReportOptions(), cts.Token);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ExportOrderFlowAsync_WithCancellation_ThrowsOnCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await AnalysisExportService.Instance.ExportOrderFlowAsync(
            new OrderFlowExportOptions { Symbols = new List<string> { "SPY" } }, cts.Token);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task ExportIntegrityEventsAsync_WithCancellation_ThrowsOnCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await AnalysisExportService.Instance.ExportIntegrityEventsAsync(
            new IntegrityExportOptions { Symbols = new List<string> { "AAPL" }, Format = "CSV" }, cts.Token);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task CreateResearchPackageAsync_WithCancellation_ThrowsOnCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await AnalysisExportService.Instance.CreateResearchPackageAsync(
            new ResearchPackageOptions { Name = "Test", Symbols = new List<string> { "SPY" } }, cts.Token);

        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void ProgressChanged_EventCanBeSubscribed()
    {
        ExportProgressEventArgs? received = null;
        AnalysisExportService.Instance.ProgressChanged += (_, e) => received = e;

        // Verify event wiring works (no throw on subscribe/unsubscribe)
        AnalysisExportService.Instance.ProgressChanged -= (_, _) => { };
        received.Should().BeNull();
    }

    [Fact]
    public void AnalysisExportOptions_DefaultFormat_IsParquet()
    {
        var options = new AnalysisExportOptions();
        options.Format.Should().Be(AnalysisExportFormat.Parquet);
        options.IncludeMetadata.Should().BeTrue();
    }

    [Fact]
    public void AnalysisExportResult_CanBeConstructed()
    {
        var result = new AnalysisExportResult
        {
            Success = true,
            OutputPath = "/output/data.parquet",
            RowsExported = 5000,
            BytesWritten = 102400,
            Duration = TimeSpan.FromSeconds(2.5)
        };

        result.Success.Should().BeTrue();
        result.RowsExported.Should().Be(5000);
        result.Duration.Should().Be(TimeSpan.FromSeconds(2.5));
    }

    [Fact]
    public void ExportTemplate_HasCorrectProperties()
    {
        var template = new ExportTemplate
        {
            Name = "Test",
            Format = AnalysisExportFormat.CSV,
            Aggregation = DataAggregation.Daily,
            IncludeMetadata = true
        };

        template.Name.Should().Be("Test");
        template.Format.Should().Be(AnalysisExportFormat.CSV);
        template.Aggregation.Should().Be(DataAggregation.Daily);
    }
}
