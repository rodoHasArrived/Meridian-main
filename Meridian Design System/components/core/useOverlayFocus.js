// Meridian overlay services — the ONE implementation of the overlay contracts
// (ACCESSIBILITY.md §3 / guidelines/PATTERNS.md "Keyboard navigation").
//
// useOverlayFocus (blocking overlays: Dialog, Drawer, Modal):
//   1. on open  — remember the trigger, move focus to the first focusable in the panel
//   2. while open — trap Tab: cycle at the boundaries, never escape to the page behind
//   3. on close — restore focus to the trigger
//   4. lock body scroll while open (ref-counted across stacked overlays)
//
// useOverlayEscape (anything dismissed by Escape): a LIFO layer stack — one document
// listener fires ONLY the top layer's handler, so Escape peels overlays one at a
// time instead of closing a Dialog and the Drawer behind it together. Components
// that handle Escape on their own element (Select, Combobox, editable cells) must
// e.stopPropagation() after handling so this document-level stack never sees it.
import React from "react";

const FOCUSABLE = "button, a[href], area[href], input:not([type='hidden']), select, textarea, summary, [tabindex], [contenteditable='true']";

// A descendant can still expose tabIndex=0 while its section is hidden, inert,
// disabled by a fieldset, or collapsed. Fixed-position controls remain valid.
function available(element) {
  if (!element?.isConnected || element.matches(":disabled")) return false;
  for (let node = element; node; node = node.parentElement) {
    if (node.hidden || node.hasAttribute("inert") || node.getAttribute("aria-hidden") === "true") return false;
    const style = window.getComputedStyle(node);
    if (style.display === "none" || style.visibility === "hidden" || style.visibility === "collapse" || style.contentVisibility === "hidden") return false;
    if (node.tagName === "DETAILS" && !node.open) {
      const summary = Array.from(node.children).find((child) => child.tagName === "SUMMARY");
      if (!summary?.contains(element)) return false;
    }
  }
  return true;
}

let scrollLocks = 0;
let savedOverflow = "";
function lockScroll() {
  if (scrollLocks++ === 0) { savedOverflow = document.body.style.overflow; document.body.style.overflow = "hidden"; }
}
function unlockScroll() {
  if (scrollLocks > 0 && --scrollLocks === 0) document.body.style.overflow = savedOverflow;
}

export function useOverlayFocus(ref, open, panelSelector = null, { scrollLock = true } = {}) {
  React.useEffect(() => {
    if (!open || !ref.current) return;
    const panel = panelSelector ? ref.current.querySelector(panelSelector) : ref.current;
    if (!panel || !available(panel)) return;
    const previouslyFocused = document.activeElement;
    if (scrollLock) lockScroll();
    const candidates = () => Array.from(panel.querySelectorAll(FOCUSABLE)).filter(available);
    const tabbable = () => candidates().filter((element) => element.tabIndex >= 0);
    // Negative-tabindex headings may intentionally receive initial focus, but
    // must never become additional stops in the subsequent Tab cycle.
    const autofocus = Array.from(panel.querySelectorAll("[autofocus], [data-autofocus]")).find(
      (element) => element.matches(FOCUSABLE) && available(element)
    );
    const retained = panel.contains(document.activeElement) && available(document.activeElement) ? document.activeElement : null;
    let addedPanelTabIndex = false;
    const focusPanel = () => {
      if (!panel.hasAttribute("tabindex")) { panel.setAttribute("tabindex", "-1"); addedPanelTabIndex = true; }
      panel.focus();
    };
    const initial = autofocus || retained || tabbable()[0];
    if (initial) initial.focus(); else focusPanel();
    const onKeyDown = (e) => {
      if (e.key !== "Tab" || !available(panel)) return;
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
      if (scrollLock) unlockScroll();
      if (available(previouslyFocused) && previouslyFocused.focus) previouslyFocused.focus();
    };
  }, [open, panelSelector]);
}

// ── Escape layer stack ──
const escStack = [];
function onDocEscape(e) {
  if (e.key !== "Escape" || escStack.length === 0) return;
  // Bubble phase (not capture): element-level handlers that stopPropagation win first.
  e.preventDefault();
  escStack[escStack.length - 1].fire();
}
export function useOverlayEscape(active, handler) {
  const latest = React.useRef(handler);
  latest.current = handler;
  React.useEffect(() => {
    if (!active) return;
    const entry = { fire: () => latest.current?.() };
    escStack.push(entry);
    if (escStack.length === 1) document.addEventListener("keydown", onDocEscape);
    return () => {
      const i = escStack.indexOf(entry);
      if (i >= 0) escStack.splice(i, 1);
      if (escStack.length === 0) document.removeEventListener("keydown", onDocEscape);
    };
  }, [active]);
}
