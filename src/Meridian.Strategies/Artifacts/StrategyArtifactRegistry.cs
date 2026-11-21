using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Storage.Archival;
using Meridian.Strategies.Storage;

namespace Meridian.Strategies.Artifacts;

/// <summary>Immutable strategy inputs, published by digest before a run can reference them.</summary>
public sealed class StrategyArtifactRegistry(string dataRoot)
{
    private readonly string _directory = Path.GetFullPath(Path.Combine(dataRoot, "strategies", "artifacts"));

    public async Task<string> RetainAsync(StrategyArtifact artifact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var payload = JsonSerializer.Serialize(artifact, StrategyArtifactJsonContext.Default.StrategyArtifact);
        var snapshot = JsonSerializer.Deserialize(payload, StrategyArtifactJsonContext.Default.StrategyArtifact)!;
        var id = snapshot.ComputeId();
        var path = PathFor(id);
        await using var lease = await JsonlPromotionRecordStore.AcquireAuthorityLockAsync(path + ".lock", ct)
            .ConfigureAwait(false);
        if (await GetAsync(id, ct).ConfigureAwait(false) is null)
        {
            await AtomicFileWriter.WriteAsync(path, payload,
                UnixFileMode.UserRead | UnixFileMode.UserWrite, ct).ConfigureAwait(false);
        }
        return id;
    }

    public async Task<StrategyArtifact?> GetAsync(string id, CancellationToken ct = default)
    {
        var path = PathFor(id);
        if (!File.Exists(path))
            return null;

        var payload = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var artifact = JsonSerializer.Deserialize(payload, StrategyArtifactJsonContext.Default.StrategyArtifact);
        if (artifact is null || !string.Equals(artifact.ComputeId(), id, StringComparison.Ordinal))
            throw new InvalidDataException($"Strategy artifact {id} failed its content check.");

        return artifact;
    }

    private string PathFor(string id)
    {
        if (!StrategyArtifact.IsValidId(id))
            throw new ArgumentException("Expected a versioned strategy artifact ID.", nameof(id));
        return Path.Combine(_directory, id + ".json");
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(StrategyArtifact))]
internal sealed partial class StrategyArtifactJsonContext : JsonSerializerContext;
