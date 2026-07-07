// Meridian Drawer — side panel for navigation, filters, or secondary actions.
// Slides in from edge, can be dismissed with backdrop click or ESC.
import React from "react";
import { useOverlayFocus } from "./useOverlayFocus";
const { useEffect, useRef } = React;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-drawer-overlay {
  position: fixed; inset: 0; background: var(--scrim, rgba(14,17,19,.5));
  animation: mds-drawer-fade-in var(--motion-base, 150ms) var(--ease-standard, ease-out);
}
@keyframes mds-drawer-fade-in {
  from { opacity: 0; }
  to { opacity: 1; }
}
.mds-drawer-wrap {
  position: fixed; background: var(--bg-light, #FAFBFC);
  display: flex; flex-direction: column; z-index: 1000;
  box-shadow: var(--shadow-menu);
}
/* Positions */
.mds-drawer-wrap--left {
  left: 0; top: 0; bottom: 0; width: 360px;
  animation: mds-drawer-slide-from-left var(--motion-base, 150ms) var(--ease-standard, ease-out);
}
.mds-drawer-wrap--right {
  right: 0; top: 0; bottom: 0; width: 360px;
  animation: mds-drawer-slide-from-right var(--motion-base, 150ms) var(--ease-standard, ease-out);
}
.mds-drawer-wrap--top {
  top: 0; left: 0; right: 0; height: 240px;
  animation: mds-drawer-slide-from-top var(--motion-base, 150ms) var(--ease-standard, ease-out);
}
.mds-drawer-wrap--bottom {
  bottom: 0; left: 0; right: 0; height: 240px;
  animation: mds-drawer-slide-from-bottom var(--motion-base, 150ms) var(--ease-standard, ease-out);
}
@keyframes mds-drawer-slide-from-left {
  from { transform: translateX(-100%); }
  to { transform: translateX(0); }
}
@keyframes mds-drawer-slide-from-right {
  from { transform: translateX(100%); }
  to { transform: translateX(0); }
}
@keyframes mds-drawer-slide-from-top {
  from { transform: translateY(-100%); }
  to { transform: translateY(0); }
}
@keyframes mds-drawer-slide-from-bottom {
  from { transform: translateY(100%); }
  to { transform: translateY(0); }
}
.mds-drawer-hd {
  display: flex; align-items: center; justify-content: space-between;
  padding: var(--space-md, 12px) var(--space-lg, 16px);
  border-bottom: 1px solid var(--border-divider, #E5E9EE);
  background: var(--bg-medium, #F5F7FA);
}
.mds-drawer-title {
  font-family: var(--font-body); font-size: var(--type-card-title, 13px);
  font-weight: 600; color: var(--text-primary, #22272E); margin: 0;
}
.mds-drawer-close {
  appearance: none; border: none; background: transparent;
  width: 32px; height: 32px; display: flex; align-items: center; justify-content: center;
  cursor: pointer; color: var(--text-secondary, #4D5967); font-size: 20px; line-height: 1; padding: 0;
}
.mds-drawer-close:hover { color: var(--text-primary, #22272E); }
.mds-drawer-close:focus-visible {
  outline: 2px solid var(--border-focus, #2F6F8F); outline-offset: -2px;
}
.mds-drawer-bd {
  flex: 1; overflow-y: auto; padding: var(--space-lg, 16px);
  font-family: var(--font-body); font-size: var(--type-body, 13px);
  color: var(--text-primary, #22272E);
}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "drawer");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Drawer({
  open = false,
  onClose = () => {},
  side = "right",
  title,
  children,
  size = side === "left" || side === "right" ? "360px" : "240px",
  closeOnBackdrop = true,
  closeOnEsc = true,
}) {
  inject();
  const ref = useRef(null);
  // Shared overlay focus contract: initial focus, Tab trap at boundaries, restore on close.
  useOverlayFocus(ref, open, ".mds-drawer-wrap");

  useEffect(() => {
    if (!open) return;
    const handleEsc = (e) => {
      if (closeOnEsc && e.key === "Escape") {
        onClose();
      }
    };
    document.addEventListener("keydown", handleEsc);
    return () => document.removeEventListener("keydown", handleEsc);
  }, [open, closeOnEsc, onClose]);

  if (!open) return null;

  const sizeStyle =
    side === "left" || side === "right" ? { width: size } : { height: size };

  return (
    <div
      ref={ref}
      className="mds-drawer-overlay"
      onClick={closeOnBackdrop ? onClose : undefined}
      role="presentation"
    >
      <div
        className={`mds-drawer-wrap mds-drawer-wrap--${side}`}
        style={sizeStyle}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby={title ? "mds-drawer-title" : undefined}
      >
        {title && <DrawerHeader title={title} onClose={onClose} />}
        <DrawerBody>{children}</DrawerBody>
      </div>
    </div>
  );
}

export function DrawerHeader({ title, onClose }) {
  return (
    <div className="mds-drawer-hd">
      <h2 className="mds-drawer-title" id="mds-drawer-title">
        {title}
      </h2>
      {onClose && (
        <button
          className="mds-drawer-close"
          onClick={onClose}
          aria-label="Close drawer"
          type="button"
        >
          ✕
        </button>
      )}
    </div>
  );
}

export function DrawerBody({ children }) {
  return <div className="mds-drawer-bd">{children}</div>;
}
