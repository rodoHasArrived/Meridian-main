using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// JSON-backed workflow preset store rooted under Meridian's resolved data directory.
/// </summary>
public sealed class FileWorkflowPresetStore : IWorkflowPresetStore
{
    // Additive nullable DTO properties (e.g. ViewStateEnvelope) stay on version 1: older
    // snapshots deserialize them as null and older binaries ignore them. Bump the version
    // only for breaking shape changes, and add a v(n-1)->v(n) migration in LoadAsync
    // instead of relying on the unsupported-version throw below.
    private const int SnapshotVersion = 1;

    private readonly string _snapshotPath;
    private readonly ILogger<FileWorkflowPresetStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileWorkflowPresetStore(string dataRoot, ILogger<FileWorkflowPresetStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var presetDirectory = Path.Combine(dataRoot, "workstation", "workflows");
        Directory.CreateDirectory(presetDirectory);
        _snapshotPath = Path.Combine(presetDirectory, "workflow-presets.json");
    }

    public async Task<IReadOnlyList<WorkflowPresetDto>> LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await LoadCoreAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<WorkflowPresetDto>> UpdateAsync(
        Func<IReadOnlyList<WorkflowPresetDto>, IReadOnlyList<WorkflowPresetDto>> update,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await LoadCoreAsync(ct).ConfigureAwait(false);
            var next = update(current).ToArray();
            await PersistAsync(next, ct).ConfigureAwait(false);
            return next;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyList<WorkflowPresetDto>> LoadCoreAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(_snapshotPath))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(_snapshotPath);
            var snapshot = await JsonSerializer.DeserializeAsync(
                    stream,
                    WorkflowPresetJsonContext.Default.WorkflowPresetSnapshot,
                    ct)
                .ConfigureAwait(false);
            if (snapshot is null)
            {
                return [];
            }

            if (snapshot.Version != SnapshotVersion)
            {
                throw new InvalidOperationException(
                    $"Workflow preset snapshot version {snapshot.Version} is not supported. Expected version {SnapshotVersion}: {_snapshotPath}");
            }

            return snapshot?.Presets ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Workflow preset snapshot is not valid JSON: {Path}", _snapshotPath);
            throw new InvalidOperationException($"Workflow preset snapshot is invalid: {_snapshotPath}", ex);
        }
    }

    private async Task PersistAsync(IReadOnlyList<WorkflowPresetDto> presets, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var snapshot = new WorkflowPresetSnapshot(SnapshotVersion, presets);
        var json = JsonSerializer.Serialize(snapshot, WorkflowPresetJsonContext.Default.WorkflowPresetSnapshot);
        await AtomicFileWriter.WriteAsync(_snapshotPath, json, ct).ConfigureAwait(false);
    }
}

internal sealed record WorkflowPresetSnapshot(int Version, IReadOnlyList<WorkflowPresetDto> Presets);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(WorkflowPresetSnapshot))]
internal sealed partial class WorkflowPresetJsonContext : JsonSerializerContext;
