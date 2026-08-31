// Meridian Modal System — compound components for dialogs, confirmations, forms.
// Modal + ModalHeader + ModalBody + ModalFooter form a complete dialog.
// ModalForm wraps a form with auto-submission in a modal context.
import React from "react";
import { useOverlayFocus } from "./useOverlayFocus";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mdl-overlay { position: fixed; inset: 0; background: var(--scrim, rgba(14,17,19,.5)); z-index: 50;
  display: flex; align-items: center; justify-content: center;
  animation: mdl-fade-in var(--motion-base, 150ms) var(--ease-standard, ease-out); }
@keyframes mdl-fade-in { from { opacity: 0; } to { opacity: 1; } }
.mdl-wrap { background: var(--bg-light, #fff); border: 1px solid var(--border, #D7DCE2);
  max-width: 520px; width: 92vw; max-height: 92vh; display: flex; flex-direction: column; }
.mdl-hd { display: flex; align-items: center; justify-content: space-between;
  padding: 16px 18px; border-bottom: 1px solid var(--border, #D7DCE2);
  background: var(--bg-medium, #F5F7FA); }
.mdl-title { font-family: var(--font-body); font-size: 15px; font-weight: 600;
  color: var(--text-primary, #22272E); margin: 0; }
.mdl-close { appearance: none; border: none; background: transparent; width: 28px; height: 28px;
  display: flex; align-items: center; justify-content: center; cursor: pointer;
  color: var(--text-muted, #59636F); font-size: 18px; line-height: 1;
}
.mdl-close:hover { color: var(--text-primary, #22272E); }
.mdl-bd { flex: 1; overflow-y: auto; padding: 18px; font-family: var(--font-body);
  font-size: 13px; color: var(--text-primary, #22272E); }
.mdl-ft { display: flex; align-items: center; gap: 8px; justify-content: flex-end;
  padding: 14px 18px; border-top: 1px solid var(--border, #D7DCE2);
  background: var(--bg-medium, #F5F7FA); flex-wrap: wrap; }
.mdl-btn { padding: 7px 14px; border: 1px solid var(--border, #D7DCE2);
  border-radius: var(--radius-button,2px); background: var(--bg-light, #fff);
  color: var(--text-primary, #22272E); font-family: var(--font-body);
  font-size: 12px; font-weight: 500; cursor: pointer;
  transition: background 100ms ease, border-color 100ms ease; }
.mdl-btn:hover { background: var(--bg-active, #E6EEF5); border-color: var(--accent, #2F6F8F); }
.mdl-btn--primary { background: var(--accent, #2F6F8F); color: var(--text-on-accent, #fff); border-color: var(--accent, #2F6F8F); }
.mdl-btn--primary:hover { background: var(--accent-dim, #255B75); }
.mdl-btn:disabled { opacity: 0.5; cursor: not-allowed; }
`;
  const el = document.createElement("style");
  el.setAttribute("data-mdl", "modal");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Modal({ open = false, onClose = () => {}, closeOnEsc = true, children }) {
  inject();
  const ref = React.useRef(null);
  // Shared overlay focus contract (initial focus, Tab trap, restore) — same hook as Dialog/Drawer.
  useOverlayFocus(ref, open, ".mdl-wrap");
  // Escape-to-close, matching Dialog/Drawer. Bound only while open.
  React.useEffect(() => {
    if (!open || !closeOnEsc) return;
    const onKey = (e) => { if (e.key === "Escape") onClose(); };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, closeOnEsc, onClose]);
  if (!open) return null;
  return (
    <div className="mdl-overlay" onClick={onClose} ref={ref}>
      <div
        className="mdl-wrap"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-labelledby="mdl-title"
      >
        {children}
      </div>
    </div>
  );
}

export function ModalHeader({ title, onClose }) {
  return (
    <div className="mdl-hd">
      <h2 className="mdl-title" id="mdl-title">{title}</h2>
      <button className="mdl-close" onClick={onClose} aria-label="Close">×</button>
    </div>
  );
}

export function ModalBody({ children }) {
  return <div className="mdl-bd">{children}</div>;
}

export function ModalFooter({ children }) {
  return <div className="mdl-ft">{children}</div>;
}

export function ModalForm({
  open = false,
  onClose = () => {},
  title = "Confirm",
  onSubmit = () => {},
  submitLabel = "Save",
  cancelLabel = "Cancel",
  loading = false,
  children,
}) {
  const handleSubmit = (e) => {
    e.preventDefault();
    onSubmit();
  };

  return (
    <Modal open={open} onClose={onClose}>
      <ModalHeader title={title} onClose={onClose} />
      <form onSubmit={handleSubmit}>
        <ModalBody>{children}</ModalBody>
        <ModalFooter>
          <button type="button" className="mdl-btn" onClick={onClose}>
            {cancelLabel}
          </button>
          <button type="submit" className="mdl-btn mdl-btn--primary" disabled={loading}>
            {loading ? "…" : submitLabel}
          </button>
        </ModalFooter>
      </form>
    </Modal>
  );
}
