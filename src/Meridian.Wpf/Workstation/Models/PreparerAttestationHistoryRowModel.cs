namespace Meridian.Wpf.Workstation.Models;

/// <summary>Presentation of retained assertions, distinct from evidence readiness and approval.</summary>
public sealed record PreparerAttestationHistoryRowModel(
    string ReceiptId,
    string Disclosure,
    string ActorAndTime,
    string Subject,
    string Hashes,
    string SourceProvenance,
    string CurrentStatus,
    string Reason);
