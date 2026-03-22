using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Storage.Export;
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
    public async Task ExportToXlsx_WithNoData_ShouldCreateEmptyXlsxFile()
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
        result.Success.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Contains("No source data"));
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
