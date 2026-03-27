namespace Meridian.Ledger;

/// <summary>
/// A fund-structure-aware ledger that organises independent double-entry
/// sub-ledgers by entity, sleeve, and vehicle boundaries, backed by
/// <see cref="ProjectLedgerBook"/>.
/// </summary>
public sealed class FundLedgerBook
{
    private readonly ProjectLedgerBook _inner;

    public FundLedgerBook(string fundId)
    {
        if (string.IsNullOrWhiteSpace(fundId))
            throw new ArgumentException("Fund identifier must not be blank.", nameof(fundId));

        FundId = fundId.Trim();
        _inner = new ProjectLedgerBook(FundId);
    }

    public string FundId { get; }

    // ── Ledger accessors ───────────────────────────────────────────────────────

    /// <summary>Gets (or lazily creates) the top-level fund ledger.</summary>
    public Ledger FundLedger =>
        _inner.GetOrCreate(new LedgerBookKey(FundId, "Fund"));

    /// <summary>Gets (or lazily creates) the ledger for a named entity.</summary>
    public Ledger EntityLedger(string entityId) =>
        _inner.GetOrCreate(new LedgerBookKey(FundId, $"Entity:{entityId}"));

    /// <summary>Gets (or lazily creates) the ledger for a named sleeve.</summary>
    public Ledger SleeveLedger(string sleeveId) =>
        _inner.GetOrCreate(new LedgerBookKey(FundId, $"Sleeve:{sleeveId}"));

    /// <summary>Gets (or lazily creates) the ledger for a named vehicle.</summary>
    public Ledger VehicleLedger(string vehicleId) =>
        _inner.GetOrCreate(new LedgerBookKey(FundId, $"Vehicle:{vehicleId}"));

    // ── Consolidated views ─────────────────────────────────────────────────────

    /// <summary>Consolidated trial balance across all sub-ledgers in this fund.</summary>
    public IReadOnlyDictionary<LedgerAccount, decimal> ConsolidatedTrialBalance() =>
        _inner.ConsolidatedTrialBalance();

    /// <summary>Consolidated point-in-time snapshot across all sub-ledgers.</summary>
    public LedgerSnapshot ConsolidatedSnapshotAsOf(DateTimeOffset asOf) =>
        _inner.ConsolidatedSnapshotAsOf(asOf);

    /// <summary>All journal entries across all sub-ledgers, ordered by timestamp.</summary>
    public IReadOnlyList<JournalEntry> ConsolidatedJournalEntries() =>
        _inner.ConsolidatedJournalEntries();

    // ── Per-dimension snapshots ────────────────────────────────────────────────

    /// <summary>Point-in-time snapshots keyed by entity identifier.</summary>
    public IReadOnlyDictionary<string, LedgerSnapshot> EntitySnapshotsAsOf(DateTimeOffset asOf)
        => SnapshotsByPrefix("Entity:", asOf);

    /// <summary>Point-in-time snapshots keyed by sleeve identifier.</summary>
    public IReadOnlyDictionary<string, LedgerSnapshot> SleeveSnapshotsAsOf(DateTimeOffset asOf)
        => SnapshotsByPrefix("Sleeve:", asOf);

    /// <summary>Point-in-time snapshots keyed by vehicle identifier.</summary>
    public IReadOnlyDictionary<string, LedgerSnapshot> VehicleSnapshotsAsOf(DateTimeOffset asOf)
        => SnapshotsByPrefix("Vehicle:", asOf);

    // ── Reconciliation snapshot ────────────────────────────────────────────────

    /// <summary>
    /// Returns a reconciliation-friendly snapshot that includes the consolidated
    /// fund view together with per-entity, per-sleeve, and per-vehicle breakdowns.
    /// </summary>
    public FundLedgerSnapshot ReconciliationSnapshot(DateTimeOffset asOf) =>
        new FundLedgerSnapshot(
            FundId: FundId,
            AsOf: asOf,
            Consolidated: ConsolidatedSnapshotAsOf(asOf),
            Entities: EntitySnapshotsAsOf(asOf),
            Sleeves: SleeveSnapshotsAsOf(asOf),
            Vehicles: VehicleSnapshotsAsOf(asOf));

    // ── Private helpers ────────────────────────────────────────────────────────

    private Dictionary<string, LedgerSnapshot> SnapshotsByPrefix(string prefix, DateTimeOffset asOf)
    {
        var result = new Dictionary<string, LedgerSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, ledger) in _inner.FilteredSnapshot())
        {
            if (!key.LedgerBook.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var dimensionId = key.LedgerBook[prefix.Length..];
            result[dimensionId] = ((Ledger)ledger).SnapshotAsOf(asOf);
        }
        return result;
    }
}

/// <summary>
/// A point-in-time reconciliation snapshot for an entire fund structure,
/// including consolidated and per-dimension breakdowns.
/// </summary>
public sealed record FundLedgerSnapshot(
    string FundId,
    DateTimeOffset AsOf,
    LedgerSnapshot Consolidated,
    IReadOnlyDictionary<string, LedgerSnapshot> Entities,
    IReadOnlyDictionary<string, LedgerSnapshot> Sleeves,
    IReadOnlyDictionary<string, LedgerSnapshot> Vehicles);
