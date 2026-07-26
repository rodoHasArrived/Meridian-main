using System;
using System.Collections.Generic;
using System.IO;
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
    public async Task GetAggregationOptionsAsync_DoesNotAdvertiseOptionsMissingFromSharedContract()
    {
        var result = await AnalysisExportService.Instance.GetAggregationOptionsAsync();

        result.Should().BeEmpty(
            "ExportAnalysisApiRequest cannot carry aggregation without silently dropping it");
    }

    [Fact]
    public async Task GetExportTemplatesAsync_DoesNotAdvertiseUnexecutablePresets()
    {
        var result = await AnalysisExportService.Instance.GetExportTemplatesAsync();

        result.Should().BeEmpty(
            "historical presets require aggregation and field-selection options absent from the shared request");
    }

    [Fact]
    public void GetCanonicalFormats_ShouldAdvertiseOnlyExecutableProfiles()
    {
        var formats = AnalysisExportService.GetCanonicalFormats();

        formats.Select(format => format.Extension)
            .Should().Equal(".csv", ".parquet", ".xlsx", ".arrow");
        formats.Should().OnlyContain(format => !format.SupportsCompression,
            "the canonical request cannot carry a caller-selected compression option");
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

    [Theory]
    [InlineData(AnalysisExportFormat.CSV, "r-stats", "csv")]
    [InlineData(AnalysisExportFormat.Parquet, "python-pandas", "parquet")]
    [InlineData(AnalysisExportFormat.Excel, "excel", "xlsx")]
    [InlineData(AnalysisExportFormat.Feather, "arrow-feather", "arrow")]
    public void CreateCanonicalExportRequest_ShouldMatchFormatToRegisteredProfile(
        AnalysisExportFormat format,
        string expectedProfileId,
        string expectedFormat)
    {
        var options = new AnalysisExportOptions
        {
            Symbols = new List<string> { "SPY", "QQQ" },
            FromDate = new DateOnly(2026, 1, 2),
            ToDate = new DateOnly(2026, 1, 5),
            Format = format
        };

        var request = AnalysisExportService.CreateCanonicalExportRequest(options);

        request.ProfileId.Should().Be(expectedProfileId);
        request.Format.Should().Be(expectedFormat);
        request.Symbols.Should().Equal("SPY", "QQQ");
        request.StartDate.Should().Be(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        request.EndDate.Should().Be(new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(AnalysisExportFormat.JSON)]
    [InlineData(AnalysisExportFormat.JSONL)]
    [InlineData(AnalysisExportFormat.HDF5)]
    public void CreateCanonicalExportRequest_WithoutRegisteredProfile_ShouldFailClosed(
        AnalysisExportFormat format)
    {
        var options = new AnalysisExportOptions { Format = format };

        var act = () => AnalysisExportService.CreateCanonicalExportRequest(options);

        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*'{format}'*not supported*");
    }

    [Fact]
    public void CreateCanonicalExportRequest_WithOptionsMissingFromApiContract_ShouldFailClosed()
    {
        var options = new AnalysisExportOptions
        {
            Format = AnalysisExportFormat.CSV,
            Aggregation = DataAggregation.Daily,
            IncludeFields = ["price"],
            OutputPath = "requested-output",
            Compression = CompressionType.Gzip,
            IncludeMetadata = false,
            SplitBySymbol = true,
            Timezone = "America/Phoenix"
        };

        var act = () => AnalysisExportService.CreateCanonicalExportRequest(options);

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*cannot represent*Aggregation*IncludeFields*OutputPath*Compression*" +
                         "IncludeMetadata=false*SplitBySymbol=true*Timezone*No export was requested*");
    }

    [Fact]
    public void MapCanonicalExportResponse_ShouldPreserveCanonicalEvidence()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "analysis-export-result");
        var response = new ExportAnalysisApiResponse(
            JobId: "job-1",
            Success: true,
            Status: "completed",
            ProfileId: "excel",
            Symbols: new[] { "SPY" },
            FilesGenerated: 1,
            TotalRecords: 42,
            TotalBytes: 4096,
            OutputDirectory: outputDirectory,
            DurationSeconds: 1.25,
            Error: null,
            Warnings: new[] { "source warning" },
            Files: new[]
            {
                new ExportAnalysisApiFile("SPY_20260102.xlsx", "SPY", "xlsx", 4096, 42)
            },
            Timestamp: DateTimeOffset.UtcNow);

        var result = AnalysisExportService.MapCanonicalExportResponse(response);

        result.Success.Should().BeTrue();
        result.OutputPath.Should().Be(outputDirectory);
        result.FilesCreated.Should().ContainSingle()
            .Which.Should().Be(Path.Combine(outputDirectory, "SPY_20260102.xlsx"));
        result.RowsExported.Should().Be(42);
        result.BytesWritten.Should().Be(4096);
        result.Duration.Should().Be(TimeSpan.FromSeconds(1.25));
        result.Warnings.Should().Equal("source warning");
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
