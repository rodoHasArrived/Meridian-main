import type { ReactNode } from "react";
import { cn } from "@/lib/utils";
import "@/styles/workspace-workbench-shell.css";

export interface WorkspaceWorkbenchShellProps {
  label: string;
  statusBand?: ReactNode;
  contextRail?: ReactNode;
  contextRailLabel?: string;
  children: ReactNode;
  contentLabel?: string;
  inspector?: ReactNode;
  inspectorLabel?: string;
  evidenceDrawer?: ReactNode;
  evidenceDrawerLabel?: string;
  className?: string;
  contentClassName?: string;
}

export function WorkspaceWorkbenchShell({
  label,
  statusBand,
  contextRail,
  contextRailLabel = "Workspace context",
  children,
  contentLabel,
  inspector,
  inspectorLabel = "Selected object inspector",
  evidenceDrawer,
  evidenceDrawerLabel = "Evidence drawer",
  className,
  contentClassName
}: WorkspaceWorkbenchShellProps) {
  const hasContext = Boolean(contextRail);
  const hasInspector = Boolean(inspector);

  return (
    <section className={cn("workspace-workbench-shell", className)} role="region" aria-label={label}>
      {statusBand ? (
        <section
          className="workspace-workbench-shell-status-band"
          role="region"
          aria-label={`${label} status`}
          data-workbench-slot="status"
        >
          {statusBand}
        </section>
      ) : null}

      <div
        className={cn(
          "workspace-workbench-shell-layout",
          hasContext && "workspace-workbench-shell-layout--with-context",
          hasInspector && "workspace-workbench-shell-layout--with-inspector"
        )}
      >
        {contextRail ? (
          <aside
            className="workspace-workbench-shell-context-rail"
            aria-label={contextRailLabel}
            data-workbench-slot="context"
          >
            {contextRail}
          </aside>
        ) : null}

        <section
          className={cn("workspace-workbench-shell-main", contentClassName)}
          role="region"
          aria-label={contentLabel ?? `${label} work surface`}
          data-workbench-slot="main"
        >
          {children}
        </section>

        {inspector ? (
          <aside
            className="workspace-workbench-shell-inspector"
            aria-label={inspectorLabel}
            data-workbench-slot="inspector"
          >
            {inspector}
          </aside>
        ) : null}
      </div>

      {evidenceDrawer ? (
        <section
          className="workspace-workbench-shell-evidence-drawer"
          role="region"
          aria-label={evidenceDrawerLabel}
          data-workbench-slot="evidence"
        >
          {evidenceDrawer}
        </section>
      ) : null}
    </section>
  );
}
