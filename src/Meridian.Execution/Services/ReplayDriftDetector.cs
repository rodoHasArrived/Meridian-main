namespace Meridian.Execution.Services;

public enum ReplayDriftStatus
{
    Unknown = 0,
    Aligned = 1,
    DriftDetected = 2
}

public sealed record ReplayDriftAssessment(
    ReplayDriftStatus Status,
    bool IsDrifted,
    string RequiredNextAction,
    string Detail);

public static class ReplayDriftDetector
{
    public static ReplayDriftAssessment Assess(
        string activeSessionId,
        int activeFillCount,
        int activeOrderCount,
        int activeLedgerEntryCount,
        string? verifiedSessionId,
        int verifiedFillCount,
        int verifiedOrderCount,
        int verifiedLedgerEntryCount)
    {
        if (string.IsNullOrWhiteSpace(activeSessionId) || string.IsNullOrWhiteSpace(verifiedSessionId))
        {
            return new ReplayDriftAssessment(
                ReplayDriftStatus.Unknown,
                true,
                "Run replay verification.",
                "Replay verification evidence is missing for the active paper session.");
        }

        var mismatch = !string.Equals(activeSessionId, verifiedSessionId, StringComparison.OrdinalIgnoreCase) ||
                       activeFillCount != verifiedFillCount ||
                       activeOrderCount != verifiedOrderCount ||
                       activeLedgerEntryCount != verifiedLedgerEntryCount;

        return mismatch
            ? new ReplayDriftAssessment(
                ReplayDriftStatus.DriftDetected,
                true,
                "Run replay verification again to refresh the audit marker.",
                $"Replay verification for paper session {activeSessionId} is stale (fills active={activeFillCount}, verified={verifiedFillCount}; orders active={activeOrderCount}, verified={verifiedOrderCount}; ledger active={activeLedgerEntryCount}, verified={verifiedLedgerEntryCount}).")
            : new ReplayDriftAssessment(
                ReplayDriftStatus.Aligned,
                false,
                "No action required.",
                "Replay verification is aligned with active paper-session counts.");
    }
}
