import type { KeyboardEvent, ReactNode } from "react";
import { cn } from "@/lib/utils";
import "@/styles/dense-row-detail-accessibility.css";

export const DENSE_ROW_DETAIL_KEYBOARD_INSTRUCTIONS =
  "Use Up Arrow and Down Arrow to move between rows. Use Home and End to jump to the first or last row. Use Enter or Space to select the focused row and move focus to its detail panel. Use Escape from the detail panel to return focus to the selected row.";

export const DENSE_ROW_DETAIL_ACCESSIBILITY_SPEC = {
  keyboardNavigation: [
    "Arrow Down moves focus and selection to the next row.",
    "Arrow Up moves focus and selection to the previous row.",
    "Home moves focus and selection to the first row.",
    "End moves focus and selection to the last row.",
    "Enter and Space select the focused row and hand focus to the controlled detail panel.",
    "Escape from a detail panel returns focus to the selected controlling row."
  ],
  focusHandoff: [
    "Row focus remains in the list while operators move with arrow keys, Home, or End.",
    "Activation keys hand focus to the labelled detail region after the row selection has been applied.",
    "Detail panels are programmatically focusable with tabIndex=-1 so focus can move without adding them to the tab sequence.",
    "Escape resolves aria-controls back to the currently selected row before falling back to the first controlling row."
  ],
  ariaRolesAndStates: [
    "The dense list remains a semantic table with labelled selectable rows.",
    "The active row exposes aria-selected=true and inactive rows omit aria-selected.",
    "Rows expose aria-controls pointing at their detail panel and aria-expanded to mirror the selected/expanded relationship.",
    "Detail panels are labelled regions or complementary asides with aria-live=polite updates."
  ],
  announcements: [
    "Selection changes are announced through a polite, atomic live region scoped to the table.",
    "Detail panel updates are announced by the labelled aria-live panel when its selected content changes."
  ]
} as const;

export interface DenseRowDetailPanelProps {
  id: string;
  ariaLabel: string;
  children: ReactNode;
  className?: string;
  role?: "region" | "complementary" | "status";
  ariaLive?: "polite" | "assertive" | "off";
  selectedSourceLabel?: string;
}

export function DenseRowDetailPanel({
  id,
  ariaLabel,
  children,
  className,
  role = "region",
  ariaLive = "polite",
  selectedSourceLabel = "Selected detail"
}: DenseRowDetailPanelProps) {
  return (
    <aside
      id={id}
      role={role}
      aria-label={ariaLabel}
      aria-live={ariaLive === "off" ? undefined : ariaLive}
      tabIndex={-1}
      data-dense-row-detail-panel="true"
      data-selected-source={selectedSourceLabel}
      className={className}
      onKeyDown={handleDenseRowDetailPanelKeyDown}
    >
      {children}
    </aside>
  );
}

export function buildDenseRowDetailAnnouncement(label: string): string {
  return `${label} selected. Detail panel updated.`;
}

/**
 * Returns true when it has taken over focus handling (scrolled the controlling
 * row back into view and focused it). Registered by virtualized dense tables so
 * Escape can return focus to a selected row that has been unmounted by windowing.
 */
export type DenseRowRevealHandler = () => boolean;

const denseRowRevealHandlers = new Map<string, DenseRowRevealHandler>();

/**
 * Register a reveal handler for a detail panel id. A virtualized dense table
 * uses this so that pressing Escape from the detail panel can bring a
 * scrolled-out selected row back into the window before focusing it. Returns an
 * unregister function; safe to call when the same panel id is re-registered.
 */
export function registerDenseRowRevealHandler(panelId: string, handler: DenseRowRevealHandler): () => void {
  denseRowRevealHandlers.set(panelId, handler);
  return () => {
    if (denseRowRevealHandlers.get(panelId) === handler) {
      denseRowRevealHandlers.delete(panelId);
    }
  };
}

export function focusDenseRowDetailPanel(panelId: string | undefined): void {
  if (!panelId || typeof document === "undefined") return;
  window.requestAnimationFrame(() => {
    document.getElementById(panelId)?.focus();
  });
}

function handleDenseRowDetailPanelKeyDown(event: KeyboardEvent<HTMLElement>) {
  if (event.key !== "Escape") return;

  const panelId = event.currentTarget.id;
  if (!panelId) return;

  // The selected controlling row, if it is currently mounted.
  const selectedController = document.querySelector<HTMLElement>(
    `[aria-controls="${escapeAttributeSelectorValue(panelId)}"][aria-selected="true"]`
  );
  if (selectedController) {
    event.preventDefault();
    selectedController.focus();
    return;
  }

  // The selected row is not mounted — a virtualized table has scrolled it out of
  // the window. A registered reveal handler owns the panel's reveal semantics
  // (it knows the selected row's index); prefer it over the generic fallback,
  // which would otherwise grab an unrelated windowed row that controls the same
  // panel.
  const revealer = denseRowRevealHandlers.get(panelId);
  if (revealer?.()) {
    event.preventDefault();
    return;
  }

  // Non-virtualized fallback: focus the first controlling row.
  const fallbackController = document.querySelector<HTMLElement>(
    `[aria-controls="${escapeAttributeSelectorValue(panelId)}"][data-selectable="true"]`
  );
  if (fallbackController) {
    event.preventDefault();
    fallbackController.focus();
  }
}

function escapeAttributeSelectorValue(value: string): string {
  return value.replace(/\\/g, "\\\\").replace(/"/g, "\\\"");
}

export function denseRowDetailPanelClassName(...classes: Array<string | undefined | false | null>) {
  return cn(...classes);
}
