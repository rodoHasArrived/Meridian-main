import { ExternalLink } from "lucide-react";
import { Button } from "@/components/ui/button";
import { COMPANION_PANES, companionPaneHref, openCompanionPane, type PaneOpener } from "@/lib/companion-pane/pane-window";
import { recordCompanionPaneOpened } from "@/lib/companion-pane/open-registry";

export interface PopOutPaneButtonProps {
  paneId: string;
  label?: string;
  /** Injectable opener for tests. */
  opener?: PaneOpener;
  /** Injectable pop-out recorder for tests; records intent for saved layouts. */
  recordOpen?: (paneId: string) => void;
}

/**
 * Open a workspace surface in a companion window. Renders a button that calls
 * `window.open` (with `noopener`) plus a persistent, always-visible fallback
 * link. Because a `noopener` open returns null even on success, we never try to
 * detect a blocked popup — the fallback link is the reliable path in.
 *
 * Both entry points record the pane as popped out so a saved layout can capture
 * and later re-open it (see lib/companion-pane/open-registry.ts).
 */
export function PopOutPaneButton({
  paneId,
  label = "Pop out",
  opener = window,
  recordOpen = recordCompanionPaneOpened
}: PopOutPaneButtonProps) {
  const paneTitle = COMPANION_PANES[paneId]?.title ?? paneId;
  return (
    <span className="pop-out-pane">
      <Button
        type="button"
        size="sm"
        variant="outline"
        onClick={() => {
          openCompanionPane(paneId, opener);
          recordOpen(paneId);
        }}
        aria-label={`${label} in a companion window`}
      >
        <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
        <span>{label}</span>
      </Button>
      <a
        className="pop-out-pane-fallback"
        href={companionPaneHref(paneId)}
        target="_blank"
        rel="noopener noreferrer"
        onClick={() => recordOpen(paneId)}
        aria-label={`Open ${paneTitle} in a new tab`}
      >
        Open in new tab
      </a>
    </span>
  );
}
