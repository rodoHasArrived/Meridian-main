using System.Text.Json;
using Meridian.Application.Reconciliation;
using Meridian.Domain.Reconciliation;

namespace Meridian.Infrastructure.Reconciliation;

public interface IReconciliationCaseStore
{
    Task SaveAsync(ReconciliationCase reconciliationCase, CancellationToken ct = default);
    Task<ReconciliationCase?> GetAsync(string caseId, CancellationToken ct = default);
    Task<IReadOnlyList<ReconciliationCase>> ListAsync(CancellationToken ct = default);
}

public sealed class JsonReconciliationCaseStore(string dataRoot) : IReconciliationCaseStore
{
    private readonly string _folder = Path.Combine(dataRoot, "reconciliation", "cases");
    public async Task SaveAsync(ReconciliationCase reconciliationCase, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(Path.Combine(_folder, $"{reconciliationCase.CaseId}.json"), JsonSerializer.Serialize(reconciliationCase), ct);
    }

    public Task<ReconciliationCase?> GetAsync(string caseId, CancellationToken ct = default)
    {
        var path = Path.Combine(_folder, $"{caseId}.json");
        return Task.FromResult(File.Exists(path) ? JsonSerializer.Deserialize<ReconciliationCase>(File.ReadAllText(path)) : null);
    }

    public Task<IReadOnlyList<ReconciliationCase>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_folder)) return Task.FromResult<IReadOnlyList<ReconciliationCase>>([]);
        return Task.FromResult<IReadOnlyList<ReconciliationCase>>(Directory.EnumerateFiles(_folder, "*.json")
            .Select(f => JsonSerializer.Deserialize<ReconciliationCase>(File.ReadAllText(f)))
            .Where(x => x is not null)
            .Cast<ReconciliationCase>()
            .ToList());
    }
}

public sealed class ReconciliationCaseService(IReconciliationCaseStore store) : IReconciliationCaseService
{
    public async Task<IReadOnlyList<ReconciliationCase>> CreateOpenCasesAsync(string importId, IReadOnlyList<MatchOutcome> outcomes, CancellationToken ct = default)
    {
        var cases = outcomes.Where(o => o.OutcomeType == "unmatched").Select(o => new ReconciliationCase(
            Guid.NewGuid().ToString("N"), importId, "Open", "Unmatched statement row", o.Confidence, o.Rationale, DateTimeOffset.UtcNow,
            [new ReconciliationCaseHistoryEntry(DateTimeOffset.UtcNow, "None", "Open", "Case created from matcher outcome")])).ToList();
        foreach (var c in cases) await store.SaveAsync(c, ct);
        return cases;
    }

    public async Task<ReconciliationCase> UpdateStatusAsync(string caseId, string toStatus, string note, CancellationToken ct = default)
    {
        var c = await store.GetAsync(caseId, ct) ?? throw new InvalidOperationException($"Case not found: {caseId}");
        var updated = c with
        {
            Status = toStatus,
            History = c.History.Concat([new ReconciliationCaseHistoryEntry(DateTimeOffset.UtcNow, c.Status, toStatus, note)]).ToList()
        };
        await store.SaveAsync(updated, ct);
        return updated;
    }

    public async Task<IReadOnlyList<ReconciliationCase>> ListOpenCasesAsync(CancellationToken ct = default)
        => (await store.ListAsync(ct)).Where(x => string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase)).ToList();
}
