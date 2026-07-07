// Meridian Dialog — accessible modal dialog with focus trapping and ESC key handling.
// Use for confirmations, alerts, and blocking user interactions.
// Compound: Dialog + DialogHeader + DialogBody + DialogFooter.
import React from "react";
import { useOverlayFocus } from "./useOverlayFocus";
const { useEffect, useRef } = React;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-dialog-overlay {
  position: fixed; inset: 0; background: var(--scrim, rgba(14,17,19,.5));
  z-index: 1000; display: flex; align-items: center; justify-content: center;
  animation: mds-dialog-fade-in var(--motion-base, 150ms) var(--ease-standard, ease-out);
}
@keyframes mds-dialog-fade-in {
  from { opacity: 0; }
  to { opacity: 1; }
}
.mds-dialog-wrap {
  background: var(--bg-light, #FAFBFC); border: 1px solid var(--border, #D7DCE2);
  border-radius: var(--radius-card, 2px); max-width: 520px; width: 92vw;
  max-height: 92vh; display: flex; flex-direction: column;
  box-shadow: var(--shadow-menu); animation: mds-dialog-slide-up var(--motion-base, 150ms) var(--ease-standard, ease-out);
}
@keyframes mds-dialog-slide-up {
  from { transform: translateY(16px); opacity: 0; }
  to { transform: translateY(0); opacity: 1; }
}
.mds-dialog-hd {
  display: flex; align-items: center; justify-content: space-between;
  padding: var(--space-md, 12px) var(--space-lg, 16px);
  border-bottom: 1px solid var(--border-divider, #E5E9EE);
  background: var(--bg-medium, #F5F7FA);
}
.mds-dialog-title {
  font-family: var(--font-body); font-size: var(--type-card-title, 13px);
  font-weight: 600; color: var(--text-primary, #22272E); margin: 0;
}
.mds-dialog-close {
  appearance: none; border: none; background: transparent;
  width: 32px; height: 32px; display: flex; align-items: center; justify-content: center;
  cursor: pointer; color: var(--text-secondary, #4D5967); font-size: 20px; line-height: 1;
  padding: 0;
}
.mds-dialog-close:hover { color: var(--text-primary, #22272E); }
.mds-dialog-close:focus-visible {
  outline: 2px solid var(--border-focus, #2F6F8F); outline-offset: 2px;
}
.mds-dialog-bd {
  flex: 1; overflow-y: auto; padding: var(--space-lg, 16px);
  font-family: var(--font-body); font-size: var(--type-body, 13px);
  color: var(--text-primary, #22272E); line-height: var(--lh-body, 20px);
}
.mds-dialog-ft {
  display: flex; align-items: center; gap: var(--space-md, 12px);
  justify-content: flex-end; padding: var(--space-md, 12px) var(--space-lg, 16px);
  border-top: 1px solid var(--border-divider, #E5E9EE);
  background: var(--bg-medium, #F5F7FA); flex-wrap: wrap;
}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "dialog");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Dialog({
  open = false,
  onClose = () => {},
  title,
  children,
  showClose = true,
  maxWidth = "520px",
  closeOnEsc = true,
}) {
  inject();
  const ref = useRef(null);
  // Shared overlay focus contract: initial focus, Tab trap at boundaries, restore on close.
  useOverlayFocus(ref, open, ".mds-dialog-wrap");

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

  return (
    <div
      ref={ref}
      className="mds-dialog-overlay"
      onClick={onClose}
      role="presentation"
    >
      <div
        className="mds-dialog-wrap"
        style={{ maxWidth }}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby={title ? "mds-dialog-title" : undefined}
      >
        {title && (
          <DialogHeader title={title} onClose={showClose ? onClose : undefined} showClose={showClose} />
        )}
        {children}
      </div>
    </div>
  );
}

export function DialogHeader({ title, onClose, showClose = true }) {
  return (
    <div className="mds-dialog-hd">
      <h2 className="mds-dialog-title" id="mds-dialog-title">
        {title}
      </h2>
      {showClose && (
        <button
          className="mds-dialog-close"
          onClick={onClose}
          aria-label="Close dialog"
          type="button"
        >
          ✕
        </button>
      )}
    </div>
  );
}

export function DialogBody({ children }) {
  return <div className="mds-dialog-bd">{children}</div>;
}

export function DialogFooter({ children }) {
  return <div className="mds-dialog-ft">{children}</div>;
}
