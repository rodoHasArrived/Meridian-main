import { useEffect, useId, useRef, type ReactNode } from "react";
import { ChevronDown } from "lucide-react";

/** A disclosure of existing view controls, not an ARIA menu of immediate commands.
 * Keep children mounted within a route so dialogs restore focus to their trigger.
 * Navigation disposes their overlay state before the closed disclosure can hide it.
 */
export function WorkspaceViewsMenu({ children, routeKey }: { children: ReactNode; routeKey?: string }) {
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
      <summary aria-controls={contentId}>
        Views <ChevronDown aria-hidden="true" className="size-4" />
      </summary>
      <div key={routeKey} id={contentId} role="region" aria-label="View controls" className="workstation-views-panel">
        {children}
      </div>
    </details>
  );
}
