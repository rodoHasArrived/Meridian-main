import { Link } from "react-router-dom";
import { cn } from "@/lib/utils";
import type { AppShellDecisionBrief } from "@/app-shell.workflow-continuity-types";

/**
 * Compact masthead handle for the cross-workspace decision brief. The brief's
 * detail and queue live behind its action route (break queue / Daily Control
 * Tower); the shell keeps only this always-visible pill so one blocked item
 * does not repaint every workspace with a banner.
 */
export function DecisionBriefPill({ brief }: { brief: AppShellDecisionBrief }) {
  return (
    <Link
      to={brief.actionHref}
      className={cn(
        "masthead-status-pill focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
        `masthead-status-pill-${brief.statusTone}`
      )}
      aria-label={`${brief.label}: ${brief.title}. Status ${brief.statusLabel}. ${brief.actionLabel}.`}
      title={`${brief.title} — ${brief.reason}`}
    >
      <span className="masthead-status-pill-dot" aria-hidden="true" />
      <span className="masthead-status-pill-status">{brief.statusLabel}</span>
      <span className="masthead-status-pill-title">{brief.title}</span>
    </Link>
  );
}
