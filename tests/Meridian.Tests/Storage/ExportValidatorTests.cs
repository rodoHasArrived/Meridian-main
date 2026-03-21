using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Export;
using Meridian.Storage.Export;
using Xunit;

namespace Meridian.Tests.Storage;

public sealed class ExportValidatorTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly string _outputDir;
    private readonly ExportValidator _validator;

    public ExportValidatorTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), $"mdc_vtest_{Guid.NewGuid():N}");
        _outputDir = Path.Combine(Path.GetTempPath(), $"mdc_vout_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataRoot);
        Directory.CreateDirectory(_outputDir);
        _validator = new ExportValidator(_dataRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, true);
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, true);
    }

    // -------------------------------------------------------------------------
    // ExportValidator — disk-space / permission / data checks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_WithDataPresent_ReturnsValid()
    {
        // Arrange
        await WriteJsonlAsync("AAPL.Trade.jsonl", 3);

        var request = new ExportRequest
        {
            OutputDirectory = _outputDir,
            Symbols = new[] { "AAPL" },
            EventTypes = new[] { "Trade" },
            StartDate = DateTime.UtcNow.AddDays(-7),
            EndDate = DateTime.UtcNow
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
        result.EstimatedRecordCount.Should().Be(3);
    }

    [Fact]
    public async Task ValidateAsync_WithNoData_ReturnsWarning_NotError()
    {
        // Arrange — empty data root
        var request = new ExportRequest
        {
            OutputDirectory = _outputDir,
            Symbols = new[] { "NONEXISTENT" },
            EventTypes = new[] { "Trade" }
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert — warning, not error (default behaviour is to allow empty exports)
        result.IsValid.Should().BeTrue();
        result.Warnings.Should().Contain(w => w.Code == "NO_DATA");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_RequireData_WithNoData_ReturnsError()
    {
        // Arrange
        var request = new ExportRequest
        {
            OutputDirectory = _outputDir,
            Symbols = new[] { "NONEXISTENT" },
            EventTypes = new[] { "Trade" },
            ValidationRules = new ExportValidationRulesRequest { RequireData = true }
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "NO_DATA");
    }

    [Fact]
    public async Task ValidateAsync_UnwritableOutputDir_ReturnsPermissionError()
    {
        // Arrange — pick a path that cannot be created on the current platform.
        // On Windows: embed an existing file (kernel32.dll) as a path component so
        //   that Directory.CreateDirectory throws because the component is a file.
        // On Linux/Mac: /proc/sys/kernel is a read-only kernel filesystem.
        string badPath;
        if (OperatingSystem.IsWindows())
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            badPath = Path.Combine(system32, "kernel32.dll", "no_write_here_please");
        }
        else
        {
            badPath = Path.Combine("/proc/sys/kernel", "no_write_here_please");
        }

        var request = new ExportRequest
        {
            OutputDirectory = badPath,
            EventTypes = new[] { "Trade" }
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Code == "WRITE_PERMISSION");
    }

    [Fact]
    public async Task ValidateAsync_CsvFormatWithComplexEventType_ReturnsWarning()
    {
        // Arrange
        await WriteJsonlAsync("AAPL.LOBSnapshot.jsonl", 2);

        var request = new ExportRequest
        {
            OutputDirectory = _outputDir,
            EventTypes = new[] { "LOBSnapshot" },
            CustomProfile = new ExportProfile
            {
                Id = "test-csv",
                Name = "Test CSV",
                Format = ExportFormat.Csv
            }
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.Warnings.Should().Contain(w => w.Code == "CSV_COMPLEX_TYPES");
    }

    [Fact]
    public async Task ValidateAsync_ParquetFormatWithComplexEventType_NoFormatWarning()
    {
        // Arrange
        await WriteJsonlAsync("AAPL.LOBSnapshot.jsonl", 2);

        var request = new ExportRequest
        {
            OutputDirectory = _outputDir,
            EventTypes = new[] { "LOBSnapshot" },
            CustomProfile = new ExportProfile
            {
                Id = "test-parquet",
                Name = "Test Parquet",
                Format = ExportFormat.Parquet
            }
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.Issues.Should().NotContain(i => i.Code == "CSV_COMPLEX_TYPES");
    }

    [Fact]
    public async Task ValidateAsync_WritableDirectory_NoPermissionError()
    {
        // Arrange
        var request = new ExportRequest
        {
            OutputDirectory = _outputDir,
            EventTypes = new[] { "Trade" }
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.Issues.Should().NotContain(i => i.Code == "WRITE_PERMISSION");
    }

    [Fact]
    public async Task ValidateAsync_ReturnsEstimatedRecordCount()
    {
        // Arrange
        await WriteJsonlAsync("SPY.Trade.jsonl", 10);

        var request = new ExportRequest
        {
            OutputDirectory = _outputDir,
            Symbols = new[] { "SPY" },
            EventTypes = new[] { "Trade" }
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        result.EstimatedRecordCount.Should().Be(10);
    }

    // -------------------------------------------------------------------------
    // ExportVerifier — manifest verification
    // -------------------------------------------------------------------------

    [Fact]
    public async Task VerifyExportAsync_NoManifest_ReturnsFailed()
    {
        // Arrange — output dir with no manifest
        var verifier = new ExportVerifier();

        // Act
        var result = await verifier.VerifyExportAsync(_outputDir);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Contains("not found"));
    }

    [Fact]
    public async Task VerifyExportAsync_ValidExport_ReturnsSuccess()
    {
        // Arrange — create a JSONL file and a matching manifest
        var csvContent = "timestamp,price\n2026-01-03T10:00:00Z,100.0\n2026-01-03T10:01:00Z,101.0\n";
        var csvPath = Path.Combine(_outputDir, "AAPL_export.csv");
        await File.WriteAllTextAsync(csvPath, csvContent);

        var checksum = await ComputeChecksumAsync(csvPath);
        await WriteManifestAsync(new[]
        {
            new { relativePath = "AAPL_export.csv", checksumSha256 = checksum, recordCount = 2 }
        });

        var verifier = new ExportVerifier();

        // Act
        var result = await verifier.VerifyExportAsync(_outputDir);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ChecksumsValid.Should().BeTrue();
        result.RecordCountsValid.Should().BeTrue();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifyExportAsync_ChecksumMismatch_ReturnsFailed()
    {
        // Arrange
        var csvPath = Path.Combine(_outputDir, "SPY_export.csv");
        await File.WriteAllTextAsync(csvPath, "timestamp,price\n2026-01-03,420.0\n");
        await WriteManifestAsync(new[]
        {
            new { relativePath = "SPY_export.csv", checksumSha256 = "deadbeef", recordCount = 1 }
        });

        var verifier = new ExportVerifier();

        // Act
        var result = await verifier.VerifyExportAsync(_outputDir);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ChecksumsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Contains("Checksum mismatch"));
    }

    [Fact]
    public async Task VerifyExportAsync_MissingFile_ReturnsFailed()
    {
        // Arrange — manifest references a file that doesn't exist
        await WriteManifestAsync(new[]
        {
            new { relativePath = "missing_file.csv", checksumSha256 = "abc123", recordCount = 5 }
        });

        var verifier = new ExportVerifier();

        // Act
        var result = await verifier.VerifyExportAsync(_outputDir);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.Contains("Missing file") || i.Contains("not found"));
    }

    // -------------------------------------------------------------------------
    // StandardPresets — built-in preset definitions
    // -------------------------------------------------------------------------

    [Fact]
    public void StandardPresets_GetAll_ReturnsThreePresets()
    {
        var presets = StandardPresets.GetAll();
        presets.Should().HaveCount(3);
    }

    [Fact]
    public void StandardPresets_QuantConnectFormat_HasExpectedProperties()
    {
        var preset = StandardPresets.QuantConnectFormat;

        preset.Name.Should().Be("QuantConnect Lean Format");
        preset.Format.Should().Be(ExportPresetFormat.Lean);
        preset.Compression.Should().Be(ExportPresetCompression.Zip);
        preset.IncludeManifest.Should().BeTrue();
        preset.IsBuiltIn.Should().BeTrue();
        preset.Columns.Should().Contain("datetime")
                              .And.Contain("open")
                              .And.Contain("close")
                              .And.Contain("volume");
    }

    [Fact]
    public void StandardPresets_PandasDataFrame_HasExpectedProperties()
    {
        var preset = StandardPresets.PandasDataFrame;

        preset.Name.Should().Be("Pandas DataFrame (Parquet)");
        preset.Format.Should().Be(ExportPresetFormat.Parquet);
        preset.Compression.Should().Be(ExportPresetCompression.Snappy);
        preset.IncludeManifest.Should().BeTrue();
        preset.IsBuiltIn.Should().BeTrue();
    }

    [Fact]
    public void StandardPresets_ResearchNotebook_HasExpectedProperties()
    {
        var preset = StandardPresets.ResearchNotebook;

        preset.Name.Should().Be("Jupyter Notebook (CSV)");
        preset.Format.Should().Be(ExportPresetFormat.Csv);
        preset.Compression.Should().Be(ExportPresetCompression.Gzip);
        preset.IncludeManifest.Should().BeTrue();
        preset.IsBuiltIn.Should().BeTrue();
        preset.Columns.Should().Contain("timestamp")
                              .And.Contain("price")
                              .And.Contain("bid")
                              .And.Contain("ask");
    }

    [Fact]
    public void StandardPresets_AllBuiltIn_HaveUniqueIds()
    {
        var ids = StandardPresets.GetAll().Select(p => p.Id).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ExportPreset_IncludeManifest_DefaultsToTrue()
    {
        var preset = new ExportPreset();
        preset.IncludeManifest.Should().BeTrue();
    }

    [Fact]
    public void ExportPreset_Columns_DefaultsToEmpty()
    {
        var preset = new ExportPreset();
        preset.Columns.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task WriteJsonlAsync(string fileName, int lineCount)
    {
        var path = Path.Combine(_dataRoot, fileName);
        var sb = new StringBuilder();
        for (int i = 0; i < lineCount; i++)
            sb.AppendLine($"{{\"index\":{i}}}");
        await File.WriteAllTextAsync(path, sb.ToString());
    }

    private async Task WriteManifestAsync<T>(T[] outputs)
    {
        var manifest = new
        {
            version = "1.0.0",
            generatedAtUtc = DateTime.UtcNow.ToString("o"),
            generator = "Meridian",
            outputs
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(_outputDir, "lineage_manifest.json"), json);
    }

    private static async Task<string> ComputeChecksumAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
