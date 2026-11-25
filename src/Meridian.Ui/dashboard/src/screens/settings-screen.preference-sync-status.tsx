// One status line for the operator presentation preferences on Settings > Preferences: whether the
// current set reached the host, so it follows the operator to other browsers and the desktop app,
// or is still only in this browser. It never claims a host save that did not happen.
import { useSyncExternalStore } from "react";
import { Button } from "@/components/ui/button";
import { TimeText } from "@/components/ui/time-text";
import {
  getOperatorPreferenceSyncStatus,
  retryOperatorPreferenceSync,
  subscribeOperatorPreferenceSyncStatus,
  type OperatorPreferenceSyncStatus
} from "@/lib/operator-preference-events";

export function useOperatorPreferenceSyncStatus(): OperatorPreferenceSyncStatus {
  return useSyncExternalStore(
    subscribeOperatorPreferenceSyncStatus,
    getOperatorPreferenceSyncStatus,
    getOperatorPreferenceSyncStatus
  );
}

export function PreferenceSyncStatus({ className }: { className?: string }) {
  const status = useOperatorPreferenceSyncStatus();
  return (
    <div
      role="status"
      aria-label="Preference sync status"
      aria-live="polite"
      data-sync-state={status.state}
      className={["mt-2 flex flex-wrap items-center gap-2 text-xs text-muted-foreground", className].filter(Boolean).join(" ")}
    >
      <PreferenceSyncStatusText status={status} />
      {status.state === "failed" ? (
        <Button type="button" variant="outline" size="sm" onClick={() => retryOperatorPreferenceSync()}>
          Retry now
        </Button>
      ) : null}
    </div>
  );
}

function PreferenceSyncStatusText({ status }: { status: OperatorPreferenceSyncStatus }) {
  switch (status.state) {
    case "synced":
      return (
        <span>
          Saved to your profile
          {status.lastSavedAt ? (
            <>
              {" · "}
              <TimeText value={status.lastSavedAt} display="relative" />
            </>
          ) : null}
        </span>
      );
    case "saving":
      return <span>Saving to your profile…</span>;
    case "loading":
      return <span>Loading your saved preferences…</span>;
    case "failed":
      return (
        <span className="text-warning">
          {status.reason === "load"
            ? "Only in this browser — your saved preferences could not be read from the host."
            : "Only in this browser — the host could not be reached; changes will be retried."}
        </span>
      );
    case "local":
    default:
      return <span>Only in this browser — this session is not connected to a host.</span>;
  }
}
