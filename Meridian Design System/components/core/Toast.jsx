// Meridian Toast — ephemeral notifications. Call window.MeridianToast.show() from anywhere.
// ToastProvider renders the stack; useToast() returns convenience methods.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-toast-stack{position:fixed;bottom:20px;right:20px;z-index:var(--z-toast,300);
  display:flex;flex-direction:column-reverse;gap:8px;pointer-events:none;}
.mds-toast{display:flex;align-items:flex-start;gap:10px;min-width:280px;max-width:420px;
  padding:12px 14px;border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);
  pointer-events:all;}
.mds-toast--out{display:none;}
.mds-toast__bar{width:3px;flex-shrink:0;align-self:stretch;margin:-12px 0 -12px -14px;}
.mds-toast--success .mds-toast__bar{background:var(--green,#2E7655);}
.mds-toast--warning .mds-toast__bar{background:var(--amber,#986717);}
.mds-toast--error   .mds-toast__bar{background:var(--red,#B54B42);}
.mds-toast--info    .mds-toast__bar{background:var(--semantic-info,#2F648A);}
.mds-toast__body{flex:1;min-width:0;}
.mds-toast__title{font-family:var(--font-body);font-size:13px;font-weight:600;
  color:var(--text-primary,#20252B);}
.mds-toast__detail{font-family:var(--font-data);font-size:11px;color:var(--text-secondary,#535C65);margin-top:2px;}
.mds-toast__close{appearance:none;border:none;background:transparent;cursor:pointer;
  color:var(--text-muted,#6B737B);font-size:16px;line-height:1;padding:0;flex-shrink:0;}
.mds-toast__close:hover{color:var(--text-primary,#20252B);}
.mds-toast__action{appearance:none;border:1px solid var(--brand-signal,#C56343);background:transparent;
  cursor:pointer;color:var(--brand-signal-dim,#A64E33);font-family:var(--font-body);font-size:12px;
  font-weight:600;padding:4px 10px;margin-top:8px;align-self:flex-start;border-radius:var(--radius-button,2px);}
.mds-toast__action:hover{background:var(--brand-signal-ghost,#F6E6DF);}
.mds-toast__action:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","toast");
  el.textContent = css;
  document.head.appendChild(el);
}

// Global event bus
const listeners = new Set();
const toastApi = {
  show({ tone = "info", title, detail, duration = 4000, action = null }) {
    const id = Date.now() + Math.random();
    listeners.forEach(fn => fn({ id, tone, title, detail, duration, action }));
    return id;
  },
  success(title, detail, duration) { return this.show({ tone:"success", title, detail, duration }); },
  warning(title, detail, duration) { return this.show({ tone:"warning", title, detail, duration }); },
  error(title, detail, duration)   { return this.show({ tone:"error",   title, detail, duration }); },
  info(title, detail, duration)    { return this.show({ tone:"info",    title, detail, duration }); },
  // Confirmation of a reversible action, with an Undo affordance. onUndo fires if the
  // operator clicks Undo before the toast auto-dismisses (default 6s — longer than a plain toast).
  undo(title, onUndo, detail, duration = 6000) {
    return this.show({ tone: "info", title, detail, duration, action: { label: "Undo", onAction: onUndo } });
  },
  dismiss(id) { dismissers.forEach(fn => fn(id)); },
};
const dismissers = new Set();
if (typeof window !== "undefined") window.MeridianToast = toastApi;

export function ToastProvider() {
  inject();
  const [toasts, setToasts] = React.useState([]);
  const timers = React.useRef(new Set());
  const schedule = (callback, duration) => {
    const timer = setTimeout(() => { timers.current.delete(timer); callback(); }, duration);
    timers.current.add(timer);
  };

  React.useEffect(() => {
    const handler = (toast) => {
      setToasts(prev => [...prev.slice(-4), { ...toast, exiting: false }]);
      if (toast.duration > 0) schedule(() => {
        setToasts(prev => prev.map(t => t.id === toast.id ? { ...t, exiting: true } : t));
        schedule(() => setToasts(prev => prev.filter(t => t.id !== toast.id)), 180);
      }, toast.duration);
    };
    const dismisser = (id) => dismiss(id);
    listeners.add(handler);
    dismissers.add(dismisser);
    return () => { listeners.delete(handler); dismissers.delete(dismisser); timers.current.forEach(clearTimeout); timers.current.clear(); };
  }, []);

  const dismiss = (id) => {
    setToasts(prev => prev.map(t => t.id === id ? { ...t, exiting: true } : t));
    schedule(() => setToasts(prev => prev.filter(t => t.id !== id)), 180);
  };

  if (!toasts.length) return null;
  return (
    <div className="mds-toast-stack" role="status" aria-live="polite" aria-atomic="false">
      {toasts.map(t => (
        <div key={t.id} className={`mds-toast mds-toast--${t.tone}${t.exiting ? " mds-toast--out" : ""}`}>
          <div className="mds-toast__bar" />
          <div className="mds-toast__body">
            <div className="mds-toast__title">{t.title}</div>
            {t.detail && <div className="mds-toast__detail">{t.detail}</div>}
            {t.action && (
              <button type="button" className="mds-toast__action" onClick={() => { t.action.onAction?.(); dismiss(t.id); }}>
                {t.action.label}
              </button>
            )}
          </div>
          <button type="button" className="mds-toast__close" aria-label="Dismiss notification" onClick={() => dismiss(t.id)}>×</button>
        </div>
      ))}
    </div>
  );
}

export function useToast() {
  return toastApi;
}

// Alias so the bundler finds a named "Toast" export from Toast.jsx
export const Toast = ToastProvider;
