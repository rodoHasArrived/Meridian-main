import { useEffect, useId, useRef, type ReactNode } from "react";
import { ChevronDown, MoreHorizontal } from "lucide-react";

interface HeaderDisclosureMenuProps {
  children: ReactNode;
  routeKey?: string;
  summary: ReactNode;
  summaryAriaLabel?: string;
  panelLabel: string;
  /** Close after a button inside the panel is activated (one-shot commands such as Copy link). */
  closeOnAction?: boolean;
}

/** A disclosure of existing header controls, not an ARIA menu of immediate commands.
 * Keep children mounted within a route so dialogs restore focus to their trigger.
 * Navigation disposes their overlay state before the closed disclosure can hide it.
 */
function HeaderDisclosureMenu({ children, routeKey, summary, summaryAriaLabel, panelLabel, closeOnAction = false }: HeaderDisclosureMenuProps) {
  const detailsRef = useRef<HTMLDetailsElement>(null);
  const contentId = useId();

  useEffect(() => {
    if (detailsRef.current) detailsRef.current.open = false;
  }, [routeKey]);

  useEffect(() => {
    const onPointerDown = (event: PointerEvent) => {
      const details = detailsRef.current;
      if (details?.open && event.target instanceof Node && !details.contains(event.target)) {
        details.open = false;
      }
    };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, []);

  return (
    <details
      ref={detailsRef}
      className="workstation-views-menu"
      onKeyDown={(event) => {
        // Nested dialogs own Escape and restore focus to their own controls first.
        if (event.key !== "Escape" || (event.target as Element).closest('[role="dialog"]')) {
          return;
        }
        event.preventDefault();
        if (detailsRef.current) {
          detailsRef.current.open = false;
          detailsRef.current.querySelector("summary")?.focus();
        }
      }}
    >
      <summary aria-controls={contentId} aria-label={summaryAriaLabel} title={summaryAriaLabel}>
        {summary}
      </summary>
      <div
        key={routeKey}
        id={contentId}
        role="region"
        aria-label={panelLabel}
        className="workstation-views-panel"
        onClick={closeOnAction ? (event) => {
          if ((event.target as Element).closest("button") && detailsRef.current) {
            detailsRef.current.open = false;
          }
        } : undefined}
      >
        {children}
      </div>
    </details>
  );
}

/** Saved-layout and save-current-view controls. */
export function WorkspaceViewsMenu({ children, routeKey }: { children: ReactNode; routeKey?: string }) {
  return (
    <HeaderDisclosureMenu
      routeKey={routeKey}
      panelLabel="View controls"
      summary={<>Views <ChevronDown aria-hidden="true" className="size-4" /></>}
    >
      {children}
    </HeaderDisclosureMenu>
  );
}

/** Rarely used header actions (Copy link, About this workspace) behind one overflow control. */
export function WorkspaceOverflowMenu({ children, routeKey }: { children: ReactNode; routeKey?: string }) {
  return (
    <HeaderDisclosureMenu
      routeKey={routeKey}
      panelLabel="More actions"
      summaryAriaLabel="More actions"
      summary={<MoreHorizontal aria-hidden="true" className="size-4" />}
      closeOnAction
    >
      {children}
    </HeaderDisclosureMenu>
  );
}
