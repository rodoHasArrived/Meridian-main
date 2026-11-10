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

/** The masthead's provider-trust posture, as the trust strip reports it. */
export interface WorkstationStatusProviderTrust {
  value: string;
  tone: "ready" | "review" | "blocked" | "pending";
}

/** The current page's freshness fact, as its posture strip reports it. */
export interface WorkstationStatusPageFreshness {
  tone: "ready" | "review" | "blocked" | "unknown";
  value: string | null;
}

const MAX_FRESHNESS_VALUE_LENGTH = 56;

export function buildWorkstationStatusItems({
  workspaceLabel,
  refreshing,
  hasError,
  providerTrust = null,
  pageFreshness = null
}: {
  workspaceLabel: string;
  refreshing: boolean;
  hasError: boolean;
  providerTrust?: WorkstationStatusProviderTrust | null;
  pageFreshness?: WorkstationStatusPageFreshness | null;
}): WorkstationStatusBarItem[] {
  // "Sync" only says the workstation's own read of the API is current. When provider trust needs
  // review or is blocked, the footer repeats that posture beside it so a green sync dot is never
  // the footer's whole answer while the masthead is warning. A ready or still-loading posture adds
  // nothing, which keeps the footer to sync and workspace in the normal case.
  const providerItems: WorkstationStatusBarItem[] =
    providerTrust && (providerTrust.tone === "review" || providerTrust.tone === "blocked")
      ? [{
        key: "providers",
        status: providerTrust.tone === "blocked" ? "err" : "warn",
        label: "Providers",
        value: providerTrust.value
      }]
      : [];

  // The page's posture strip owns the freshness answer. When it says the data on screen needs
  // review or is blocked, "Sync" drops to the narrower claim it can still make (the workstation
  // reached the service) and the footer repeats the strip's own words beside it.
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
    ...providerItems,
    {
      key: "workspace",
      label: "Workspace",
      value: workspaceLabel,
      push: true
    }
  ];
}

function truncateStatusValue(value: string): string {
  const trimmed = value.trim();
  return trimmed.length > MAX_FRESHNESS_VALUE_LENGTH
    ? `${trimmed.slice(0, MAX_FRESHNESS_VALUE_LENGTH - 1).trimEnd()}…`
    : trimmed;
}
