import { useId, useRef, useState, type ReactNode } from "react";
import { Button } from "@/components/ui/button";
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
  /** Opt in to queue/inspector switching on constrained desktop widths. */
  switchInspector?: boolean;
  inspectorRole?: "complementary" | "region";
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
  switchInspector = false,
  inspectorRole = "complementary",
  evidenceDrawer,
  evidenceDrawerLabel = "Evidence drawer",
  className,
  contentClassName
}: WorkspaceWorkbenchShellProps) {
  const [detailOpen, setDetailOpen] = useState(false);
  const inspectorId = useId();
  const mainRef = useRef<HTMLElement>(null);
  const inspectorRef = useRef<HTMLElement>(null);
  const returnFocus = useRef<HTMLElement | null>(null);
  const openInspector = () => {
    returnFocus.current = mainRef.current?.querySelector<HTMLElement>('tr[aria-selected="true"]') ?? null;
    setDetailOpen(true);
    requestAnimationFrame(() => {
      const panel = inspectorRef.current?.querySelector<HTMLElement>('[data-dense-row-detail-panel], [tabindex="-1"]');
      (panel ?? inspectorRef.current)?.focus();
    });
  };
  const closeInspector = () => {
    setDetailOpen(false);
    requestAnimationFrame(() => {
      const target = returnFocus.current?.isConnected ? returnFocus.current : mainRef.current;
      target?.focus();
    });
  };
  const hasContext = Boolean(contextRail);
  const hasInspector = Boolean(inspector);

  return (
    <section className={cn("workspace-workbench-shell", switchInspector && "workspace-workbench-shell--switchable", detailOpen && "workspace-workbench-shell--detail-open", className)} role="region" aria-label={label}>
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

      {switchInspector && hasInspector ? (
        <div className="workspace-workbench-inspect-toggle">
          <Button type="button" size="sm" variant="outline" aria-controls={inspectorId} aria-expanded={detailOpen} onClick={detailOpen ? closeInspector : openInspector}>
            {detailOpen ? "Back to work list" : "Inspect selection"}
          </Button>
        </div>
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
          ref={mainRef}
          tabIndex={-1}
          onKeyDownCapture={(event) => {
            if (!switchInspector || !hasInspector || !window.matchMedia?.("(max-width: 1279px)").matches) return;
            if ((event.key === "Enter" || event.key === " ") && event.target instanceof HTMLElement && event.target.matches('tr[aria-controls]')) {
              // DenseDataTable owns selection and focus handoff; reveal its target first.
              returnFocus.current = event.target;
              setDetailOpen(true);
            }
          }}
          className={cn("workspace-workbench-shell-main", contentClassName)}
          role="region"
          aria-label={contentLabel ?? `${label} work surface`}
          data-workbench-slot="main"
        >
          {children}
        </section>

        {inspector ? (
          <aside
            id={inspectorId}
            ref={inspectorRef}
            tabIndex={-1}
            onKeyDownCapture={(event) => {
              const target = event.target;
              if (!switchInspector || !detailOpen || event.key !== "Escape" || !(target instanceof HTMLElement)) return;
              // Portalled dialogs and nested popup controls own their Escape key.
              if (!event.currentTarget.contains(target) || target.closest('[role="dialog"], [role="menu"], [role="listbox"], select')) return;
              // Restore focus after the queue is visible; the dense panel may also
              // attempt an immediate handoff while React is still committing.
              closeInspector();
            }}
            className="workspace-workbench-shell-inspector"
            role={inspectorRole}
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
