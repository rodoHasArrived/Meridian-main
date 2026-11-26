import { cn } from "@/lib/utils";

export interface WorkstationStatusBarItem {
  key: string;
  label?: string;
  value: string;
  status?: "ok" | "warn" | "err";
  push?: boolean;
}

export interface WorkstationStatusBarProps {
  items: WorkstationStatusBarItem[];
}

/**
 * Concrete workstation status bar: the near-black telemetry footer shared by
 * the web shell and the design-system shell reference.
 */
export function WorkstationStatusBar({ items }: WorkstationStatusBarProps) {
  return (
    <footer className="workstation-statusbar" aria-label="Workstation status">
      {items.map((item) => (
        <span
          key={item.key}
          className={cn("workstation-statusbar-item", item.push && "workstation-statusbar-item-push")}
        >
          {item.status ? (
            <span className={`workstation-statusbar-dot workstation-statusbar-dot-${item.status}`} aria-hidden="true" />
          ) : null}
          {item.label ? <span className="workstation-statusbar-label">{item.label}</span> : null}
          <span className="workstation-statusbar-value">{item.value}</span>
        </span>
      ))}
    </footer>
  );
}

/** The current page's freshness fact, as its status row reports it. */
export interface WorkstationStatusPageFreshness {
  tone: "ready" | "review" | "blocked" | "unknown";
  value: string | null;
}

const MAX_FRESHNESS_VALUE_LENGTH = 56;

const FRESHNESS_RANK: Record<WorkstationStatusPageFreshness["tone"], number> = { unknown: 0, ready: 1, review: 2, blocked: 3 };

/** The worse of two freshness facts; an unknown fact never clears or outranks a reported one. */
export function worstStatusFreshness(
  ...facts: readonly (WorkstationStatusPageFreshness | null | undefined)[]
): WorkstationStatusPageFreshness | null {
  return facts.reduce<WorkstationStatusPageFreshness | null>((worst, fact) => {
    if (!fact) return worst;
    return !worst || FRESHNESS_RANK[fact.tone] > FRESHNESS_RANK[worst.tone] ? fact : worst;
  }, null);
}

export function buildWorkstationStatusItems({
  refreshing,
  hasError,
  pageFreshness = null,
  shellFreshness = null
}: {
  /** Compatibility input: the shared header already presents the route name. */
  workspaceLabel: string;
  refreshing: boolean;
  hasError: boolean;
  pageFreshness?: WorkstationStatusPageFreshness | null;
  /**
   * The freshness of the sources the whole workstation depends on, from the same check as the
   * masthead pill (`resolveShellFreshness`). A page can add to it but never clear it.
   */
  shellFreshness?: WorkstationStatusPageFreshness | null;
}): WorkstationStatusBarItem[] {
  // The footer holds connectivity and freshness facts only (UX consolidation P1). What needs
  // attention is owned by the masthead trust strip, which opens the queue; repeating its worst
  // issue here put the same item on screen three or four times and taught operators to ignore it.
  // Freshness is the worse of the shell's (the sources every screen depends on, judged by the
  // masthead pill's rule) and the page's own status row. "Up to date" is said only when every
  // reported source is current; otherwise "Sync" drops to the narrower claim it can still make
  // (the workstation reached the service) and the footer repeats the worst source's own words.
  const freshness = worstStatusFreshness(shellFreshness, pageFreshness);
  const needsReview = freshness?.tone === "review" || freshness?.tone === "blocked";
  // With shell freshness supplied the footer always states freshness, so silence never reads as
  // "fine"; a caller with neither fact keeps the sync-only footer.
  const freshnessItems: WorkstationStatusBarItem[] = needsReview
    ? [{
      key: "freshness",
      status: freshness.tone === "blocked" ? "err" : "warn",
      label: "Freshness",
      value: truncateStatusValue(freshness.value ?? (freshness.tone === "blocked" ? "blocked" : "needs review"))
    }]
    : shellFreshness
      ? [{
        key: "freshness",
        status: freshness?.tone === "ready" ? "ok" : "warn",
        label: "Freshness",
        value: freshness?.tone === "ready" ? "current" : "not yet verified"
      }]
      : [];
  const current = shellFreshness ? freshness?.tone === "ready" : !needsReview;

  return [
    {
      key: "sync",
      status: hasError ? "err" : refreshing ? "warn" : "ok",
      label: "Sync",
      value: hasError
        ? "attention required"
        : refreshing
          ? "refreshing..."
          : current
            ? "up to date"
            : "connected"
    },
    ...freshnessItems
  ];
}

function truncateStatusValue(value: string): string {
  const trimmed = value.trim();
  return trimmed.length > MAX_FRESHNESS_VALUE_LENGTH
    ? `${trimmed.slice(0, MAX_FRESHNESS_VALUE_LENGTH - 1).trimEnd()}…`
    : trimmed;
}
