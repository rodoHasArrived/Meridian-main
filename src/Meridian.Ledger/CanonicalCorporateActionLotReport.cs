using Meridian.Contracts.Accounting.Lots;

namespace Meridian.Ledger;

/// <summary>Retained accounting proof, reconstructed from the atomic mutation snapshots rather than today's lots.</summary>
public sealed record CanonicalCorporateActionLotReport(
    Guid CorporateActionId,
    Guid JournalEntryId,
    OpenLotDto PredecessorBefore,
    OpenLotDto PredecessorAfter,
    IReadOnlyList<OpenLotDto> Successors);

