using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Export;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Ui.Tests.Services;

public sealed class AtomicPersistenceServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "mdc-atomic-persistence-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task ConfigService_SaveConfigAsync_WritesThroughBasePersistencePath()
    {
        var configPath = Path.Combine(_tempDir, "config", "appsettings.json");
        var service = new ConfigService(() => configPath);

        await service.SaveConfigAsync(new AppConfigDto
        {
            DataSource = "Polygon",
            DataRoot = "operator-data",
            Compress = true
        });

        File.Exists(configPath).Should().BeTrue();
        var loaded = await service.LoadConfigAsync();
        loaded.Should().NotBeNull();
        loaded!.DataSource.Should().Be("Polygon");
        loaded.DataRoot.Should().Be("operator-data");
        loaded.Compress.Should().BeTrue();
        Directory.GetFiles(Path.GetDirectoryName(configPath)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task ExportPresetsAsync_DoesNotMutateStoredPresetIdentityOrUsage()
    {
        var presetDirectory = Path.Combine(_tempDir, "presets");
        var exportDirectory = Path.Combine(_tempDir, "missing", "exports");
        var service = new TestExportPresetService(presetDirectory);
        var preset = await service.CreatePresetAsync("Operator packet", format: ExportPresetFormat.Jsonl);
        await service.RecordPresetUsageAsync(preset.Id);

        var originalId = preset.Id;
        var originalUseCount = preset.UseCount;

        var exportPath = await service.ExportPresetsAsync(new[] { preset.Id }, exportDirectory);

        File.Exists(exportPath).Should().BeTrue();
        service.GetPreset(originalId).Should().NotBeNull();
        preset.Id.Should().Be(originalId);
        preset.IsBuiltIn.Should().BeFalse();
        preset.UseCount.Should().Be(originalUseCount);
        preset.LastUsedAt.Should().NotBeNull();

        var exported = JsonSerializer.Deserialize<List<ExportPreset>>(await File.ReadAllTextAsync(exportPath));
        exported.Should().ContainSingle();
        exported![0].Id.Should().NotBe(originalId);
        exported[0].IsBuiltIn.Should().BeFalse();
        exported[0].UseCount.Should().Be(0);
        exported[0].LastUsedAt.Should().BeNull();
    }

    [Fact]
    public async Task QualityArchiveStore_RecordAsync_PersistsHistoryToConfiguredArchiveFile()
    {
        var archivePath = Path.Combine(_tempDir, "quality", "quality-archive.json");
        await using (var store = new QualityArchiveStore(NullLogger<QualityArchiveStore>.Instance, archivePath))
        {
            await store.RecordAsync("spy", new DateOnly(2026, 5, 12), 0.98, CancellationToken.None);
        }

        await using var reloaded = new QualityArchiveStore(NullLogger<QualityArchiveStore>.Instance, archivePath);
        var history = await reloaded.GetHistoryAsync("SPY", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), CancellationToken.None);

        history.Should().ContainSingle();
        history[0].Symbol.Should().Be("SPY");
        history[0].Date.Should().Be(new DateOnly(2026, 5, 12));
        history[0].CompletenessScore.Should().Be(0.98);
        Directory.GetFiles(Path.GetDirectoryName(archivePath)!, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPresetsAsync_CorruptedFile_FallsBackAndLogsFailure()
    {
        var presetDirectory = Path.Combine(_tempDir, "corrupted-presets");
        Directory.CreateDirectory(presetDirectory);
        await File.WriteAllTextAsync(Path.Combine(presetDirectory, "export_presets.json"), "{not valid json");

        var service = new TestExportPresetService(presetDirectory);

        await service.LoadPresetsAsync();

        service.Presets.Should().NotBeEmpty();
        service.LoadFailures.Should().ContainSingle();
    }

    [Fact]
    public async Task SavePresetsAsync_WriteFailure_ReturnsFalseAndRaisesFailureEvent()
    {
        var presetDirectory = Path.Combine(_tempDir, "save-failure-presets");
        var service = new TestExportPresetService(presetDirectory)
        {
            ForceWriteFailure = true
        };

        Exception? raisedException = null;
        service.SaveFailed += (_, ex) => raisedException = ex;

        _ = await service.CreatePresetAsync("Fails to save", format: ExportPresetFormat.Jsonl);

        raisedException.Should().NotBeNull();
        service.SaveFailures.Should().ContainSingle();
    }

    private sealed class TestExportPresetService : ExportPresetServiceBase
    {
        public bool ForceWriteFailure { get; set; }
        public List<Exception> LoadFailures { get; } = new();
        public List<Exception> SaveFailures { get; } = new();

        public TestExportPresetService(string presetsDirectoryPath)
            : base(presetsDirectoryPath)
        {
        }

        protected override Task WritePresetsFileAsync(string json, CancellationToken cancellationToken)
        {
            if (ForceWriteFailure)
            {
                throw new IOException("Forced write failure for test coverage.");
            }

            return base.WritePresetsFileAsync(json, cancellationToken);
        }

        protected override void LogLoadFailure(Exception exception)
        {
            LoadFailures.Add(exception);
            base.LogLoadFailure(exception);
        }

        protected override void LogSaveFailure(Exception exception)
        {
            SaveFailures.Add(exception);
            base.LogSaveFailure(exception);
        }
    }
}
