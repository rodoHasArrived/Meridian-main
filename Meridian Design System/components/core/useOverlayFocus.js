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

const FOCUSABLE = "button, a[href], area[href], input:not([type='hidden']), select, textarea, summary, [tabindex], [contenteditable='true']";

// Check ancestors as well as the control: a button inside a hidden/inert section
// can still report tabIndex=0. offsetParent is deliberately not used; fixed
// controls can have no offset parent while remaining visible and focusable.
function available(element) {
  if (!element.isConnected || element.matches(":disabled")) return false;
  for (let node = element; node; node = node.parentElement) {
    if (node.hidden || node.hasAttribute("inert") || node.getAttribute("aria-hidden") === "true") return false;
    const style = window.getComputedStyle(node);
    if (style.display === "none" || style.visibility === "hidden" || style.visibility === "collapse") return false;
    if (node.tagName === "DETAILS" && !node.open) {
      const summary = Array.from(node.children).find((child) => child.tagName === "SUMMARY");
      if (!summary?.contains(element)) return false;
    }
  }
  return true;
}

export function useOverlayFocus(ref, open, panelSelector = null) {
  React.useEffect(() => {
    if (!open || !ref.current) return;
    const previouslyFocused = document.activeElement;
    const panel = panelSelector ? ref.current.querySelector(panelSelector) : ref.current;
    if (!panel) return;
    const candidates = () => Array.from(panel.querySelectorAll(FOCUSABLE)).filter((element) => available(element));
    const tabbable = () => candidates().filter((element) => element.tabIndex >= 0);
    // A heading with tabIndex=-1 may be an intentional initial-focus target.
    // It must never become an extra stop in the subsequent Tab cycle.
    const autofocus = Array.from(panel.querySelectorAll("[autofocus], [data-autofocus]")).find((element) => element.matches(FOCUSABLE) && available(element));
    const retainedFocus = panel.contains(document.activeElement) && available(document.activeElement) ? document.activeElement : null;
    let addedPanelTabIndex = false;
    const focusPanel = () => {
      if (!panel.hasAttribute("tabindex")) {
        panel.setAttribute("tabindex", "-1");
        addedPanelTabIndex = true;
      }
      panel.focus();
    };
    const initial = autofocus || retainedFocus || tabbable()[0];
    if (initial) initial.focus();
    else focusPanel();
    const onKeyDown = (e) => {
      if (e.key !== "Tab") return;
      const items = tabbable();
      if (!items.length) { e.preventDefault(); focusPanel(); return; }
      const first = items[0];
      const last = items[items.length - 1];
      const active = document.activeElement;
      if (e.shiftKey && (active === first || !items.includes(active))) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && (active === last || !items.includes(active))) {
        e.preventDefault();
        first.focus();
      }
    };
    // Capture phase so the trap wins over content-level keydown handlers.
    document.addEventListener("keydown", onKeyDown, true);
    return () => {
      document.removeEventListener("keydown", onKeyDown, true);
      if (addedPanelTabIndex && panel.getAttribute("tabindex") === "-1") panel.removeAttribute("tabindex");
      if (previouslyFocused?.isConnected && previouslyFocused.focus) previouslyFocused.focus();
    };
  }, [open, panelSelector]);
}
