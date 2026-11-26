using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Parquet;

namespace Meridian.Ui.Tests.Services;

public sealed class TimeSeriesAlignmentExporterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"alignment-export-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.JsonLines)]
    [InlineData(ExportFormat.Parquet)]
    public async Task SameResultWritesIdenticalBytesAndKeepsProvenance(ExportFormat format)
    {
        var result = Fixture();
        var firstPath = Path.Combine(_root, "first");
        var secondPath = Path.Combine(_root, "second");
        await TimeSeriesAlignmentExporter.WriteAsync(result, format, firstPath, CancellationToken.None);
        await TimeSeriesAlignmentExporter.WriteAsync(result, format, secondPath, CancellationToken.None);
        (await File.ReadAllBytesAsync(firstPath)).Should().Equal(await File.ReadAllBytesAsync(secondPath));

        if (format == ExportFormat.Parquet)
        {
            await using var stream = File.OpenRead(firstPath);
            using var reader = await ParquetReader.CreateAsync(stream);
            using var group = reader.OpenRowGroupReader(0);
            var idField = reader.Schema.DataFields.Single(field => field.Name == "source_ids_json");
            var idColumn = await group.ReadColumnAsync(idField);
            idColumn.Data.GetValue(0).Should().Be("[\"jsonl:archive:1:hash\"]");
            reader.CustomMetadata["meridian_alignment"].Should().Contain("contentHash");
        }
        else
        {
            var text = await File.ReadAllTextAsync(firstPath);
            text.Should().Contain("jsonl:archive:1:hash").And.Contain("fixture-content");
            if (format == ExportFormat.JsonLines)
            {
                using var row = JsonDocument.Parse(text.Split('\n')[1]);
                row.RootElement.GetProperty("close").GetDecimal().Should().Be(1.234567890123456789012345678m);
            }
        }
    }

    [Fact]
    public async Task CancelledExportLeavesPriorArtifactUntouched()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "existing.csv");
        await File.WriteAllTextAsync(path, "prior");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var write = () => TimeSeriesAlignmentExporter.WriteAsync(Fixture(), ExportFormat.Csv, path, cts.Token);
        await write.Should().ThrowAsync<OperationCanceledException>();
        (await File.ReadAllTextAsync(path)).Should().Be("prior");
    }

    private static TimeSeriesAlignmentExecution Fixture()
    {
        var row = new TimeSeriesAlignmentRow("ALPACA", "AAPL", new DateOnly(2024, 7, 3),
            new DateTimeOffset(2024, 7, 3, 13, 30, 0, TimeSpan.Zero), "Raw", "Observed", null, null, null,
            1.234567890123456789012345678m, 5m, ["jsonl:archive:1:hash"]);
        return new(new("fixture-content", "US_EQUITIES_REGULAR", "session date", "UTC", "DailyBars", "Daily", "Null", 5,
            "Retain gaps.", "AsStored", "CollapseEqual", ["Close", "Volume"], 1, 1, 1, 0, 0, 0, [], []),
            [row]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
