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

export function buildWorkstationStatusItems({
  workspaceLabel,
  refreshing,
  hasError,
  providerTrust = null
}: {
  workspaceLabel: string;
  refreshing: boolean;
  hasError: boolean;
  providerTrust?: WorkstationStatusProviderTrust | null;
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

  return [
    {
      key: "sync",
      status: hasError ? "err" : refreshing ? "warn" : "ok",
      label: "Sync",
      value: hasError ? "attention required" : refreshing ? "refreshing..." : "up to date"
    },
    ...providerItems,
    {
      key: "workspace",
      label: "Workspace",
      value: workspaceLabel,
      push: true
    }
  ];
}
