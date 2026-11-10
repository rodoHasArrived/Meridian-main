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

export function buildWorkstationStatusItems({
  workspaceLabel,
  refreshing,
  hasError,
  attention = null
}: {
  workspaceLabel: string;
  refreshing: boolean;
  hasError: boolean;
  attention?: WorkstationStatusAttention | null;
}): WorkstationStatusBarItem[] {
  // "Sync" only says the workstation's own read of the API is current. When the shell attention
  // state needs review or is blocked, the footer repeats the masthead pill's worst issue beside
  // it, so a green sync dot is never the footer's whole answer while the masthead is warning.
  // It reads the same attention state as the pill, so the two cannot disagree. A clear or
  // still-loading state adds nothing, which keeps the footer to sync and workspace normally.
  const attentionItems: WorkstationStatusBarItem[] =
    attention && (attention.tone === "review" || attention.tone === "blocked")
      ? [{
        key: "attention",
        status: attention.tone === "blocked" ? "err" : "warn",
        label: "Attention",
        value: attention.moreCount > 0 ? `${attention.title} +${attention.moreCount} more` : attention.title
      }]
      : [];

  return [
    {
      key: "sync",
      status: hasError ? "err" : refreshing ? "warn" : "ok",
      label: "Sync",
      value: hasError ? "attention required" : refreshing ? "refreshing..." : "up to date"
    },
    ...attentionItems,
    {
      key: "workspace",
      label: "Workspace",
      value: workspaceLabel,
      push: true
    }
  ];
}
