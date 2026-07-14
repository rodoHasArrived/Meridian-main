using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Store;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Workflows;

/// <summary>
/// JSON-backed workflow preset store rooted under Meridian's resolved data directory.
/// </summary>
public sealed class FileWorkflowPresetStore : JsonFileSnapshotStore<WorkflowPresetSnapshot>, IWorkflowPresetStore
{
    // Additive nullable DTO properties (e.g. ViewStateEnvelope) stay on version 1: older
    // snapshots deserialize them as null and older binaries ignore them. Bump the version
    // only for breaking shape changes, and add a v(n-1)->v(n) migration in OnSnapshotLoaded
    // instead of relying on the unsupported-version throw below.
    private const int SnapshotVersion = 1;

    private readonly ILogger<FileWorkflowPresetStore> _logger;

    public FileWorkflowPresetStore(string dataRoot, ILogger<FileWorkflowPresetStore> logger)
        : base(ResolveSnapshotPath(dataRoot), WorkflowPresetJsonContext.Default.WorkflowPresetSnapshot)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<IReadOnlyList<WorkflowPresetDto>> LoadAsync(CancellationToken ct = default)
        => ReadSnapshotAsync(static snapshot => snapshot.Presets ?? [], ct);

    public async Task<IReadOnlyList<WorkflowPresetDto>> UpdateAsync(
        Func<IReadOnlyList<WorkflowPresetDto>, IReadOnlyList<WorkflowPresetDto>> update,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        return await UpdateSnapshotAsync(
                snapshot =>
                {
                    var next = update(snapshot.Presets ?? []).ToArray();
                    return (new WorkflowPresetSnapshot(SnapshotVersion, next), next);
                },
                ct)
            .ConfigureAwait(false);
    }

    protected override WorkflowPresetSnapshot CreateEmptySnapshot() => new(SnapshotVersion, []);

    protected override WorkflowPresetSnapshot HandleCorruptSnapshot(JsonException exception)
    {
        _logger.LogWarning(exception, "Workflow preset snapshot is not valid JSON: {Path}", SnapshotPath);
        throw new InvalidOperationException($"Workflow preset snapshot is invalid: {SnapshotPath}", exception);
    }

    protected override WorkflowPresetSnapshot OnSnapshotLoaded(WorkflowPresetSnapshot snapshot)
    {
        if (snapshot.Version != SnapshotVersion)
        {
            throw new InvalidOperationException(
                $"Workflow preset snapshot version {snapshot.Version} is not supported. Expected version {SnapshotVersion}: {SnapshotPath}");
        }

        return snapshot;
    }

    private static string ResolveSnapshotPath(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var presetDirectory = Path.Combine(dataRoot, "workstation", "workflows");
        Directory.CreateDirectory(presetDirectory);
        return Path.Combine(presetDirectory, "workflow-presets.json");
    }
}

public sealed record WorkflowPresetSnapshot(int Version, IReadOnlyList<WorkflowPresetDto> Presets);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, WriteIndented = true)]
[JsonSerializable(typeof(WorkflowPresetSnapshot))]
internal sealed partial class WorkflowPresetJsonContext : JsonSerializerContext;
