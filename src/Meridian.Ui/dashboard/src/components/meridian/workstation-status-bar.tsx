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

/** The slice of the shell attention state the footer repeats. */
export interface WorkstationStatusAttention {
  tone: "ready" | "review" | "blocked" | "pending";
  title: string;
  moreCount: number;
}

/** The current page's freshness fact, as its status row reports it. */
export interface WorkstationStatusPageFreshness {
  tone: "ready" | "review" | "blocked" | "unknown";
  value: string | null;
}

const MAX_FRESHNESS_VALUE_LENGTH = 56;

export function buildWorkstationStatusItems({
  refreshing,
  hasError,
  attention = null,
  pageFreshness = null
}: {
  /** Compatibility input: the shared header already presents the route name. */
  workspaceLabel: string;
  refreshing: boolean;
  hasError: boolean;
  attention?: WorkstationStatusAttention | null;
  pageFreshness?: WorkstationStatusPageFreshness | null;
}): WorkstationStatusBarItem[] {
  // "Sync" only says the workstation's own read of the API is current. When the shell attention
  // state needs review or is blocked, the footer repeats the masthead disclosure's worst issue beside
  // it, so a green sync dot is never the footer's whole answer while the masthead is warning.
  // It reads the same attention state as the masthead, so the two cannot disagree. A clear or
  // still-loading state adds nothing; route names and provider inventory stay in their existing
  // header/disclosure surfaces instead of repeating in the footer.
  const attentionItems: WorkstationStatusBarItem[] =
    attention && (attention.tone === "review" || attention.tone === "blocked")
      ? [{
        key: "attention",
        status: attention.tone === "blocked" ? "err" : "warn",
        label: "Attention",
        value: attention.moreCount > 0 ? `${attention.title} +${attention.moreCount} more` : attention.title
      }]
      : [];

  // The shell attention state covers shell-wide sources; the freshness of the data on the page
  // belongs to the page's status row. When that row says its freshness needs review or is
  // blocked, "Sync" drops to the narrower claim it can still make (the workstation reached the
  // service) and the footer repeats the row's own words, so it never says "up to date" over a
  // page that says "stale".
  const pageDataNeedsReview = pageFreshness?.tone === "review" || pageFreshness?.tone === "blocked";
  const freshnessItems: WorkstationStatusBarItem[] = pageDataNeedsReview
    ? [{
      key: "freshness",
      status: pageFreshness.tone === "blocked" ? "err" : "warn",
      label: "Freshness",
      value: truncateStatusValue(pageFreshness.value ?? (pageFreshness.tone === "blocked" ? "blocked" : "needs review"))
    }]
    : [];

  return [
    {
      key: "sync",
      status: hasError ? "err" : refreshing ? "warn" : "ok",
      label: "Sync",
      value: hasError
        ? "attention required"
        : refreshing
          ? "refreshing..."
          : pageDataNeedsReview
            ? "connected"
            : "up to date"
    },
    ...freshnessItems,
    ...attentionItems
  ];
}

function truncateStatusValue(value: string): string {
  const trimmed = value.trim();
  return trimmed.length > MAX_FRESHNESS_VALUE_LENGTH
    ? `${trimmed.slice(0, MAX_FRESHNESS_VALUE_LENGTH - 1).trimEnd()}…`
    : trimmed;
}
