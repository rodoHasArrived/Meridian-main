import { Button } from "@/components/ui/button";
import { useOnGlobalRefresh } from "@/lib/global-refresh";

/**
 * Takes the place of a panel's own "Refresh" button. The panel reloads whenever the operator
 * presses the header Refresh, and this renders nothing while the panel is healthy. After a failed
 * load it renders one "Retry" button, so a local control appears only inside an error state.
 *
 * Use it for a button that only re-reads data the panel already loads by itself. An action that
 * starts new work on the server (verify, synchronize, recalculate) keeps its own button and is
 * named for what it does.
 */
export function PanelReload({ reload, failed, busy = false, what, className }: {
  /** Re-reads the panel's data. Called on every header Refresh and by Retry. */
  reload: () => unknown;
  /** True while the panel's last load failed; the Retry button shows only then. */
  failed: boolean;
  busy?: boolean;
  /** What is being loaded, for the accessible name: "Retry loading {what}". */
  what: string;
  className?: string;
}) {
  useOnGlobalRefresh(reload);
  if (!failed) {
    return null;
  }
  return (
    <Button type="button" size="sm" variant="outline" className={className} disabled={busy} onClick={() => { void reload(); }} aria-label={`Retry loading ${what}`}>
      {busy ? "Retrying…" : "Retry"}
    </Button>
  );
}
