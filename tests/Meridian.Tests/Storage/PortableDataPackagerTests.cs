using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using Meridian.Storage.Packaging;
using Xunit;

namespace Meridian.Tests.Storage;

public class PortableDataPackagerTests : IDisposable
{
    private readonly string _testDataRoot;
    private readonly string _testOutputDir;
    private readonly PortableDataPackager _packager;

    public PortableDataPackagerTests()
    {
        _testDataRoot = Path.Combine(Path.GetTempPath(), $"mdc_packager_test_{Guid.NewGuid():N}");
        _testOutputDir = Path.Combine(Path.GetTempPath(), $"mdc_packager_out_{Guid.NewGuid():N}");

        Directory.CreateDirectory(_testDataRoot);
        Directory.CreateDirectory(_testOutputDir);

        _packager = new PortableDataPackager(_testDataRoot);
    }

    public void Dispose()
    {
        DeleteDirectoryWithRetry(_testDataRoot);
        DeleteDirectoryWithRetry(_testOutputDir);
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        if (!Directory.Exists(path))
            return;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(10);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(10);
            }
        }
    }

    [Fact]
    public async Task CreatePackageAsync_WithValidData_ShouldCreatePackage()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 },
            new { Timestamp = "2026-01-03T10:00:01Z", Symbol = "AAPL", Price = 185.55m, Size = 200 }
        });

        var options = new PackageOptions
        {
            Name = "test-package",
            Description = "Test package for unit tests",
            OutputDirectory = _testOutputDir,
            IncludeQualityReport = false,
            VerifyChecksums = true
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.PackagePath.Should().NotBeNull();
        result.FilesIncluded.Should().Be(1);
        result.PackageSizeBytes.Should().BeGreaterThan(0);
        result.TotalEvents.Should().Be(2);
        File.Exists(result.PackagePath).Should().BeTrue();
    }

    [Fact]
    public async Task CreatePackageAsync_WithNoMatchingFiles_ShouldReturnFailure()
    {
        // Arrange
        var options = new PackageOptions
        {
            Name = "empty-package",
            OutputDirectory = _testOutputDir,
            Symbols = new[] { "NONEXISTENT" }
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No data files found");
    }

    [Fact]
    public async Task CreatePackageAsync_WithSymbolFilter_ShouldFilterFiles()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        await CreateTestJsonlFileAsync("MSFT/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "MSFT", Price = 380.25m, Size = 150 }
        });

        var options = new PackageOptions
        {
            Name = "filtered-package",
            OutputDirectory = _testOutputDir,
            Symbols = new[] { "AAPL" }
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.Symbols.Should().Contain("AAPL");
        result.Symbols.Should().NotContain("MSFT");
        result.FilesIncluded.Should().Be(1);
    }

    [Fact]
    public async Task CreatePackageAsync_WithDateFilter_ShouldFilterByDate()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL.Trade.2026-01-01.jsonl", new[]
        {
            new { Timestamp = "2026-01-01T10:00:00Z", Symbol = "AAPL", Price = 185.00m, Size = 100 }
        });

        await CreateTestJsonlFileAsync("AAPL.Trade.2026-01-05.jsonl", new[]
        {
            new { Timestamp = "2026-01-05T10:00:00Z", Symbol = "AAPL", Price = 186.00m, Size = 100 }
        });

        var options = new PackageOptions
        {
            Name = "date-filtered-package",
            OutputDirectory = _testOutputDir,
            StartDate = new DateTime(2026, 1, 1),
            EndDate = new DateTime(2026, 1, 3)
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.FilesIncluded.Should().Be(1); // Only the 2026-01-01 file
    }

    [Fact]
    public async Task CreatePackageAsync_ShouldIncludeManifest()
    {
        // Arrange
        await CreateTestJsonlFileAsync("SPY/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "SPY", Price = 450.25m, Size = 200 }
        });

        var options = new PackageOptions
        {
            Name = "manifest-test",
            OutputDirectory = _testOutputDir,
            IncludeLoaderScripts = true,
            IncludeDataDictionary = true
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.Manifest.Should().NotBeNull();
        result.Manifest!.PackageId.Should().NotBeNullOrEmpty();
        result.Manifest.Name.Should().Be("manifest-test");
        result.Manifest.TotalFiles.Should().Be(1);

        // Verify manifest is in the package
        using var archive = ZipFile.OpenRead(result.PackagePath!);
        archive.Entries.Should().Contain(e => e.Name == "manifest.json");
        archive.Entries.Should().Contain(e => e.Name == "README.md");
    }

    [Fact]
    public async Task CreatePackageAsync_WithLoaderScripts_ShouldIncludeScripts()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var options = new PackageOptions
        {
            Name = "scripts-test",
            OutputDirectory = _testOutputDir,
            IncludeLoaderScripts = true
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        using var archive = ZipFile.OpenRead(result.PackagePath!);
        archive.Entries.Should().Contain(e => e.FullName.Contains("load_data.py"));
        archive.Entries.Should().Contain(e => e.FullName.Contains("load_data.R"));
    }

    [Fact]
    public async Task CreatePackageAsync_WithChecksums_ShouldComputeSha256()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var options = new PackageOptions
        {
            Name = "checksum-test",
            OutputDirectory = _testOutputDir,
            VerifyChecksums = true
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.PackageChecksum.Should().NotBeNullOrEmpty();
        result.Manifest!.Files.Should().AllSatisfy(f => f.ChecksumSha256.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task ImportPackageAsync_WithValidPackage_ShouldExtractFiles()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var createOptions = new PackageOptions
        {
            Name = "import-test",
            OutputDirectory = _testOutputDir,
            VerifyChecksums = true
        };

        var createResult = await _packager.CreatePackageAsync(createOptions);
        createResult.Success.Should().BeTrue();

        var extractDir = Path.Combine(_testOutputDir, "extracted");
        Directory.CreateDirectory(extractDir);

        // Act
        var importResult = await _packager.ImportPackageAsync(
            createResult.PackagePath!,
            extractDir,
            validateChecksums: true);

        // Assert
        importResult.Success.Should().BeTrue();
        importResult.FilesExtracted.Should().BeGreaterThan(0);
        importResult.BytesExtracted.Should().BeGreaterThan(0);
        importResult.ValidationFailures.Should().Be(0);
        Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories).Should().NotBeEmpty();
    }

    [Fact]
    public async Task ImportPackageAsync_WithPathTraversalEntry_ShouldRejectPackage()
    {
        var packagePath = Path.Combine(_testOutputDir, "path-traversal.zip");
        var extractDir = Path.Combine(_testOutputDir, "extract-safe");
        var outsidePath = Path.Combine(_testOutputDir, "escaped.txt");

        Directory.CreateDirectory(extractDir);
        await CreateTraversalPackageAsync(packagePath);

        var result = await _packager.ImportPackageAsync(packagePath, extractDir, validateChecksums: true);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("outside the destination directory");
        File.Exists(outsidePath).Should().BeFalse();
        Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task ImportPackageAsync_WithChecksumMismatch_ShouldNotWriteFileToDestination()
    {
        var packagePath = Path.Combine(_testOutputDir, "checksum-mismatch.zip");
        var extractDir = Path.Combine(_testOutputDir, "extract-mismatch");

        Directory.CreateDirectory(extractDir);
        await CreateChecksumMismatchPackageAsync(packagePath);

        var result = await _packager.ImportPackageAsync(packagePath, extractDir, validateChecksums: true);

        result.Success.Should().BeFalse();
        result.ValidationFailures.Should().Be(1);
        File.Exists(Path.Combine(extractDir, "AAPL/Trade/2026-01-03.jsonl")).Should().BeFalse();
    }

    [Fact]
    public async Task ImportPackageAsync_WithNonExistentFile_ShouldReturnFailure()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testOutputDir, "nonexistent.zip");

        // Act
        var result = await _packager.ImportPackageAsync(nonExistentPath, _testOutputDir);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ValidatePackageAsync_WithValidPackage_ShouldPass()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var createOptions = new PackageOptions
        {
            Name = "validate-test",
            OutputDirectory = _testOutputDir
        };

        var createResult = await _packager.CreatePackageAsync(createOptions);
        createResult.Success.Should().BeTrue();

        // Act
        var validationResult = await _packager.ValidatePackageAsync(createResult.PackagePath!);

        // Assert
        validationResult.IsValid.Should().BeTrue();
        validationResult.Manifest.Should().NotBeNull();
        validationResult.Issues.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task ValidatePackageAsync_WithNonExistentFile_ShouldFail()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testOutputDir, "nonexistent.zip");

        // Act
        var result = await _packager.ValidatePackageAsync(nonExistentPath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("not found");
    }

    [Fact]
    public async Task ListPackageContentsAsync_ShouldReturnContents()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var createOptions = new PackageOptions
        {
            Name = "list-test",
            Description = "Test description",
            OutputDirectory = _testOutputDir
        };

        var createResult = await _packager.CreatePackageAsync(createOptions);
        createResult.Success.Should().BeTrue();

        // Act
        var contents = await _packager.ListPackageContentsAsync(createResult.PackagePath!);

        // Assert
        contents.Name.Should().Be("list-test");
        contents.Description.Should().Be("Test description");
        contents.TotalFiles.Should().Be(1);
        contents.Symbols.Should().Contain("AAPL");
        contents.Files.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreatePackageAsync_WithDifferentFormats_ShouldUseCorrectExtension()
    {
        // Arrange
        await CreateTestJsonlFileAsync("SPY/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "SPY", Price = 450.25m, Size = 100 }
        });

        // Act & Assert - ZIP format
        var zipOptions = new PackageOptions
        {
            Name = "zip-test",
            OutputDirectory = _testOutputDir,
            Format = PackageFormat.Zip
        };
        var zipResult = await _packager.CreatePackageAsync(zipOptions);
        zipResult.Success.Should().BeTrue();
        zipResult.PackagePath.Should().EndWith(".zip");
    }

    [Fact]
    public async Task CreatePackageAsync_WithCompressionLevels_ShouldApplyCompression()
    {
        // Arrange
        var largeData = Enumerable.Range(1, 1000).Select(i => new
        {
            Timestamp = $"2026-01-03T10:{i % 60:00}:00Z",
            Symbol = "TEST",
            Price = 100m + i,
            Size = i * 10
        }).ToArray();

        await CreateTestJsonlFileAsync("TEST/Trade/2026-01-03.jsonl", largeData);

        // Act - Maximum compression
        var maxOptions = new PackageOptions
        {
            Name = "max-compress",
            OutputDirectory = _testOutputDir,
            CompressionLevel = PackageCompressionLevel.Maximum
        };
        var maxResult = await _packager.CreatePackageAsync(maxOptions);

        // Act - No compression
        var noneOptions = new PackageOptions
        {
            Name = "no-compress",
            OutputDirectory = _testOutputDir,
            CompressionLevel = PackageCompressionLevel.None
        };
        var noneResult = await _packager.CreatePackageAsync(noneOptions);

        // Assert
        maxResult.Success.Should().BeTrue();
        noneResult.Success.Should().BeTrue();
        maxResult.PackageSizeBytes.Should().BeLessThanOrEqualTo(noneResult.PackageSizeBytes);
    }

    [Fact]
    public async Task CreatePackageAsync_WithTags_ShouldIncludeTagsInManifest()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var options = new PackageOptions
        {
            Name = "tags-test",
            OutputDirectory = _testOutputDir,
            Tags = new[] { "research", "equities", "2026" }
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.Manifest!.Tags.Should().BeEquivalentTo(new[] { "research", "equities", "2026" });
    }

    [Fact]
    public async Task CreatePackageAsync_WithCustomMetadata_ShouldIncludeMetadata()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var options = new PackageOptions
        {
            Name = "metadata-test",
            OutputDirectory = _testOutputDir,
            CustomMetadata = new Dictionary<string, string>
            {
                { "project", "research-alpha" },
                { "team", "quant-dev" }
            }
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        result.Manifest!.CustomMetadata.Should().ContainKey("project");
        result.Manifest.CustomMetadata!["project"].Should().Be("research-alpha");
    }

    [Fact]
    public async Task ProgressChanged_ShouldReportProgress()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var progressEvents = new List<PackageProgress>();
        _packager.ProgressChanged += (_, progress) => progressEvents.Add(progress);

        var options = new PackageOptions
        {
            Name = "progress-test",
            OutputDirectory = _testOutputDir
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeTrue();
        progressEvents.Should().NotBeEmpty();
        progressEvents.Should().Contain(p => p.Stage == PackageStage.Initializing);
        progressEvents.Should().Contain(p => p.Stage == PackageStage.Complete);
    }

    [Fact]
    public async Task CreatePackageAsync_WithCancellation_ShouldCancel()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var options = new PackageOptions
        {
            Name = "cancel-test",
            OutputDirectory = _testOutputDir
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var result = await _packager.CreatePackageAsync(options, cts.Token);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("cancelled");
    }

    [Fact]
    public async Task CreatePackageAsync_WithDataDictionary_ShouldIncludeDocumentation()
    {
        // Arrange
        await CreateTestJsonlFileAsync("AAPL/Trade/2026-01-03.jsonl", new[]
        {
            new { Timestamp = "2026-01-03T10:00:00Z", Symbol = "AAPL", Price = 185.50m, Size = 100 }
        });

        var options = new PackageOptions
        {
            Name = "dictionary-test",
            OutputDirectory = _testOutputDir,
            IncludeDataDictionary = true
        };

        // Act
        var result = await _packager.CreatePackageAsync(options);

        // Assert
        result.Success.Should().BeTrue();

        using var archive = ZipFile.OpenRead(result.PackagePath!);
        archive.Entries.Should().Contain(e => e.FullName.Contains("data_dictionary.md"));
    }

    private async Task CreateTestJsonlFileAsync<T>(string relativePath, T[] records)
    {
        var fullPath = Path.Combine(_testDataRoot, relativePath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var sb = new StringBuilder();
        foreach (var record in records)
        {
            sb.AppendLine(JsonSerializer.Serialize(record));
        }

        await File.WriteAllTextAsync(fullPath, sb.ToString());
    }

    private static async Task CreateTraversalPackageAsync(string packagePath)
    {
        var manifest = new PackageManifest
        {
            PackageId = "test-package",
            Name = "path-traversal-test",
            Files = Array.Empty<PackageFileEntry>(),
            SupplementaryFiles = new[]
            {
                new SupplementaryFileInfo
                {
                    Path = "../escaped.txt",
                    Type = "readme",
                    Description = "malicious"
                }
            },
            TotalFiles = 0
        };

        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("manifest.json");
        await using (var manifestStream = manifestEntry.Open())
        await using (var writer = new StreamWriter(manifestStream, Encoding.UTF8, 1024, leaveOpen: false))
        {
            await writer.WriteAsync(JsonSerializer.Serialize(manifest));
        }

        var maliciousEntry = archive.CreateEntry("../escaped.txt");
        await using (var maliciousStream = maliciousEntry.Open())
        await using (var writer = new StreamWriter(maliciousStream, Encoding.UTF8, 1024, leaveOpen: false))
        {
            await writer.WriteAsync("owned");
        }
    }

    private static async Task CreateChecksumMismatchPackageAsync(string packagePath)
    {
        const string filePath = "AAPL/Trade/2026-01-03.jsonl";
        const string payload = "{\"symbol\":\"AAPL\",\"price\":185.50}\n";

        var manifest = new PackageManifest
        {
            PackageId = "checksum-package",
            Name = "checksum-mismatch-test",
            Files = new[]
            {
                new PackageFileEntry
                {
                    Path = filePath,
                    Symbol = "AAPL",
                    EventType = "Trade",
                    ChecksumSha256 = new string('0', 64),
                    SizeBytes = Encoding.UTF8.GetByteCount(payload)
                }
            },
            TotalFiles = 1
        };

        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        var manifestEntry = archive.CreateEntry("manifest.json");
        await using (var manifestStream = manifestEntry.Open())
        await using (var writer = new StreamWriter(manifestStream, Encoding.UTF8, 1024, leaveOpen: false))
        {
            await writer.WriteAsync(JsonSerializer.Serialize(manifest));
        }

        var dataEntry = archive.CreateEntry(filePath);
        await using (var dataStream = dataEntry.Open())
        await using (var writer = new StreamWriter(dataStream, Encoding.UTF8, 1024, leaveOpen: false))
        {
            await writer.WriteAsync(payload);
        }
    }
}

public class PackageOptionsTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveReasonableDefaults()
    {
        // Act
        var options = new PackageOptions();

        // Assert
        options.Format.Should().Be(PackageFormat.Zip);
        options.CompressionLevel.Should().Be(PackageCompressionLevel.Balanced);
        options.InternalLayout.Should().Be(PackageLayout.ByDate);
        options.IncludeQualityReport.Should().BeTrue();
        options.IncludeDataDictionary.Should().BeTrue();
        options.IncludeLoaderScripts.Should().BeTrue();
        options.VerifyChecksums.Should().BeTrue();
    }

    [Fact]
    public void PackageFormat_Enum_ShouldHaveExpectedValues()
    {
        // Assert
        Enum.GetValues<PackageFormat>().Should().HaveCount(3);
        Enum.GetValues<PackageFormat>().Should().Contain(PackageFormat.Zip);
        Enum.GetValues<PackageFormat>().Should().Contain(PackageFormat.TarGz);
        Enum.GetValues<PackageFormat>().Should().Contain(PackageFormat.SevenZip);
    }
}

public class PackageManifestTests
{
    [Fact]
    public void NewManifest_ShouldHaveValidDefaults()
    {
        // Act
        var manifest = new PackageManifest();

        // Assert
        manifest.PackageId.Should().NotBeNullOrEmpty();
        manifest.PackageVersion.Should().Be("1.0.0");
        manifest.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        manifest.MinRequiredVersion.Should().Be("1.0.0");
    }

    [Fact]
    public void PackageFileEntry_ShouldStoreAllProperties()
    {
        // Arrange
        var entry = new PackageFileEntry
        {
            Path = "data/2026-01-03/AAPL/Trade/data.jsonl",
            Symbol = "AAPL",
            EventType = "Trade",
            Date = new DateTime(2026, 1, 3),
            Format = "jsonl",
            SizeBytes = 1024,
            EventCount = 100,
            ChecksumSha256 = "abc123"
        };

        // Assert
        entry.Path.Should().Be("data/2026-01-03/AAPL/Trade/data.jsonl");
        entry.Symbol.Should().Be("AAPL");
        entry.EventType.Should().Be("Trade");
        entry.SizeBytes.Should().Be(1024);
        entry.EventCount.Should().Be(100);
    }
}

public class PackageResultTests
{
    [Fact]
    public void CreateSuccess_ShouldSetSuccessFlag()
    {
        // Arrange
        var manifest = new PackageManifest { PackageId = "test-123" };

        // Act
        var result = PackageResult.CreateSuccess("/path/to/package.zip", manifest);

        // Assert
        result.Success.Should().BeTrue();
        result.PackagePath.Should().Be("/path/to/package.zip");
        result.Manifest.Should().Be(manifest);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void CreateFailure_ShouldSetErrorMessage()
    {
        // Act
        var result = PackageResult.CreateFailure("Something went wrong");

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Something went wrong");
        result.PackagePath.Should().BeNull();
    }

    [Fact]
    public void CompressionRatio_ShouldCalculateCorrectly()
    {
        // Arrange
        var result = new PackageResult
        {
            PackageSizeBytes = 1000,
            UncompressedSizeBytes = 5000
        };

        // Assert
        result.CompressionRatio.Should().Be(5.0);
    }

    [Fact]
    public void DurationSeconds_ShouldCalculateCorrectly()
    {
        // Arrange
        var result = new PackageResult
        {
            StartedAt = DateTime.UtcNow.AddSeconds(-10),
            CompletedAt = DateTime.UtcNow
        };

        // Assert
        result.DurationSeconds.Should().BeApproximately(10, 1);
    }
}

public class ImportResultTests
{
    [Fact]
    public void CreateSuccess_ShouldSetProperties()
    {
        // Arrange
        var manifest = new PackageManifest { PackageId = "import-123" };

        // Act
        var result = ImportResult.CreateSuccess("/source.zip", "/destination", manifest);

        // Assert
        result.Success.Should().BeTrue();
        result.SourcePath.Should().Be("/source.zip");
        result.DestinationPath.Should().Be("/destination");
        result.PackageId.Should().Be("import-123");
    }

    [Fact]
    public void CreateFailure_ShouldSetError()
    {
        // Act
        var result = ImportResult.CreateFailure("/source.zip", "Import failed");

        // Assert
        result.Success.Should().BeFalse();
        result.SourcePath.Should().Be("/source.zip");
        result.Error.Should().Be("Import failed");
    }
}
