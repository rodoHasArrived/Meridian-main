// Meridian useOverlayFocus — the ONE focus-management implementation for blocking
// overlays (Dialog, Drawer, Modal). Implements the contract in ACCESSIBILITY.md §3 /
// PATTERNS.md "Keyboard navigation":
//   1. on open  — remember the trigger, move focus to the first focusable in the panel
//   2. while open — trap Tab: cycle at the boundaries, never escape to the page behind
//   3. on close — restore focus to the trigger
// `panelSelector` scopes the trap to the panel inside the overlay root (backdrop excluded).
// Previously Dialog and Drawer each carried a copy-pasted variant of steps 1+3 and no
// component implemented step 2; Modal had none of it.
import React from "react";

const FOCUSABLE = "button, [href], input, select, textarea, [tabindex]:not([tabindex='-1'])";

export function useOverlayFocus(ref, open, panelSelector = null) {
  React.useEffect(() => {
    if (!open || !ref.current) return;
    const previouslyFocused = document.activeElement;
    const panel = panelSelector ? ref.current.querySelector(panelSelector) : ref.current;
    if (panel) {
      const first = panel.querySelector(FOCUSABLE);
      if (first) first.focus();
    }
    const onKeyDown = (e) => {
      if (e.key !== "Tab" || !panel) return;
      const items = Array.from(panel.querySelectorAll(FOCUSABLE)).filter(
        (el) => !el.disabled && el.getAttribute("aria-hidden") !== "true" && el.offsetParent !== null
      );
      if (!items.length) { e.preventDefault(); return; }
      const first = items[0];
      const last = items[items.length - 1];
      const active = document.activeElement;
      if (e.shiftKey && (active === first || !panel.contains(active))) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && (active === last || !panel.contains(active))) {
        e.preventDefault();
        first.focus();
      }
    };
    // Capture phase so the trap wins over content-level keydown handlers.
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      if (previouslyFocused && previouslyFocused.focus) previouslyFocused.focus();
    };
  }, [open, panelSelector]);
}
