import { ExternalLink } from "lucide-react";
import { Button } from "@/components/ui/button";
import { companionPaneHref, openCompanionPane, type PaneOpener } from "@/lib/companion-pane/pane-window";

export interface PopOutPaneButtonProps {
  paneId: string;
  label?: string;
  /** Injectable opener for tests. */
  opener?: PaneOpener;
}

/**
 * Open a workspace surface in a companion window. Renders a button that calls
 * `window.open` (with `noopener`) plus a persistent, always-visible fallback
 * link. Because a `noopener` open returns null even on success, we never try to
 * detect a blocked popup — the fallback link is the reliable path in.
 */
export function PopOutPaneButton({ paneId, label = "Pop out", opener = window }: PopOutPaneButtonProps) {
  return (
    <span className="pop-out-pane">
      <Button
        type="button"
        size="sm"
        variant="outline"
        onClick={() => openCompanionPane(paneId, opener)}
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
      >
        Open in new tab
      </a>
    </span>
  );
}
