namespace Meridian.Ledger;

/// <summary>
/// Holds multiple independent double-entry ledgers for a single project.
/// Each logical ledger can represent a different view such as actuals,
/// historical replay, parameterized P&amp;L, or contractual security-master flows.
/// </summary>
public sealed class ProjectLedgerBook
{
    private readonly Dictionary<LedgerBookKey, Ledger> _ledgers = [];

    public ProjectLedgerBook(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("Project identifier must not be null or whitespace.", nameof(projectId));

        ProjectId = projectId.Trim();
    }

    public string ProjectId { get; }

    public IReadOnlyCollection<LedgerBookKey> LedgerKeys
        => _ledgers.Keys
            .OrderBy(key => key.LedgerBook, StringComparer.OrdinalIgnoreCase)
            .ThenBy(key => key.LedgerView)
            .ThenBy(key => key.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public Ledger GetOrCreate(LedgerBookKey key)
    {
        var normalized = NormalizeKey(key);
        if (!_ledgers.TryGetValue(normalized, out var ledger))
        {
            ledger = new Ledger();
            _ledgers[normalized] = ledger;
        }

        return ledger;
    }

    public bool TryGetLedger(LedgerBookKey key, out Ledger? ledger)
        => _ledgers.TryGetValue(NormalizeKey(key), out ledger);

    public IReadOnlyDictionary<LedgerBookKey, IReadOnlyLedger> Snapshot()
        => _ledgers.ToDictionary(pair => pair.Key, pair => (IReadOnlyLedger)pair.Value);

    private LedgerBookKey NormalizeKey(LedgerBookKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var normalized = key.Normalize();
        if (!string.Equals(normalized.ProjectId, ProjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Ledger book project '{normalized.ProjectId}' does not match container project '{ProjectId}'.",
                nameof(key));
        }

        return normalized;
    }
}
