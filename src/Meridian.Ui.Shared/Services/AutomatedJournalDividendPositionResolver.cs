using System.Globalization;
using Meridian.Contracts.Domain;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>Named durable position source used by a recurring dividend schedule.</summary>
public sealed record AutomatedJournalPositionSnapshotScope(string RunId, string AccountId);

/// <summary>Authoritative position history resolved for one dividend cycle.</summary>
public sealed record AutomatedJournalDividendPositionResolution(
    IReadOnlyList<DividendAccrualPosition> Positions,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks,
    IReadOnlyList<string> Blockers)
{
    public bool IsReady => Blockers.Count == 0 && EvidenceLinks.Count > 0;
}

public interface IAutomatedJournalDividendPositionResolver
{
    Task<AutomatedJournalDividendPositionResolution> ResolveAsync(
        AutomatedJournalScheduleWorkItem workItem,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken ct = default);
}

/// <summary>
/// Loads exact-owner durable position history for a recurring dividend cycle. Each snapshot is
/// expanded with zero-quantity tombstones for symbols absent from later snapshots, allowing the
/// corporate-action producer to reconstruct the quantity actually held on each ex-date.
/// </summary>
public sealed class PositionSnapshotAutomatedJournalDividendPositionResolver
    : IAutomatedJournalDividendPositionResolver
{
    private readonly IPositionSnapshotStore? _snapshotStore;

    public PositionSnapshotAutomatedJournalDividendPositionResolver(IPositionSnapshotStore? snapshotStore)
    {
        _snapshotStore = snapshotStore;
    }

    public async Task<AutomatedJournalDividendPositionResolution> ResolveAsync(
        AutomatedJournalScheduleWorkItem workItem,
        DateTimeOffset evaluatedAtUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        evaluatedAtUtc = evaluatedAtUtc.ToUniversalTime();
        if (_snapshotStore is null)
            return Blocked("The durable position snapshot store is unavailable for recurring dividend capture.");
        if (workItem.PositionSnapshotScopes.Count == 0)
            return Blocked("Recurring dividend capture requires at least one named durable position-snapshot scope.");
        if (string.IsNullOrWhiteSpace(workItem.TenantId) ||
            string.IsNullOrWhiteSpace(workItem.CompanyId) ||
            string.IsNullOrWhiteSpace(workItem.FundProfileId) ||
            workItem.LedgerBookId == Guid.Empty ||
            string.IsNullOrWhiteSpace(workItem.EntityId))
        {
            return Blocked("Recurring dividend capture requires exact tenant, company, fund, ledger-book, and entity ownership.");
        }

        var owner = new PositionSnapshotOwnerScope(
            workItem.TenantId.Trim(),
            workItem.CompanyId.Trim(),
            workItem.FundProfileId.Trim(),
            workItem.LedgerBookId,
            workItem.EntityId.Trim());
        var periodStartUtc = new DateTimeOffset(workItem.PeriodStart.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var historyStartUtc = periodStartUtc.AddDays(-workItem.MaximumPositionAgeDays);
        var periodEndUtc = new DateTimeOffset(workItem.PeriodEnd.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var historyEndUtc = evaluatedAtUtc < periodEndUtc ? evaluatedAtUtc : periodEndUtc;
        if (historyEndUtc < periodStartUtc)
            return Blocked("The dividend cycle cannot resolve positions before its configured period begins.");

        var samples = new List<DividendAccrualPosition>();
        var evidence = new List<OperationsEvidenceLinkDto>();
        var blockers = new List<string>();
        foreach (var scope in workItem.PositionSnapshotScopes)
        {
            ct.ThrowIfCancellationRequested();
            var snapshots = new List<AccountSnapshotRecord>();
            await foreach (var snapshot in _snapshotStore.GetSnapshotHistoryAsync(
                               scope.RunId,
                               scope.AccountId,
                               owner,
                               historyStartUtc,
                               historyEndUtc,
                               ct))
            {
                snapshots.Add(snapshot);
            }

            snapshots = snapshots
                .Where(snapshot => IsOwnedBy(snapshot, owner))
                .Where(snapshot => string.Equals(snapshot.RunId, scope.RunId, StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals(snapshot.AccountId, scope.AccountId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(static snapshot => snapshot.AsOf)
                .ToList();
            if (snapshots.Count == 0)
            {
                blockers.Add($"No exact-owner position history exists for run '{scope.RunId}' and account '{scope.AccountId}' in the dividend-cycle lookback window.");
                continue;
            }

            var symbols = snapshots
                .SelectMany(static snapshot => snapshot.Positions)
                .Where(static position => !string.IsNullOrWhiteSpace(position.Symbol))
                .Select(static position => position.Symbol.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var snapshot in snapshots)
            {
                var route = BuildEvidenceRoute(scope, snapshot.AsOf);
                var snapshotEvidence = new JournalEvidenceReference(
                    EvidenceId: $"position-snapshot:{workItem.ScheduleId}:{scope.RunId}:{scope.AccountId}:{snapshot.AsOf.ToUnixTimeMilliseconds()}",
                    Uri: route,
                    Kind: "position-snapshot",
                    SourceSystem: "position-snapshot-store",
                    RetainedAtUtc: snapshot.AsOf,
                    RetainedBy: "automated-journal",
                    SubjectId: scope.AccountId);
                evidence.Add(new OperationsEvidenceLinkDto(
                    snapshotEvidence.EvidenceId,
                    "Authoritative ex-date position snapshot",
                    route,
                    snapshotEvidence.SourceSystem,
                    snapshot.AsOf));

                foreach (var symbol in symbols)
                {
                    var quantity = snapshot.Positions
                        .Where(position => string.Equals(position.Symbol?.Trim(), symbol, StringComparison.OrdinalIgnoreCase))
                        .Sum(static position => position.Quantity);
                    samples.Add(new DividendAccrualPosition(
                        symbol,
                        quantity,
                        FinancialAccountId: scope.AccountId,
                        PositionAsOfUtc: snapshot.AsOf,
                        PositionEvidenceReferences: [snapshotEvidence]));
                }
            }
        }

        return blockers.Count > 0
            ? new AutomatedJournalDividendPositionResolution([], evidence, blockers)
            : new AutomatedJournalDividendPositionResolution(
                samples
                    .DistinctBy(static position => string.Join(
                        '|',
                        position.Symbol,
                        position.FinancialAccountId,
                        position.PositionAsOfUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                        position.Quantity.ToString(CultureInfo.InvariantCulture)), StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                evidence
                    .DistinctBy(static link => $"{link.EvidenceId}|{link.Route}", StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                []);
    }

    private static bool IsOwnedBy(AccountSnapshotRecord snapshot, PositionSnapshotOwnerScope owner)
        => string.Equals(snapshot.TenantId, owner.TenantId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(snapshot.CompanyId, owner.CompanyId, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(snapshot.FundProfileId, owner.FundProfileId, StringComparison.OrdinalIgnoreCase) &&
           snapshot.LedgerBookId == owner.LedgerBookId &&
           string.Equals(snapshot.EntityId, owner.EntityId, StringComparison.OrdinalIgnoreCase);

    private static string BuildEvidenceRoute(
        AutomatedJournalPositionSnapshotScope scope,
        DateTimeOffset asOfUtc)
        => $"evidence://position-snapshots/{Uri.EscapeDataString(scope.RunId)}/{Uri.EscapeDataString(scope.AccountId)}?asOf={Uri.EscapeDataString(asOfUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))}";

    private static AutomatedJournalDividendPositionResolution Blocked(string blocker)
        => new([], [], [blocker]);
}
