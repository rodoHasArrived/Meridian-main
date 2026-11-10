import { Link } from "react-router-dom";
import type { AppShellAttentionState } from "@/app-shell.view-model";
import { cn } from "@/lib/utils";

/**
 * The masthead's one attention signal: the worst open issue across the workstation, or
 * "All clear". It always opens Today, where every signal behind it is listed. The pill reads the
 * shell attention state — the same object the page status rows, Today and the footer read — so
 * it cannot disagree with them.
 */
export function AttentionPill({ attention }: { attention: AppShellAttentionState }) {
  return (
    <Link
      to={attention.href}
      className={cn(
        "masthead-status-pill focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40",
        `masthead-status-pill-${attention.tone}`
      )}
      aria-label={attention.ariaLabel}
      title={attention.clear ? "No open issues. Open Today." : `${attention.title}. Open Today.`}
      data-attention-tone={attention.tone}
    >
      <span className="masthead-status-pill-dot" aria-hidden="true" />
      <span className="masthead-status-pill-status">{attention.statusLabel}</span>
      {attention.clear ? null : <span className="masthead-status-pill-title">{attention.title}</span>}
      {attention.moreCount > 0 ? (
        <span className="masthead-status-pill-more" aria-hidden="true">+{attention.moreCount}</span>
      ) : null}
    </Link>
  );
}
