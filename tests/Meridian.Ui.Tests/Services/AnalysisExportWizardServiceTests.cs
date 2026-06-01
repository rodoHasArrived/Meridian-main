using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Xunit;

namespace Meridian.Ui.Tests.Services;

public sealed class AnalysisExportWizardServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "meridian-analysis-export-wizard-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void GetExportProfiles_ShouldDescribeRunMatAsStrategyWorkflow()
    {
        var service = new AnalysisExportWizardService();

        var runMatProfile = service.GetExportProfiles().Should()
            .ContainSingle(profile => profile.Id == "runmat")
            .Subject;

        runMatProfile.Description.Should().Contain("RunMat Strategy workflows");
        runMatProfile.Description.Should().NotContain("RunMat research workflows");
    }

    [Fact]
    public async Task ExportToCsvAsync_ShouldWriteUnionHeaderAndAlignSparseRows()
    {
        var sourcePath = WriteJsonl(
            "SPY-trades-2026-01-02.jsonl",
            """
            {"timestamp":"2026-01-02T14:30:00Z","price":470.12}
            {"volume":100,"timestamp":"2026-01-02T14:31:00Z","price":470.25}
            {"timestamp":"2026-01-02T14:32:00Z","bid":470.2}
            """);
        var outputPath = Directory.CreateDirectory(Path.Combine(_root, "exports")).FullName;

        var result = await AnalysisExportWizardService.ExportToCsvAsync(
            "SPY",
            new List<string> { sourcePath },
            new ExportConfiguration { OutputPath = outputPath },
            CancellationToken.None);

        result.RecordCount.Should().Be(3);
        result.OutputFile.Should().Be(Path.Combine(outputPath, "SPY_6493341e.csv"));
        var lines = await File.ReadAllLinesAsync(result.OutputFile);
        lines.Should().Equal(
            "timestamp,price,volume,bid",
            "2026-01-02T14:30:00Z,470.12,,",
            "2026-01-02T14:31:00Z,470.25,100,",
            "2026-01-02T14:32:00Z,,,470.2");
    }

    [Theory]
    [InlineData("BRK/B; DROP", "BRK_B_ DROP", "market_data_brk_b__drop")]
    [InlineData("   ...   ", "symbol", "market_data____")]
    public void SafeNameHelpers_ShouldPreventPathAndSqlIdentifierInjection(
        string symbol,
        string expectedFileStem,
        string expectedSqlIdentifier)
    {
        AnalysisExportWizardService.CreateSafeFileStem(symbol).Should().Be(expectedFileStem);
        AnalysisExportWizardService.CreateSafeSqlIdentifier("market_data_", symbol).Should().Be(expectedSqlIdentifier);
    }

    [Fact]
    public async Task ExportToSqlAsync_ShouldReturnGeneratedSidecarScript()
    {
        var sourcePath = WriteJsonl(
            "BRK-B-trades-2026-01-02.jsonl",
            """{"timestamp":"2026-01-02T14:30:00Z","price":470.12,"volume":100}""");
        var outputPath = Directory.CreateDirectory(Path.Combine(_root, "sql-export")).FullName;

        var result = await AnalysisExportWizardService.ExportToSqlAsync(
            "BRK/B",
            new List<string> { sourcePath },
            new ExportConfiguration { OutputPath = outputPath },
            CancellationToken.None);

        var csvPath = Path.Combine(outputPath, "BRK_B_82f67012.csv");
        var scriptPath = Path.Combine(outputPath, "BRK_B_82f67012_load.sql");

        result.OutputFile.Should().Be(csvPath);
        result.GeneratedFiles.Should().ContainSingle().Which.Should().Be(scriptPath);
        File.Exists(csvPath).Should().BeTrue();
        File.Exists(scriptPath).Should().BeTrue();
        var script = await File.ReadAllTextAsync(scriptPath);
        script.Should().Contain("CREATE TABLE IF NOT EXISTS market_data_brk_b");
        script.Should().Contain("\\COPY market_data_brk_b FROM 'BRK_B_82f67012.csv' WITH CSV HEADER;");
    }

    [Fact]
    public void CreateUniqueFileStem_ShouldDisambiguateSymbolsWithSameSafeStem()
    {
        var first = AnalysisExportWizardService.CreateUniqueFileStem("A'B");
        var second = AnalysisExportWizardService.CreateUniqueFileStem("A;B");

        first.Should().StartWith("A_B_");
        second.Should().StartWith("A_B_");
        first.Should().NotBe(second);
    }

    [Fact]
    public async Task ExecuteExportAsync_ParquetFallbackNote_ShouldReferenceActualGeneratedCsv()
    {
        var (service, outputPath) = await CreateExportServiceAsync();

        var result = await service.ExecuteExportAsync(new ExportConfiguration
        {
            Profile = new ExportProfile { OutputFormat = "Parquet" },
            Symbols = new[] { "BRK/B" },
            DataTypes = new[] { "trades" },
            FromDate = new DateOnly(2026, 1, 1),
            ToDate = new DateOnly(2026, 1, 31),
            OutputPath = outputPath
        });

        var csvPath = Path.Combine(outputPath, "BRK_B_82f67012.csv");
        var notePath = Path.Combine(outputPath, "BRK_B_82f67012_parquet_note.txt");

        result.Success.Should().BeTrue();
        File.Exists(csvPath).Should().BeTrue();
        File.Exists(notePath).Should().BeTrue();
        var note = await File.ReadAllTextAsync(notePath);
        note.Should().Contain("pd.read_csv('BRK_B_82f67012.csv')");
        note.Should().NotContain("pd.read_csv('BRK_B.csv')");
    }

    [Fact]
    public async Task ExecuteExportAsync_Notebook_ShouldLoadActualGeneratedCsv()
    {
        var (service, outputPath) = await CreateExportServiceAsync();

        var result = await service.ExecuteExportAsync(new ExportConfiguration
        {
            Profile = new ExportProfile { OutputFormat = "Notebook" },
            Symbols = new[] { "BRK/B" },
            DataTypes = new[] { "trades" },
            FromDate = new DateOnly(2026, 1, 1),
            ToDate = new DateOnly(2026, 1, 31),
            OutputPath = outputPath
        });

        var csvPath = Path.Combine(outputPath, "BRK_B_82f67012.csv");
        var notebookPath = Path.Combine(outputPath, "BRK_B_82f67012_analysis.ipynb");

        result.Success.Should().BeTrue();
        File.Exists(csvPath).Should().BeTrue();
        File.Exists(notebookPath).Should().BeTrue();
        var notebook = await File.ReadAllTextAsync(notebookPath);
        notebook.Should().Contain("df = pd.read_csv('BRK_B_82f67012.csv')");
        notebook.Should().NotContain("df = pd.read_csv('BRK_B.csv')");
    }

    [Fact]
    public async Task ExecuteExportAsync_ShouldReturnFailureWhenSymbolExportThrows()
    {
        var dataRoot = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        WriteJsonl(
            Path.Combine(dataRoot, "SPY-trades-2026-01-02.jsonl"),
            """{"timestamp":"2026-01-02T14:30:00Z","price":470.12}""",
            rootedPath: true);
        var configPath = Path.Combine(_root, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(new AppConfigDto { DataRoot = dataRoot }));

        var service = new AnalysisExportWizardService(new ConfigService(() => configPath));
        var invalidOutputPath = Path.Combine(_root, "not-a-directory");
        await File.WriteAllTextAsync(invalidOutputPath, "blocks directory creation");

        var result = await service.ExecuteExportAsync(
            new ExportConfiguration
            {
                Profile = new ExportProfile { OutputFormat = "CSV" },
                Symbols = new[] { "SPY" },
                DataTypes = new[] { "trades" },
                FromDate = new DateOnly(2026, 1, 1),
                ToDate = new DateOnly(2026, 1, 31),
                OutputPath = invalidOutputPath
            });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        result.ProcessedSymbols.Should().Be(0);
    }

    private string WriteJsonl(string fileNameOrPath, string content, bool rootedPath = false)
    {
        var path = rootedPath ? fileNameOrPath : Path.Combine(_root, "source", fileNameOrPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private async Task<(AnalysisExportWizardService Service, string OutputPath)> CreateExportServiceAsync()
    {
        var dataRoot = Directory.CreateDirectory(Path.Combine(_root, "data")).FullName;
        WriteJsonl(
            Path.Combine(dataRoot, "BRK-B-trades-2026-01-02.jsonl"),
            """{"timestamp":"2026-01-02T14:30:00Z","price":470.12,"volume":100}""",
            rootedPath: true);

        var configPath = Path.Combine(_root, "config", "appsettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(
            configPath,
            JsonSerializer.Serialize(new AppConfigDto { DataRoot = dataRoot }));

        var outputPath = Directory.CreateDirectory(Path.Combine(_root, "export-output", Guid.NewGuid().ToString("N"))).FullName;
        return (new AnalysisExportWizardService(new ConfigService(() => configPath)), outputPath);
    }
}
