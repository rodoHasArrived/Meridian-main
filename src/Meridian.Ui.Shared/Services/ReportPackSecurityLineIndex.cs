using Meridian.Contracts.Workstation;
using Meridian.Application.Composition;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// One report pack's contribution to a security in the security→report-line index: which report (and the
/// fund/state it is in) references <see cref="SecurityId"/>, and on which report lines.
/// </summary>
public sealed record ReportPackSecurityLineIndexEntry(
    Guid SecurityId,
    Guid ReportId,
    string FundProfileId,
    ReportPackWorkflowStateDto State,
    IReadOnlyList<string> LineKeys);

/// <summary>
/// Durable-by-derivation index from a security id to the report packs whose retained report-line
/// provenance references it. It replaces the per-fund linear scan in
/// <see cref="ReportPackRestatementCandidateResolver"/> with an O(matches) lookup, and exposes a
/// cross-fund <see cref="Lookup"/> seam (consumed internally for now; a public cross-fund surface is a
/// later slice). The index is a derived cache: it is always rebuildable from the report-pack workflow
/// records (the source of truth), so it carries no independent persistence.
/// </summary>
public interface IReportPackSecurityLineIndex
{
    /// <summary>Recomputes <paramref name="record"/>'s contributions, replacing any prior entries for it.</summary>
    void Upsert(ReportPackWorkflowRecordDto record);

    /// <summary>Removes all index entries contributed by <paramref name="reportId"/>.</summary>
    void RemoveReport(Guid reportId);

    /// <summary>Clears and rebuilds the index from <paramref name="records"/> (startup backfill / self-heal).</summary>
    void Rebuild(IEnumerable<ReportPackWorkflowRecordDto> records);

    /// <summary>Entries for a security scoped to a single fund profile.</summary>
    IReadOnlyList<ReportPackSecurityLineIndexEntry> LookupByFund(Guid securityId, string fundProfileId);

    /// <summary>All entries for a security across every fund (cross-fund seam).</summary>
    IReadOnlyList<ReportPackSecurityLineIndexEntry> Lookup(Guid securityId);
}

/// <summary>
/// In-memory <see cref="IReportPackSecurityLineIndex"/>. Kept current by routing every report-pack
/// mutation through a single chokepoint in <see cref="ReportPackWorkflowService"/>, and rebuilt from the
/// persisted workflow records on construction. A single lock guards both maps so an <see cref="Upsert"/>
/// (remove-then-add) is atomic against concurrent lookups.
/// </summary>
[ProductionSafeImplementation(
    "This is a derived cache rebuilt from persisted report-pack workflow records and carries no source-of-truth state.")]
public sealed class InMemoryReportPackSecurityLineIndex : IReportPackSecurityLineIndex
{
    private readonly object _gate = new();

    // securityId -> (reportId -> entry). Nested by report so a re-upsert replaces a report's entry in place.
    private readonly Dictionary<Guid, Dictionary<Guid, ReportPackSecurityLineIndexEntry>> _bySecurity = new();

    // reportId -> the security ids it currently contributes, so a re-upsert can remove stale contributions.
    private readonly Dictionary<Guid, IReadOnlyList<Guid>> _reportSecurities = new();

    public void Upsert(ReportPackWorkflowRecordDto record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            UpsertLocked(record);
        }
    }

    // Recomputes a record's contributions. The caller MUST hold _gate, so a Rebuild can apply many
    // upserts atomically (the index is never observed half-rebuilt by a concurrent lookup).
    private void UpsertLocked(ReportPackWorkflowRecordDto record)
    {
        var perSecurityLineKeys = new Dictionary<Guid, List<string>>();
        foreach (var line in record.LineProvenance ?? [])
        {
            foreach (var securityId in ReportPackSecurityLineMatcher.ReferencedSecurityIds(line))
            {
                if (!perSecurityLineKeys.TryGetValue(securityId, out var keys))
                {
                    keys = [];
                    perSecurityLineKeys[securityId] = keys;
                }

                // Register the security even when the contributing line has no key; only collect
                // non-empty keys so the Ordinal Distinct below never hashes a null line key.
                if (!string.IsNullOrEmpty(line.LineKey))
                {
                    keys.Add(line.LineKey);
                }
            }
        }

        RemoveReportInternal(record.ReportId);

        if (perSecurityLineKeys.Count == 0)
        {
            return;
        }

        foreach (var (securityId, lineKeys) in perSecurityLineKeys)
        {
            var entry = new ReportPackSecurityLineIndexEntry(
                securityId,
                record.ReportId,
                record.FundProfileId,
                record.State,
                lineKeys.Distinct(StringComparer.Ordinal).ToArray());

            if (!_bySecurity.TryGetValue(securityId, out var byReport))
            {
                byReport = [];
                _bySecurity[securityId] = byReport;
            }

            byReport[record.ReportId] = entry;
        }

        _reportSecurities[record.ReportId] = perSecurityLineKeys.Keys.ToArray();
    }

    public void RemoveReport(Guid reportId)
    {
        lock (_gate)
        {
            RemoveReportInternal(reportId);
        }
    }

    public void Rebuild(IEnumerable<ReportPackWorkflowRecordDto> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        // Clear and re-apply under a single lock so a concurrent lookup never observes a partially
        // rebuilt (or momentarily empty) index.
        lock (_gate)
        {
            _bySecurity.Clear();
            _reportSecurities.Clear();
            foreach (var record in records)
            {
                ArgumentNullException.ThrowIfNull(record);
                UpsertLocked(record);
            }
        }
    }

    public IReadOnlyList<ReportPackSecurityLineIndexEntry> Lookup(Guid securityId)
    {
        lock (_gate)
        {
            return _bySecurity.TryGetValue(securityId, out var byReport)
                ? byReport.Values.ToArray()
                : [];
        }
    }

    public IReadOnlyList<ReportPackSecurityLineIndexEntry> LookupByFund(Guid securityId, string fundProfileId)
    {
        var fund = fundProfileId?.Trim();
        if (string.IsNullOrEmpty(fund))
        {
            return [];
        }

        lock (_gate)
        {
            if (!_bySecurity.TryGetValue(securityId, out var byReport))
            {
                return [];
            }

            return byReport.Values
                .Where(entry => string.Equals(entry.FundProfileId, fund, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    // Must be called under _gate.
    private void RemoveReportInternal(Guid reportId)
    {
        if (!_reportSecurities.TryGetValue(reportId, out var securityIds))
        {
            return;
        }

        foreach (var securityId in securityIds)
        {
            if (_bySecurity.TryGetValue(securityId, out var byReport))
            {
                byReport.Remove(reportId);
                if (byReport.Count == 0)
                {
                    _bySecurity.Remove(securityId);
                }
            }
        }

        _reportSecurities.Remove(reportId);
    }
}
