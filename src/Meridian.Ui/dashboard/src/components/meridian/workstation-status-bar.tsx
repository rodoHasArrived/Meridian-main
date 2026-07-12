import { cn } from "@/lib/utils";
import type { SessionInfo } from "@/types";

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

export function buildWorkstationStatusItems({
  session,
  workspaceLabel,
  usingDevelopmentFixtures,
  refreshing,
  hasError
}: {
  session: Pick<SessionInfo, "environment"> | null;
  workspaceLabel: string;
  usingDevelopmentFixtures: boolean;
  refreshing: boolean;
  hasError: boolean;
}): WorkstationStatusBarItem[] {
  const environment = session?.environment ?? "loading";
  const connectionStatus: WorkstationStatusBarItem["status"] = hasError ? "err" : session ? "ok" : "warn";
  const dataStatus: WorkstationStatusBarItem["status"] = usingDevelopmentFixtures ? "warn" : "ok";

  return [
    {
      key: "session",
      status: connectionStatus,
      label: "Session",
      value: session ? environment : "connecting"
    },
    {
      key: "data",
      status: dataStatus,
      label: "Data",
      value: usingDevelopmentFixtures ? "demo fixtures" : "live source"
    },
    {
      key: "sync",
      label: "Sync",
      value: refreshing ? "refreshing..." : "up to date"
    },
    {
      key: "workspace",
      label: "Workspace",
      value: workspaceLabel,
      push: true
    }
  ];
}
