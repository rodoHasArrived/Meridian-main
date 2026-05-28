using System.Text.Json;
using Meridian.Storage.Archival;

namespace Meridian.Application.Reconciliation;

public sealed class FileReconciliationDecisionJournal : IReconciliationDecisionJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly string _rootDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileReconciliationDecisionJournal(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        _rootDirectory = Path.Combine(dataRoot, "reconciliation", "decision-journal");
    }

    public async Task AppendDecisionAsync(BrokerCustodianMatchDecision decision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        await AppendJsonLineAsync(RunDecisionPath(decision.RunId), decision, ct).ConfigureAwait(false);
    }

    public async Task AppendHistoryAsync(ReconciliationResolutionHistoryEntry history, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(history);
        await AppendJsonLineAsync(DecisionHistoryPath(history.DecisionId), history, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BrokerCustodianMatchDecision>> ListDecisionsAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        return await ReadJsonLinesAsync<BrokerCustodianMatchDecision>(RunDecisionPath(runId), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReconciliationResolutionHistoryEntry>> ListHistoryAsync(string decisionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionId);
        return await ReadJsonLinesAsync<ReconciliationResolutionHistoryEntry>(DecisionHistoryPath(decisionId), ct).ConfigureAwait(false);
    }

    private async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = JsonSerializer.Serialize(value, JsonOptions);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AtomicFileWriter.AppendLinesAsync(path, [payload], ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<IReadOnlyList<T>> ReadJsonLinesAsync<T>(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var results = new List<T>();
        var lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var item = JsonSerializer.Deserialize<T>(line, JsonOptions);
            if (item is not null)
            {
                results.Add(item);
            }
        }

        return results;
    }

    private string RunDecisionPath(string runId)
        => SafePath("runs", runId, "decisions.jsonl");

    private string DecisionHistoryPath(string decisionId)
        => SafePath("decisions", decisionId, "history.jsonl");

    private string SafePath(string folder, string id, string fileName)
    {
        var safeId = string.Concat(id.Trim().Select(static ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
        var path = Path.GetFullPath(Path.Combine(_rootDirectory, folder, safeId, fileName));
        var root = Path.GetFullPath(_rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new ArgumentException("Reconciliation journal path escaped the configured root.", nameof(id));
        }

        return path;
    }
}
