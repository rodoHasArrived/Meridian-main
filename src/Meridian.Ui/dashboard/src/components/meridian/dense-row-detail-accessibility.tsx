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

  const selectedController = document.querySelector<HTMLElement>(
    `[aria-controls="${escapeAttributeSelectorValue(panelId)}"][aria-selected="true"]`
  );
  const fallbackController = document.querySelector<HTMLElement>(
    `[aria-controls="${escapeAttributeSelectorValue(panelId)}"][data-selectable="true"]`
  );
  const controller = selectedController ?? fallbackController;

  if (!controller) return;

  event.preventDefault();
  controller.focus();
}

function escapeAttributeSelectorValue(value: string): string {
  return value.replace(/\\/g, "\\\\").replace(/"/g, "\\\"");
}

export function denseRowDetailPanelClassName(...classes: Array<string | undefined | false | null>) {
  return cn(...classes);
}
