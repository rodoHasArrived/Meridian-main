// Meridian Dialog — the ONE overlay implementation: one scrim, one focus trap, one
// title-id contract. Confirmations, alerts, and bounded create/edit flows alike — width is
// a prop (`size`), not a second component. Compound: Dialog + DialogHeader + DialogBody +
// DialogFooter + DialogForm (footer actions wired to the system Button).
import React from "react";
import { useOverlayFocus } from "./useOverlayFocus";
import { Button } from "./Button";
const { useEffect, useRef } = React;

// Per-instance title id so stacked dialogs never emit duplicate DOM ids.
// DialogHeader reads it from context, so manually-composed headers stay wired.
const DialogTitleIdContext = React.createContext(null);

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-dialog-overlay {
  position: fixed; inset: 0; background: var(--scrim, rgba(17,23,29,.5));
  z-index: var(--z-modal, 200); display: flex; align-items: center; justify-content: center;
  animation: mds-dialog-fade-in var(--motion-base, 150ms) var(--ease-standard, ease-out);
}
@keyframes mds-dialog-fade-in {
  from { opacity: 0; }
  to { opacity: 1; }
}
.mds-dialog-wrap {
  background: var(--bg-light, #F8F7F3); border: 1px solid var(--border, #D2CDC3);
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
  border-bottom: 1px solid var(--border-divider, #E2DED5);
  background: var(--bg-medium, #EBE8E1);
}
.mds-dialog-title {
  font-family: var(--font-body); font-size: var(--type-card-title, 13px);
  font-weight: 600; color: var(--text-primary, #20252B); margin: 0;
}
.mds-dialog-close {
  appearance: none; border: none; background: transparent;
  width: 32px; height: 32px; display: flex; align-items: center; justify-content: center;
  cursor: pointer; color: var(--text-secondary, #535C65); font-size: 20px; line-height: 1;
  padding: 0;
}
.mds-dialog-close:hover { color: var(--text-primary, #20252B); }
.mds-dialog-close:focus-visible {
  outline: 2px solid var(--border-focus, #2D6FB3); outline-offset: 2px;
}
.mds-dialog-bd {
  flex: 1; overflow-y: auto; padding: var(--space-lg, 16px);
  font-family: var(--font-body); font-size: var(--type-body, 13px);
  color: var(--text-primary, #20252B); line-height: var(--lh-body, 20px);
}
.mds-dialog-ft {
  display: flex; align-items: center; gap: var(--space-md, 12px);
  justify-content: flex-end; padding: var(--space-md, 12px) var(--space-lg, 16px);
  border-top: 1px solid var(--border-divider, #E2DED5);
  background: var(--bg-medium, #EBE8E1); flex-wrap: wrap;
}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "dialog");
  el.textContent = css;
  document.head.appendChild(el);
}

const DIALOG_WIDTHS = { sm: "420px", md: "520px", lg: "720px" };

export function Dialog({
  open = false,
  onClose = () => {},
  title,
  children,
  showClose = true,
  size = "md",
  maxWidth,
  closeOnEsc = true,
}) {
  inject();
  const ref = useRef(null);
  const titleId = React.useId();
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
        style={{ maxWidth: maxWidth || DIALOG_WIDTHS[size] || DIALOG_WIDTHS.md }}
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
      >
        <DialogTitleIdContext.Provider value={titleId}>
          {title && (
            <DialogHeader title={title} onClose={showClose ? onClose : undefined} showClose={showClose} />
          )}
          {children}
        </DialogTitleIdContext.Provider>
      </div>
    </div>
  );
}

export function DialogHeader({ title, onClose, showClose = true }) {
  const titleId = React.useContext(DialogTitleIdContext) ?? undefined;
  return (
    <div className="mds-dialog-hd">
      <h2 className="mds-dialog-title" id={titleId}>
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

// Bounded create/edit flow: header + submitting form + footer actions in system Buttons.
export function DialogForm({
  open = false,
  onClose = () => {},
  title = "Confirm",
  onSubmit = () => {},
  submitLabel = "Save",
  cancelLabel = "Cancel",
  loading = false,
  size = "md",
  children,
}) {
  const handleSubmit = (e) => { e.preventDefault(); onSubmit(); };
  return (
    <Dialog open={open} onClose={onClose} title={title} size={size}>
      <form onSubmit={handleSubmit}>
        <DialogBody>{children}</DialogBody>
        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onClose}>{cancelLabel}</Button>
          <Button type="submit" variant="primary" busy={loading}>{submitLabel}</Button>
        </DialogFooter>
      </form>
    </Dialog>
  );
}
