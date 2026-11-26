using System.Text.Json;
using Meridian.Contracts.Integrations;

namespace Meridian.Storage.Integrations;

/// <summary>
/// Append-only actor flags (<see cref="IProviderIntegrationActorFlagStore"/>). Flags live in their own
/// category and never touch the flagged record: manifest versions stay immutable and digest-verified.
/// </summary>
public sealed partial class FileProviderIntegrationManifestStore : IProviderIntegrationActorFlagStore
{
    private const string ActorFlagCategory = "actor-flags";

    // The flag store itself, and other tenants' partitions when this is the root store.
    private static readonly HashSet<string> ActorScanExcludedCategories = new(StringComparer.Ordinal)
    {
        ActorFlagCategory, "tenants", "evidence-locks"
    };

    // Most specific first: a sync run also names its connection, and a connection its manifest.
    private static readonly string[] SubjectIdFields = ["syncRunId", "connectionId", "manifestId"];

    public async Task<IReadOnlyList<ProviderIntegrationStoredActorValue>> ScanStoredActorValuesAsync(CancellationToken ct = default)
    {
        var values = new List<ProviderIntegrationStoredActorValue>();
        if (!Directory.Exists(_rootPath))
        {
            return values;
        }

        foreach (var file in Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(_rootPath, file).Replace(Path.DirectorySeparatorChar, '/');
            var category = relative.Split('/', 2)[0];
            if (ActorScanExcludedCategories.Contains(category) || !relative.Contains('/', StringComparison.Ordinal))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // A record this store cannot read is not an actor finding; the audit script reports it.
                continue;
            }

            using (document)
            {
                CollectActorValues(document.RootElement, string.Empty, relative, category, subjectId: null, values);
            }
        }

        return values;
    }

    private static void CollectActorValues(
        JsonElement node,
        string path,
        string recordPath,
        string category,
        string? subjectId,
        List<ProviderIntegrationStoredActorValue> values)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            // The nearest object that names itself is the subject: a manifest version file wraps the
            // manifest ({ manifestReference, manifest: { manifestId, approvedBy, ... } }).
            subjectId = SubjectIdFields
                .Select(name => node.TryGetProperty(name, out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                ?? subjectId;
            string? recordedAt = null;
            foreach (var name in ProviderIntegrationActorLabelClassifier.TimestampFields)
            {
                if (node.TryGetProperty(name, out var stamp) && stamp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(stamp.GetString()))
                {
                    recordedAt = stamp.GetString();
                    break;
                }
            }

            foreach (var property in node.EnumerateObject())
            {
                var childPath = path.Length == 0 ? property.Name : $"{path}.{property.Name}";
                if (ProviderIntegrationActorLabelClassifier.ActorFields.Contains(property.Name)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    values.Add(new ProviderIntegrationStoredActorValue(
                        recordPath, category, subjectId, property.Name, childPath, property.Value.GetString()!, recordedAt));
                }

                CollectActorValues(property.Value, childPath, recordPath, category, subjectId, values);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in node.EnumerateArray())
            {
                CollectActorValues(item, $"{path}[{index++}]", recordPath, category, subjectId, values);
            }
        }
    }

    public async Task<bool> TryAppendActorFlagAsync(ProviderIntegrationActorFlagDto flag, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentException.ThrowIfNullOrWhiteSpace(flag.FlagId);
        ArgumentException.ThrowIfNullOrWhiteSpace(flag.FlaggedBy);
        if (!string.Equals(flag.FlagId, ProviderIntegrationActorFlagDto.ComputeFlagId(flag.RecordPath, flag.FieldPath, flag.RecordedValue), StringComparison.Ordinal))
        {
            throw new ArgumentException("The flag id must be derived from the record path, field path and recorded value.", nameof(flag));
        }

        await using var entityLock = await AcquireEntityLockAsync("evidence-locks", $"actor-flag:{flag.FlagId}", ct).ConfigureAwait(false);
        var path = Path.Combine(GetDirectory(ActorFlagCategory), $"{flag.FlagId}.json");
        if (File.Exists(path))
        {
            // Append-only: an existing flag is never replaced, so the first scan's evidence stands.
            return false;
        }

        await WriteAsync(path, flag, ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationActorFlagDto, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<ProviderIntegrationActorFlagDto>> ListActorFlagsAsync(CancellationToken ct = default)
    {
        var flags = await ListAsync(
            Path.Combine(_rootPath, ActorFlagCategory),
            ProviderIntegrationContractsJsonContext.Default.ProviderIntegrationActorFlagDto,
            ct).ConfigureAwait(false);
        return flags
            .OrderBy(flag => flag.RecordPath, StringComparer.Ordinal)
            .ThenBy(flag => flag.FieldPath, StringComparer.Ordinal)
            .ToArray();
    }
}
