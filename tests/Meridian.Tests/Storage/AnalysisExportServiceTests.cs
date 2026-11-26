using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using FluentAssertions;
using Meridian.Storage.Export;
using Parquet;
using Xunit;

namespace Meridian.Tests.Storage;

public class AnalysisExportServiceTests : IDisposable
{
    private readonly string _testDataRoot;
    private readonly string _testOutputDir;
    private readonly AnalysisExportService _service;

    public AnalysisExportServiceTests()
    {
        // Create temporary directories for test data
        _testDataRoot = Path.Combine(Path.GetTempPath(), $"mdc_test_{Guid.NewGuid():N}");
        _testOutputDir = Path.Combine(Path.GetTempPath(), $"mdc_export_{Guid.NewGuid():N}");

        Directory.CreateDirectory(_testDataRoot);
        Directory.CreateDirectory(_testOutputDir);

        _service = new AnalysisExportService(_testDataRoot);
    }

    public void Dispose()
    {
        // Cleanup test directories
        if (Directory.Exists(_testDataRoot))
            Directory.Delete(_testDataRoot, recursive: true);
        if (Directory.Exists(_testOutputDir))
            Directory.Delete(_testOutputDir, recursive: true);
    }

    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Parquet)]
    [InlineData(ExportFormat.Arrow)]
    [InlineData(ExportFormat.Xlsx)]
    public async Task ExportAsync_HistoricalBars_PreservesEveryBasisAfterLegacyRows(ExportFormat format)
    {
        var records = new[]
        {
            PriceBasisRecord(),
            PriceBasisRecord(adjusted: false),
            PriceBasisRecord("Raw", false),
            PriceBasisRecord("SplitAdjusted", true),
            PriceBasisRecord("DividendAdjusted", true),
            PriceBasisRecord("FullyAdjusted", true)
        };
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", records);

        var result = await _service.ExportAsync(PriceBasisExportRequest(format));

        result.Success.Should().BeTrue(result.Error);
        result.TotalRecords.Should().Be(6);
        var bases = await ReadPriceBasisColumnAsync(result.Files.Single().Path, format);
        bases.Should().Equal("Unknown", "Unknown", "Raw", "SplitAdjusted", "DividendAdjusted", "FullyAdjusted");
    }

    [Fact]
    public async Task ExportAsync_CustomCsvPriceSelection_CannotRemoveBasisCompanions()
    {
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", [PriceBasisRecord("SplitAdjusted", true)]);
        var request = PriceBasisExportRequest(ExportFormat.Csv);
        request.CustomProfile!.IncludeFields = ["Close"];
        request.CustomProfile.ExcludeFields = ["pricebasis", "ISADJUSTED"];

        var result = await _service.ExportAsync(request);

        result.Success.Should().BeTrue(result.Error);
        var lines = await File.ReadAllLinesAsync(result.Files.Single().Path);
        lines.Should().Equal("Close,PriceBasis,IsAdjusted", "50,SplitAdjusted,True");
    }

    [Theory]
    [InlineData("FullyAdjusted", "FullyAdjusted")]
    [InlineData(null, "Unknown")]
    public async Task ExportAsync_CustomAdjustedCloseSelection_RetainsItsIndependentBasis(string? basis, string expected)
    {
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", [AdjustedPriceBasisRecord(basis)]);
        var request = PriceBasisExportRequest(ExportFormat.Csv);
        request.CustomProfile!.IncludeFields = ["AdjustedClose"];
        request.CustomProfile.ExcludeFields = ["PriceBasis", "IsAdjusted", "AdjustedPriceBasis"];

        var result = await _service.ExportAsync(request);

        result.Success.Should().BeTrue(result.Error);
        var lines = await File.ReadAllLinesAsync(result.Files.Single().Path);
        lines.Should().Equal("AdjustedClose,AdjustedPriceBasis", $"25,{expected}");
    }

    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Parquet)]
    [InlineData(ExportFormat.Arrow)]
    [InlineData(ExportFormat.Xlsx)]
    public async Task ExportAsync_DifferentPrimaryAndOptionalBases_RemainIndependent(ExportFormat format)
    {
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", new[]
        {
            AdjustedPriceBasisRecord(),
            AdjustedPriceBasisRecord("FullyAdjusted"),
            AdjustedPriceBasisRecord("DividendAdjusted")
        });

        var result = await _service.ExportAsync(PriceBasisExportRequest(format));

        result.Success.Should().BeTrue(result.Error);
        var file = result.Files.Single().Path;
        (await ReadPriceBasisColumnAsync(file, format)).Should().Equal("Raw", "Raw", "Raw");
        (await ReadPriceBasisColumnAsync(file, format, "AdjustedPriceBasis"))
            .Should().Equal("Unknown", "FullyAdjusted", "DividendAdjusted");
    }

    [Theory]
    [InlineData("Unknown, Raw", false)]
    [InlineData("1", false)]
    [InlineData("Unknown, SplitAdjusted", true)]
    [InlineData("2", true)]
    public async Task ExportAsync_MalformedBasisName_RefusesCompoundOrNumericStrings(string basis, bool optionalSet)
    {
        var record = optionalSet ? AdjustedPriceBasisRecord(basis) : PriceBasisRecord(basis);
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", [record]);

        var result = await _service.ExportAsync(PriceBasisExportRequest(ExportFormat.Csv));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unsupported");
        result.Files.Should().BeEmpty();
        Directory.EnumerateFiles(_testOutputDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Theory]
    [InlineData("PriceBasis", "SplitAdjusted")]
    [InlineData("AdjustedPriceBasis", "Raw")]
    [InlineData("IsAdjusted", true)]
    public async Task ExportAsync_DuplicateCaseVariantMetadata_FailsInsteadOfChoosingOneDeclaration(string duplicateKey, object value)
    {
        var record = AdjustedPriceBasisRecord("FullyAdjusted");
        record[duplicateKey] = value;
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", [record]);

        var result = await _service.ExportAsync(PriceBasisExportRequest(ExportFormat.Csv));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("duplicate");
        result.Files.Should().BeEmpty();
        Directory.EnumerateFiles(_testOutputDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Parquet)]
    [InlineData(ExportFormat.Arrow)]
    [InlineData(ExportFormat.Xlsx)]
    public async Task ExportAsync_BufferedSchemaMissingLaterHistoricalPrices_FailsInsteadOfDroppingPrices(ExportFormat format)
    {
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", new[]
        {
            new Dictionary<string, object?> { ["Symbol"] = "SPY", ["Price"] = 50m, ["PriceBasis"] = "Raw" },
            PriceBasisRecord("Raw", false)
        });
        var request = PriceBasisExportRequest(format);
        if (format == ExportFormat.Csv)
            request.Features = new FeatureSettings { IncludeReturns = true, ReturnHorizons = [1], UseLogReturns = false };

        var result = await _service.ExportAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Historical price columns cannot be omitted");
        result.Files.Should().BeEmpty();
        Directory.EnumerateFiles(_testOutputDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Raw", "SplitAdjusted")]
    [InlineData("Raw", "Unknown")]
    [InlineData(null, "Raw")]
    public async Task ExportAsync_HistoricalFeatures_RejectsMixedOrUnknownBases(string? firstBasis, string secondBasis)
    {
        var first = PriceBasisRecord(firstBasis);
        first["close"] = 100m;
        first["high"] = 101m;
        var second = PriceBasisRecord(secondBasis);
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", [first, second]);
        var request = PriceBasisExportRequest(ExportFormat.Csv);
        request.Features = new FeatureSettings { IncludeReturns = true, ReturnHorizons = [1], UseLogReturns = false };

        var result = await _service.ExportAsync(request);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("one explicit known price basis");
        result.Files.Should().BeEmpty();
        Directory.EnumerateFiles(_testOutputDir, "*.csv").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Raw")]
    [InlineData("SplitAdjusted")]
    public async Task ExportAsync_HistoricalFeatures_KeepsReturnsWithinOneKnownBasis(string basis)
    {
        var first = PriceBasisRecord(basis);
        var second = PriceBasisRecord(basis);
        second["high"] = 56m;
        second["close"] = 55m;
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", [first, second]);
        var request = PriceBasisExportRequest(ExportFormat.Csv);
        request.Features = new FeatureSettings { IncludeReturns = true, ReturnHorizons = [1], UseLogReturns = false };

        var result = await _service.ExportAsync(request);

        result.Success.Should().BeTrue(result.Error);
        var lines = await File.ReadAllLinesAsync(result.Files.Single().Path);
        var returnColumn = System.Array.IndexOf(lines[0].Split(','), "return_1");
        returnColumn.Should().BeGreaterThanOrEqualTo(0);
        lines[2].Split(',')[returnColumn].Should().Be("0.1");
        (await ReadPriceBasisColumnAsync(result.Files.Single().Path, ExportFormat.Csv)).Should().Equal(basis, basis);
    }

    [Fact]
    public async Task ExportAsync_RunMatHistoricalBars_AppendsBasisWithoutMovingNumericColumns()
    {
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", [PriceBasisRecord("DividendAdjusted", true)]);
        var request = PriceBasisExportRequest(ExportFormat.Csv);
        request.CustomProfile = ExportProfile.RunMat;
        request.CustomProfile.IncludeLoaderScript = false;
        request.CustomProfile.IncludeDataDictionary = false;

        var result = await _service.ExportAsync(request);

        result.Success.Should().BeTrue(result.Error);
        var lines = await File.ReadAllLinesAsync(result.Files.Single().Path);
        lines[0].Should().Be("Timestamp,Price,Size,BidPrice,BidSize,AskPrice,AskSize,Open,High,Low,Close,Volume,PriceBasis,IsAdjusted");
        var values = lines[1].Split(',');
        values[10].Should().Be("50");
        values[12].Should().Be("DividendAdjusted");
        values[13].Should().Be("True");
    }

    [Fact]
    public async Task ExportAsync_CsvHistoricalPayload_RetainsBasisAlongsidePayloadProjection()
    {
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl",
            [new { Symbol = "SPY", Type = "HistoricalBar", Payload = PriceBasisRecord("FullyAdjusted", true) }]);
        var request = PriceBasisExportRequest(ExportFormat.Csv);
        request.CustomProfile!.IncludeFields = ["Payload"];
        request.CustomProfile.ExcludeFields = ["PriceBasis", "IsAdjusted"];

        var result = await _service.ExportAsync(request);

        result.Success.Should().BeTrue(result.Error);
        var lines = await File.ReadAllLinesAsync(result.Files.Single().Path);
        lines[0].Should().Be("Payload,PriceBasis,IsAdjusted");
        lines[1].Should().EndWith(",FullyAdjusted,True");
    }

    [Fact]
    public async Task ExportAsync_CsvAdjustedHistoricalPayload_RetainsBothIndependentBases()
    {
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl",
            [new { Symbol = "SPY", Type = "HistoricalBar", Payload = AdjustedPriceBasisRecord("FullyAdjusted") }]);
        var request = PriceBasisExportRequest(ExportFormat.Csv);
        request.CustomProfile!.IncludeFields = ["Payload"];
        request.CustomProfile.ExcludeFields = ["PriceBasis", "IsAdjusted", "AdjustedPriceBasis"];

        var result = await _service.ExportAsync(request);

        result.Success.Should().BeTrue(result.Error);
        var lines = await File.ReadAllLinesAsync(result.Files.Single().Path);
        lines[0].Should().Be("Payload,PriceBasis,IsAdjusted,AdjustedPriceBasis");
        lines[1].Should().EndWith(",Raw,False,FullyAdjusted");
    }

    [Theory]
    [InlineData(ExportFormat.Csv)]
    [InlineData(ExportFormat.Parquet)]
    [InlineData(ExportFormat.Arrow)]
    [InlineData(ExportFormat.Xlsx)]
    public async Task ExportAsync_ContradictoryHistoricalBasis_FailsWithoutArtifact(ExportFormat format)
    {
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", [PriceBasisRecord("Raw", true)]);

        var result = await _service.ExportAsync(PriceBasisExportRequest(format));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("contradicts");
        result.Files.Should().BeEmpty();
        Directory.EnumerateFiles(_testOutputDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_StreamingCsvWithLaterHistoricalSchema_FailsInsteadOfDroppingBasis()
    {
        await CreateTestJsonlFileAsync("SPY.HistoricalBar.jsonl", new[]
        {
            new Dictionary<string, object?> { ["Symbol"] = "SPY", ["Price"] = 50m },
            PriceBasisRecord("SplitAdjusted", true)
        });

        var result = await _service.ExportAsync(PriceBasisExportRequest(ExportFormat.Csv));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("require PriceBasis and IsAdjusted");
        Directory.EnumerateFiles(_testOutputDir, "*.csv").Should().BeEmpty();
    }

    private ExportRequest PriceBasisExportRequest(ExportFormat format) => new()
    {
        CustomProfile = CreateFormatProfile(format, "historical-price-basis"),
        OutputDirectory = _testOutputDir,
        EventTypes = ["HistoricalBar"],
        Symbols = ["SPY"],
        StartDate = new DateTime(2026, 1, 1),
        EndDate = new DateTime(2026, 1, 5),
        ValidateBeforeExport = false,
        IncludeManifest = false
    };

    private static Dictionary<string, object?> PriceBasisRecord(string? basis = null, bool? adjusted = null)
    {
        var record = new Dictionary<string, object?>
        {
            ["symbol"] = "SPY",
            ["sessionDate"] = "2026-01-03",
            ["open"] = 50m,
            ["high"] = 51m,
            ["low"] = 49m,
            ["close"] = 50m,
            ["volume"] = 1_000
        };
        if (basis is not null)
            record["priceBasis"] = basis;
        if (adjusted.HasValue)
            record["isAdjusted"] = adjusted;
        return record;
    }

    private static Dictionary<string, object?> AdjustedPriceBasisRecord(string? basis = null)
    {
        var record = PriceBasisRecord("Raw", false);
        record["adjustedOpen"] = 25m;
        record["adjustedHigh"] = 25.5m;
        record["adjustedLow"] = 24.5m;
        record["adjustedClose"] = 25m;
        if (basis is not null)
            record["adjustedPriceBasis"] = basis;
        return record;
    }

    private static async Task<string[]> ReadPriceBasisColumnAsync(string path, ExportFormat format, string columnName = "PriceBasis")
    {
        if (format == ExportFormat.Csv)
        {
            var lines = await File.ReadAllLinesAsync(path);
            var column = System.Array.FindIndex(lines[0].Split(','), field => field.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            column.Should().BeGreaterThanOrEqualTo(0);
            return lines.Skip(1).Select(line => line.Split(',')[column]).ToArray();
        }

        if (format == ExportFormat.Parquet)
        {
            await using var stream = File.OpenRead(path);
            using var reader = await ParquetReader.CreateAsync(stream);
            var field = reader.Schema.GetDataFields().Single(item => item.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            using var group = reader.OpenRowGroupReader(0);
            var column = await group.ReadColumnAsync(field);
            return (string[])column.Data;
        }

        if (format == ExportFormat.Arrow)
        {
            await using var stream = File.OpenRead(path);
            using var reader = new ArrowFileReader(stream);
            using var batch = await reader.ReadRecordBatchAsync(0);
            var field = batch.Schema.FieldsList.Single(item => item.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
            var column = (StringArray)batch.Column(field.Name);
            return Enumerable.Range(0, batch.Length).Select(index => column.GetString(index)!).ToArray();
        }

        using var archive = ZipFile.OpenRead(path);
        using var stringsStream = archive.GetEntry("xl/sharedStrings.xml")!.Open();
        using var sheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var stringsDocument = XDocument.Load(stringsStream);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var strings = stringsDocument.Descendants(ns + "si").Select(item => item.Element(ns + "t")!.Value).ToArray();
        var rows = XDocument.Load(sheetStream).Descendants(ns + "row").ToArray();
        var header = rows[0].Elements(ns + "c").Single(cell =>
            strings[int.Parse(cell.Element(ns + "v")!.Value)].Equals(columnName, StringComparison.OrdinalIgnoreCase));
        var letter = new string(header.Attribute("r")!.Value.TakeWhile(char.IsLetter).ToArray());
        return rows.Skip(1).Select(row =>
        {
            var cell = row.Elements(ns + "c").Single(item => item.Attribute("r")!.Value == letter + row.Attribute("r")!.Value);
            return strings[int.Parse(cell.Element(ns + "v")!.Value)];
        }).ToArray();
    }

    [Fact]
    public async Task ExportToXlsx_WithValidData_ShouldCreateValidXlsxFile()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 },
            new { Timestamp = "2026-01-03T10:00:01Z", Symbol = "AAPL", Price = 185.55m, Size = 200 },
            new { Timestamp = "2026-01-03T10:00:02Z", Symbol = "AAPL", Price = 185.45m, Size = 150 }
        });

        var request = new ExportRequest
        {
            ProfileId = "excel",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "AAPL" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        // Act
        var result = await _service.ExportAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.FilesGenerated.Should().Be(1);
        result.TotalRecords.Should().Be(3);

        var xlsxFile = result.Files.Single();
        xlsxFile.Format.Should().Be("xlsx");
        File.Exists(xlsxFile.Path).Should().BeTrue();

        // Verify it's a valid XLSX (ZIP archive)
        using var archive = ZipFile.OpenRead(xlsxFile.Path);
        archive.Entries.Should().Contain(e => e.FullName == "[Content_Types].xml");
        archive.Entries.Should().Contain(e => e.FullName == "xl/workbook.xml");
        archive.Entries.Should().Contain(e => e.FullName == "xl/worksheets/sheet1.xml");
        archive.Entries.Should().Contain(e => e.FullName == "xl/sharedStrings.xml");
    }

    [Fact]
    public async Task ExportToParquet_WithValidData_ShouldCreateReadableParquetFile()
    {
        await CreateTestJsonlFileAsync("SPY.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "SPY", Price = 450.25m, Size = 100 },
            new { Timestamp = "2026-01-03T10:00:01Z", Symbol = "SPY", Price = 450.50m, Size = 200 }
        });

        var result = await _service.ExportAsync(new ExportRequest
        {
            ProfileId = "python-pandas",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "SPY" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        });

        result.Success.Should().BeTrue();
        result.Files.Should().ContainSingle();
        result.TotalRecords.Should().Be(2);

        var parquetFile = result.Files.Single();
        parquetFile.Format.Should().Be("parquet");
        parquetFile.Path.Should().EndWith(".parquet");
        File.Exists(parquetFile.Path).Should().BeTrue();

        await using var stream = File.OpenRead(parquetFile.Path);
        using var reader = await ParquetReader.CreateAsync(stream);
        var rowCount = 0L;
        for (var index = 0; index < reader.RowGroupCount; index++)
        {
            using var rowGroup = reader.OpenRowGroupReader(index);
            rowCount += rowGroup.RowCount;
        }

        rowCount.Should().Be(2);
    }

    [Fact]
    public async Task ExportToArrow_WithValidData_ShouldCreateReadableArrowFile()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await CreateTestJsonlFileAsync("SPY.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "SPY", Price = 450.25m, Size = 100 },
            new { Timestamp = "2026-01-03T10:00:01Z", Symbol = "SPY", Price = 450.50m, Size = 200 }
        });

        var result = await _service.ExportAsync(new ExportRequest
        {
            ProfileId = "arrow-feather",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "SPY" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        }, cancellation.Token);

        result.Success.Should().BeTrue();
        result.Files.Should().ContainSingle();
        result.TotalRecords.Should().Be(2);

        var arrowFile = result.Files.Single();
        arrowFile.Format.Should().Be("arrow");
        arrowFile.Path.Should().EndWith(".arrow");
        File.Exists(arrowFile.Path).Should().BeTrue();

        await using var stream = File.OpenRead(arrowFile.Path);
        using var reader = new ArrowFileReader(stream);
        (await reader.RecordBatchCountAsync()).Should().Be(1);
        reader.IsFileValid.Should().BeTrue();

        using var batch = await reader.ReadRecordBatchAsync(0, cancellation.Token);
        batch.Length.Should().Be(2);
        batch.Schema.FieldsList.Select(field => field.Name)
            .Should().Equal("Timestamp", "Symbol", "Price", "Size");

        var timestampField = batch.Schema.FieldsList.Single(field => field.Name == "Timestamp");
        timestampField.DataType.Should().BeOfType<TimestampType>();
        var timestampType = (TimestampType)timestampField.DataType;
        timestampType.Unit.Should().Be(TimeUnit.Nanosecond);
        timestampType.Timezone.Should().Be("UTC");

        var expectedFirstTimestamp = new DateTimeOffset(
            2026,
            1,
            3,
            10,
            0,
            0,
            TimeSpan.Zero);
        var timestamps = (TimestampArray)batch.Column("Timestamp");
        timestamps.GetTimestamp(0).Should().Be(expectedFirstTimestamp);
        timestamps.GetTimestamp(1).Should().Be(expectedFirstTimestamp.AddSeconds(1));
        timestamps.GetValue(0).Should().Be(
            expectedFirstTimestamp.ToUnixTimeMilliseconds() * 1_000_000L);

        var symbols = (StringArray)batch.Column("Symbol");
        symbols.GetString(0).Should().Be("SPY");
        symbols.GetString(1).Should().Be("SPY");

        var prices = (DoubleArray)batch.Column("Price");
        prices.GetValue(0).Should().Be(450.25d);
        prices.GetValue(1).Should().Be(450.50d);
    }

    [Fact]
    public async Task ExportAsync_WithNoMatchingSourceData_ShouldFailWithoutArtifact()
    {
        // Arrange
        var request = new ExportRequest
        {
            ProfileId = "excel",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "NONEXISTENT" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        // Act
        var result = await _service.ExportAsync(request);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No source data");
        result.FilesGenerated.Should().Be(0);
        result.Files.Should().BeEmpty();
        result.Warnings.Should().Contain(w => w.Contains("No source data"));
    }

    [Fact]
    public async Task ExportAsync_WithEmptyMatchingSourceFile_ShouldFailAndRemoveEmptyArtifact()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_testDataRoot, "AAPL.Trade.jsonl"),
            string.Empty);

        var result = await _service.ExportAsync(new ExportRequest
        {
            ProfileId = "excel",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "AAPL" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No exportable records");
        result.FilesGenerated.Should().Be(0);
        result.Files.Should().BeEmpty();
        Directory.EnumerateFiles(_testOutputDir, "*.xlsx", SearchOption.AllDirectories)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(ExportFormat.Arrow, ".arrow")]
    [InlineData(ExportFormat.Parquet, ".parquet")]
    [InlineData(ExportFormat.Xlsx, ".xlsx")]
    public async Task ExportAsync_WhenDeterministicArtifactExistsAndOverwriteIsFalse_PreservesExistingBytes(
        ExportFormat format,
        string extension)
    {
        await CreateTestJsonlFileAsync("AAPL.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });
        var existingBytes = Encoding.UTF8.GetBytes("retained-certified-export");
        var existingPath = Path.Combine(
            _testOutputDir,
            $"AAPL_{DateTime.UtcNow:yyyyMMdd}{extension}");
        await File.WriteAllBytesAsync(existingPath, existingBytes);

        var result = await _service.ExportAsync(new ExportRequest
        {
            CustomProfile = CreateFormatProfile(format, "collision-no-overwrite"),
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "AAPL" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5),
            OverwriteExisting = false,
            ValidateBeforeExport = false,
            IncludeManifest = false
        });

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("already exists");
        result.Files.Should().BeEmpty();
        (await File.ReadAllBytesAsync(existingPath)).Should().Equal(existingBytes);
        Directory.EnumerateFiles(_testOutputDir, "*", SearchOption.AllDirectories)
            .Should().ContainSingle()
            .Which.Should().Be(existingPath);
    }

    [Theory]
    [InlineData(ExportFormat.Arrow, ".arrow")]
    [InlineData(ExportFormat.Parquet, ".parquet")]
    [InlineData(ExportFormat.Xlsx, ".xlsx")]
    public async Task ExportAsync_WhenLaterSymbolFailsAfterOverwrite_RestoresPreExistingArtifact(
        ExportFormat format,
        string extension)
    {
        await CreateTestJsonlFileAsync("AAPL.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });
        await File.WriteAllTextAsync(
            Path.Combine(_testDataRoot, "ZZZ.Trade.jsonl"),
            "{ malformed-json");

        var existingBytes = Encoding.UTF8.GetBytes("previous-approved-export");
        var existingPath = Path.Combine(
            _testOutputDir,
            $"AAPL_{DateTime.UtcNow:yyyyMMdd}{extension}");
        await File.WriteAllBytesAsync(existingPath, existingBytes);

        var result = await _service.ExportAsync(new ExportRequest
        {
            CustomProfile = CreateFormatProfile(format, "collision-rollback"),
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "AAPL", "ZZZ" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5),
            OverwriteExisting = true,
            ValidateBeforeExport = false,
            IncludeManifest = false
        });

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
        result.Files.Should().BeEmpty();
        (await File.ReadAllBytesAsync(existingPath)).Should().Equal(existingBytes);
        Directory.EnumerateFiles(_testOutputDir, "*", SearchOption.AllDirectories)
            .Should().ContainSingle()
            .Which.Should().Be(existingPath);
    }

    [Fact]
    public async Task ExportAsync_BySymbolGzipSource_ShouldDiscoverValidateAndExport()
    {
        await CreateBySymbolGzipJsonlFileAsync(
            "SPY",
            "Trade",
            new DateTime(2026, 1, 3),
            new[]
            {
                new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "SPY", Price = 450.25m, Size = 100 },
                new { Timestamp = "2026-01-03T10:00:01Z", Symbol = "SPY", Price = 450.50m, Size = 200 }
            });

        var request = new ExportRequest
        {
            ProfileId = "r-stats",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "SPY" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5),
            ValidateBeforeExport = true,
            ValidationRules = new ExportValidationRulesRequest { RequireData = true }
        };

        var preview = await _service.PreviewAsync(request, sampleSize: 1);
        var result = await _service.ExportAsync(request);

        preview.Error.Should().BeNull();
        preview.SourceFileCount.Should().Be(1);
        preview.TotalRecords.Should().Be(2);
        preview.Symbols.Should().Equal("SPY");
        preview.EventTypes.Should().Equal("Trade");
        preview.DateRange!.Start.Date.Should().Be(new DateTime(2026, 1, 3));

        result.Success.Should().BeTrue();
        result.TotalRecords.Should().Be(2);
        result.Files.Should().ContainSingle();
        result.Files[0].Format.Should().Be("csv");
        File.Exists(result.Files[0].Path).Should().BeTrue();
    }

    [Fact]
    public async Task ExportAsync_JsonlProfileWithCompressedSource_ShouldWriteDecompressedJsonl()
    {
        await CreateBySymbolGzipJsonlFileAsync(
            "SPY",
            "Trade",
            new DateTime(2026, 1, 3),
            new[]
            {
                new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "SPY", Price = 450.25m, Size = 100 }
            });

        var result = await _service.ExportAsync(new ExportRequest
        {
            CustomProfile = new ExportProfile
            {
                Id = "jsonl-uncompressed",
                Name = "JSONL uncompressed",
                Format = ExportFormat.Jsonl,
                Compression = new CompressionSettings { Type = CompressionType.None },
                IncludeDataDictionary = false,
                IncludeLoaderScript = false
            },
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "SPY" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5),
            ValidateBeforeExport = false,
            IncludeManifest = false
        });

        result.Success.Should().BeTrue();
        result.Files.Should().ContainSingle();
        result.Files[0].Path.Should().EndWith(".jsonl");
        result.Files[0].Path.Should().NotEndWith(".jsonl.gz");
        (await File.ReadAllTextAsync(result.Files[0].Path)).Should().Contain("\"Symbol\":\"SPY\"");
    }

    [Theory]
    [InlineData(".jsonl.zst")]
    [InlineData(".jsonl.lz4")]
    public async Task ExportAsync_BySymbolTierCopyWithCodecSuffixAndRawJsonl_ShouldReadActualContent(
        string suffix)
    {
        var directory = Path.Combine(_testDataRoot, "SPY", "Trade");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, $"2026-01-03{suffix}"),
            """
            {"Timestamp":"2026-01-03T10:00:00Z","Symbol":"SPY","Price":450.25,"Size":100}

            """);

        var result = await _service.ExportAsync(new ExportRequest
        {
            ProfileId = "excel",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "SPY" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5),
            ValidateBeforeExport = false,
            IncludeManifest = false
        });

        result.Success.Should().BeTrue();
        result.TotalRecords.Should().Be(1);
        result.Files.Should().ContainSingle(file => file.Format == "xlsx");
    }

    [Fact]
    public async Task ExportAsync_WhenLaterSymbolIsMalformed_ShouldRemoveNewArtifactsAndPreserveExistingFile()
    {
        var sentinel = Path.Combine(_testOutputDir, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "pre-existing");
        await CreateTestJsonlFileAsync("AAPL.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });
        await File.WriteAllTextAsync(
            Path.Combine(_testDataRoot, "ZZZ.Trade.jsonl"),
            "{ malformed-json");

        var result = await _service.ExportAsync(new ExportRequest
        {
            CustomProfile = new ExportProfile
            {
                Id = "cleanup-failure",
                Name = "Cleanup failure",
                Format = ExportFormat.Xlsx,
                SplitBySymbol = true,
                IncludeDataDictionary = false,
                IncludeLoaderScript = false
            },
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "AAPL", "ZZZ" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5),
            ValidateBeforeExport = false,
            IncludeManifest = false
        });

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
        result.FilesGenerated.Should().Be(0);
        result.Files.Should().BeEmpty();
        File.Exists(sentinel).Should().BeTrue();
        (await File.ReadAllTextAsync(sentinel)).Should().Be("pre-existing");
        Directory.EnumerateFiles(_testOutputDir, "*", SearchOption.AllDirectories)
            .Should().ContainSingle()
            .Which.Should().Be(sentinel);
    }

    [Fact]
    public async Task ExportAsync_WithPreCancelledToken_ShouldThrowAndCreateNoArtifact()
    {
        await CreateTestJsonlFileAsync("AAPL.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var act = () => _service.ExportAsync(
            new ExportRequest
            {
                ProfileId = "excel",
                OutputDirectory = _testOutputDir,
                EventTypes = new[] { "Trade" },
                Symbols = new[] { "AAPL" },
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 1, 5)
            },
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        Directory.EnumerateFiles(_testOutputDir, "*", SearchOption.AllDirectories)
            .Should().BeEmpty();
    }

    [Fact]
    public async Task ExportToXlsx_WithMultipleSymbols_ShouldCreateSeparateFiles()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        await CreateTestJsonlFileAsync("SPY.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "SPY", Price = 450.25m, Size = 200 }
        });

        var excelProfile = new ExportProfile
        {
            Id = "excel-test",
            Name = "Test Excel",
            Format = ExportFormat.Xlsx,
            SplitBySymbol = true
        };

        var request = new ExportRequest
        {
            CustomProfile = excelProfile,
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "AAPL", "SPY" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        // Act
        var result = await _service.ExportAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.FilesGenerated.Should().Be(2);
        result.Files.Should().Contain(f => f.Symbol == "AAPL");
        result.Files.Should().Contain(f => f.Symbol == "SPY");
    }

    [Fact]
    public async Task ExportToXlsx_ShouldContainCorrectSheetData()
    {
        // Arrange
        await CreateTestJsonlFileAsync("TEST.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "TEST", Price = 100.00m, Size = 50 }
        });

        var request = new ExportRequest
        {
            ProfileId = "excel",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "TEST" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        // Act
        var result = await _service.ExportAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var xlsxFile = result.Files.Single();

        // Verify worksheet contains data
        using var archive = ZipFile.OpenRead(xlsxFile.Path);
        var sheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml");
        sheetEntry.Should().NotBeNull();

        using var stream = sheetEntry!.Open();
        using var reader = new StreamReader(stream);
        var sheetXml = await reader.ReadToEndAsync();

        // Sheet should contain row data
        sheetXml.Should().Contain("<sheetData>");
        sheetXml.Should().Contain("<row r=\"1\">");  // Header row
        sheetXml.Should().Contain("<row r=\"2\">");  // Data row
    }

    [Fact]
    public async Task ExportToXlsx_WithMaxRecordsLimit_ShouldTruncate()
    {
        // Arrange - Create file with more records than the limit
        var records = Enumerable.Range(1, 100).Select(i => new
        {
            Timestamp = $"2026-01-03T10:{i:00}:00Z",
            Symbol = "TEST",
            Price = 100m + i,
            Size = i * 10
        }).ToArray();

        await CreateTestJsonlFileAsync("TEST.Trade.jsonl", records);

        var excelProfile = new ExportProfile
        {
            Id = "excel-limited",
            Name = "Excel Limited",
            Format = ExportFormat.Xlsx,
            MaxRecordsPerFile = 50
        };

        var request = new ExportRequest
        {
            CustomProfile = excelProfile,
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "TEST" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        // Act
        var result = await _service.ExportAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.TotalRecords.Should().Be(50);
    }

    [Fact]
    public async Task ExportToXlsx_ShouldHaveValidSharedStrings()
    {
        // Arrange
        await CreateTestJsonlFileAsync("TEST.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "TEST", Side = "Buy", Exchange = "NYSE" }
        });

        var request = new ExportRequest
        {
            ProfileId = "excel",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "TEST" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        // Act
        var result = await _service.ExportAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var xlsxFile = result.Files.Single();

        using var archive = ZipFile.OpenRead(xlsxFile.Path);
        var stringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        stringsEntry.Should().NotBeNull();

        using var stream = stringsEntry!.Open();
        using var reader = new StreamReader(stream);
        var stringsXml = await reader.ReadToEndAsync();

        // Should contain string values
        stringsXml.Should().Contain("<sst");
        stringsXml.Should().Contain("<si><t>");
    }

    [Fact]
    public void GetProfiles_ShouldIncludeExcelProfile()
    {
        // Act
        var profiles = _service.GetProfiles();

        // Assert
        profiles.Should().Contain(p => p.Id == "excel");
        var excelProfile = profiles.Single(p => p.Id == "excel");
        excelProfile.Format.Should().Be(ExportFormat.Xlsx);
        excelProfile.TargetTool.Should().Be("Excel");
    }

    [Fact]
    public async Task ExportToXlsx_WithSpecialCharacters_ShouldEscapeXml()
    {
        // Arrange - Test XML escaping with special characters
        await CreateTestJsonlFileAsync("TEST.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "TEST", Note = "Price < 100 & Volume > 50" }
        });

        var request = new ExportRequest
        {
            ProfileId = "excel",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "TEST" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        // Act
        var result = await _service.ExportAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        var xlsxFile = result.Files.Single();

        // Verify the file can be opened as a valid ZIP
        using var archive = ZipFile.OpenRead(xlsxFile.Path);
        var stringsEntry = archive.GetEntry("xl/sharedStrings.xml");

        using var stream = stringsEntry!.Open();
        using var reader = new StreamReader(stream);
        var stringsXml = await reader.ReadToEndAsync();

        // Should contain escaped characters
        stringsXml.Should().Contain("&lt;");  // < escaped
        stringsXml.Should().Contain("&amp;");  // & escaped
        stringsXml.Should().Contain("&gt;");   // > escaped
    }

    [Fact]
    public async Task PreviewAsync_WithValidData_ShouldReturnPreviewWithoutWritingFiles()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 },
            new { Timestamp = "2026-01-03T10:00:01Z", Symbol = "AAPL", Price = 185.55m, Size = 200 }
        });

        var request = new ExportRequest
        {
            ProfileId = "python-pandas",
            Symbols = new[] { "AAPL" },
            EventTypes = new[] { "Trade" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        // Act
        var preview = await _service.PreviewAsync(request, sampleSize: 1);

        // Assert
        preview.Error.Should().BeNull();
        preview.ProfileId.Should().Be("python-pandas");
        preview.TotalRecords.Should().Be(2);
        preview.SourceFileCount.Should().Be(1);
        preview.Symbols.Should().Contain("AAPL");
        preview.SampleRecords.Should().HaveCountGreaterThanOrEqualTo(1);
        preview.EstimatedOutputBytes.Should().BeGreaterThan(0);

        // No files should have been written to the output directory
        Directory.GetFiles(_testOutputDir).Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewAsync_WithNoData_ShouldReturnEmptyPreview()
    {
        // Arrange
        var request = new ExportRequest
        {
            ProfileId = "python-pandas",
            Symbols = new[] { "NONEXISTENT" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        // Act
        var preview = await _service.PreviewAsync(request);

        // Assert
        preview.Error.Should().BeNull();
        preview.TotalRecords.Should().Be(0);
        preview.SourceFileCount.Should().Be(0);
        preview.Symbols.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportAsync_ShouldGenerateLineageManifest()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 },
            new { Timestamp = "2026-01-03T10:00:01Z", Symbol = "AAPL", Price = 185.55m, Size = 200 }
        });

        var request = new ExportRequest
        {
            ProfileId = "python-pandas",
            OutputDirectory = _testOutputDir,
            Symbols = new[] { "AAPL" },
            EventTypes = new[] { "Trade" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        // Act
        var result = await _service.ExportAsync(request);

        // Assert
        result.Success.Should().BeTrue();
        result.LineageManifestPath.Should().NotBeNullOrEmpty();
        File.Exists(result.LineageManifestPath).Should().BeTrue();

        // Validate manifest is valid JSON with expected structure
        var manifestJson = await File.ReadAllTextAsync(result.LineageManifestPath!);
        var manifest = JsonDocument.Parse(manifestJson);
        var root = manifest.RootElement;

        root.GetProperty("version").GetString().Should().Be("1.0.0");
        root.GetProperty("generator").GetString().Should().Be("Meridian");
        root.GetProperty("exportProfile").GetProperty("id").GetString().Should().Be("python-pandas");
        root.GetProperty("sources").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("summary").GetProperty("sourceFileCount").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GenerateStandaloneLoaderAsync_Python_ShouldGenerateLoaderScript()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var outputDir = Path.Combine(_testOutputDir, "loaders");

        // Act
        var scriptPath = await _service.GenerateStandaloneLoaderAsync(outputDir, "python");

        // Assert
        scriptPath.Should().NotBeNullOrEmpty();
        File.Exists(scriptPath).Should().BeTrue();

        var content = await File.ReadAllTextAsync(scriptPath);
        content.Should().Contain("pandas");
        content.Should().Contain("load_trades");
    }

    [Fact]
    public async Task GenerateStandaloneLoaderAsync_RunMat_ShouldGenerateMatlabStyleLoaderScript()
    {
        await CreateTestJsonlFileAsync("SPY.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "SPY", Price = 597.25m, Size = 10 }
        });

        var outputDir = Path.Combine(_testOutputDir, "runmat-loaders");

        var scriptPath = await _service.GenerateStandaloneLoaderAsync(outputDir, "runmat");

        scriptPath.Should().EndWith(".m");
        File.Exists(scriptPath).Should().BeTrue();

        var content = await File.ReadAllTextAsync(scriptPath);
        content.Should().Contain("readmatrix");
        content.Should().Contain("function data = load_data");
        content.Should().Contain("pick_price_series");
    }

    [Fact]
    public async Task ExportAsync_RunMatProfile_ShouldWriteNumericFriendlyCsvAndLoaderScript()
    {
        await CreateTestJsonlFileAsync("AAPL.Trade.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100, Exchange = "XNAS" }
        });

        var request = new ExportRequest
        {
            ProfileId = "runmat",
            OutputDirectory = _testOutputDir,
            EventTypes = new[] { "Trade" },
            Symbols = new[] { "AAPL" },
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5)
        };

        var result = await _service.ExportAsync(request);

        result.Success.Should().BeTrue();
        result.LoaderScriptPath.Should().EndWith(".m");
        result.FilesGenerated.Should().Be(1);

        var csvContent = await File.ReadAllTextAsync(result.Files.Single().Path);
        csvContent.Should().StartWith("Timestamp,Price,Size,BidPrice,BidSize,AskPrice,AskSize,Open,High,Low,Close,Volume");
        csvContent.Should().NotContain("Symbol");
        csvContent.Should().Contain("1767434400000,185.5,100");
    }

    [Fact]
    public async Task ExportAsync_Csv_NeutralizesFormulaValuesAndEscapesUntrustedHeaders()
    {
        await CreateTestJsonlFileAsync(
            "AAPL.Trade.jsonl",
            [
                new Dictionary<string, object?>
                {
                    ["Timestamp"] = "2026-01-03T10:00:00Z",
                    ["Symbol"] = "AAPL",
                    ["=FormulaHeader"] = "=1+1",
                    ["Semicolon"] = "approved;=HYPERLINK(\"https://example.invalid\")",
                    ["CarriageReturn"] = "first\rsecond",
                    ["Quoted"] = "quoted \"value\"",
                    ["Unicode"] = "café 東京"
                }
            ]);

        var result = await _service.ExportAsync(new ExportRequest
        {
            CustomProfile = CreateFormatProfile(ExportFormat.Csv, "csv-formula-guard"),
            OutputDirectory = _testOutputDir,
            EventTypes = ["Trade"],
            Symbols = ["AAPL"],
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 5),
            ValidateBeforeExport = false,
            IncludeManifest = false
        });

        result.Success.Should().BeTrue();
        var csv = await File.ReadAllTextAsync(result.Files.Single().Path);
        csv.Should().Contain(",'=FormulaHeader,");
        csv.Should().Contain(",'=1+1,");
        csv.Should().Contain("\"approved;'=HYPERLINK(\"\"https://example.invalid\"\")\"");
        csv.Should().Contain("\"first\rsecond\"");
        csv.Should().Contain("\"quoted \"\"value\"\"\"");
        csv.Should().Contain("café 東京");
    }

    private async Task CreateTestJsonlFileAsync<T>(string fileName, T[] records)
    {
        var filePath = Path.Combine(_testDataRoot, fileName);
        var sb = new StringBuilder();

        foreach (var record in records)
        {
            sb.AppendLine(JsonSerializer.Serialize(record));
        }

        await File.WriteAllTextAsync(filePath, sb.ToString());
    }

    private async Task CreateBySymbolGzipJsonlFileAsync<T>(
        string symbol,
        string eventType,
        DateTime date,
        T[] records)
    {
        var directory = Path.Combine(_testDataRoot, symbol, eventType);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{date:yyyy-MM-dd}.jsonl.gz");

        await using var file = File.Create(path);
        await using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        await using var writer = new StreamWriter(gzip, Encoding.UTF8);
        foreach (var record in records)
            await writer.WriteLineAsync(JsonSerializer.Serialize(record));
    }

    private static ExportProfile CreateFormatProfile(ExportFormat format, string id) => new()
    {
        Id = id,
        Name = id,
        Format = format,
        SplitBySymbol = true,
        IncludeDataDictionary = false,
        IncludeLoaderScript = false,
        TimestampSettings = new TimestampSettings
        {
            Format = TimestampFormat.UnixNanoseconds,
            Timezone = "UTC",
            IncludeNanoseconds = true
        }
    };

    [Fact]
    public void ExportProfile_WithFormat_ShouldPreserveAllSettingsExceptFormat()
    {
        // Arrange - use a profile with non-default settings to make the test meaningful
        var original = ExportProfile.PythonPandas;

        // Act
        var modified = original.WithFormat(ExportFormat.Csv);

        // Assert - format is changed
        modified.Format.Should().Be(ExportFormat.Csv);

        // All other settings should be preserved
        modified.Id.Should().Be(original.Id);
        modified.Name.Should().Be(original.Name);
        modified.Description.Should().Be(original.Description);
        modified.TargetTool.Should().Be(original.TargetTool);
        modified.Compression.Type.Should().Be(original.Compression.Type);
        modified.TimestampSettings.Format.Should().Be(original.TimestampSettings.Format);
        modified.TimestampSettings.Timezone.Should().Be(original.TimestampSettings.Timezone);
        modified.IncludeLoaderScript.Should().Be(original.IncludeLoaderScript);
        modified.IncludeDataDictionary.Should().Be(original.IncludeDataDictionary);
        modified.FileNamePattern.Should().Be(original.FileNamePattern);
        modified.SplitBySymbol.Should().Be(original.SplitBySymbol);
        modified.SplitByDate.Should().Be(original.SplitByDate);
        modified.MaxRecordsPerFile.Should().Be(original.MaxRecordsPerFile);
    }

    [Fact]
    public void ExportProfile_WithFormat_ShouldNotMutateOriginalProfile()
    {
        // Arrange
        var original = ExportProfile.QuantConnectLean; // has Lean format

        // Act
        var modified = original.WithFormat(ExportFormat.Csv);

        // Assert - original is not mutated
        original.Format.Should().Be(ExportFormat.Lean);
        modified.Format.Should().Be(ExportFormat.Csv);
    }

    [Fact]
    public void GetProfiles_ShouldIncludeRunMatProfile()
    {
        var profiles = _service.GetProfiles();

        profiles.Should().Contain(p => p.Id == "runmat");
        var runMatProfile = profiles.Single(p => p.Id == "runmat");
        runMatProfile.Format.Should().Be(ExportFormat.Csv);
        runMatProfile.TargetTool.Should().Be("RunMat");
        runMatProfile.TimestampSettings.Format.Should().Be(TimestampFormat.UnixMilliseconds);
    }
}
