// Meridian Toast — ephemeral notifications. Call window.MeridianToast.show() from anywhere.
// ToastProvider renders the stack; useToast() returns convenience methods.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-toast-stack{position:fixed;bottom:20px;right:20px;z-index:9999;
  display:flex;flex-direction:column-reverse;gap:8px;pointer-events:none;}
.mds-toast{display:flex;align-items:flex-start;gap:10px;min-width:280px;max-width:420px;
  padding:12px 14px;border-radius:var(--radius-card,8px);
  border:1px solid var(--border,#D7DCE2);background:var(--bg-light,#fff);
  box-shadow:0 4px 16px rgba(23,26,31,.14),0 1px 4px rgba(23,26,31,.08);
  pointer-events:all;animation:mds-toast-in 180ms cubic-bezier(.2,.7,.3,1);}
@keyframes mds-toast-in{from{opacity:0;transform:translateY(12px) scale(.97);}to{opacity:1;transform:none;}}
.mds-toast--out{animation:mds-toast-out 160ms ease forwards;}
@keyframes mds-toast-out{to{opacity:0;transform:translateY(8px) scale(.97);}}
.mds-toast__bar{width:3px;border-radius:2px;flex-shrink:0;align-self:stretch;margin:-12px 0 -12px -14px;border-radius:8px 0 0 8px;}
.mds-toast--success .mds-toast__bar{background:var(--green,#16885F);}
.mds-toast--warning .mds-toast__bar{background:var(--amber,#B7791F);}
.mds-toast--error   .mds-toast__bar{background:var(--red,#BA3F55);}
.mds-toast--info    .mds-toast__bar{background:var(--accent,#2F6F8F);}
.mds-toast__body{flex:1;min-width:0;}
.mds-toast__title{font-family:var(--font-body);font-size:13px;font-weight:600;
  color:var(--text-primary,#22272E);}
.mds-toast__detail{font-family:var(--font-data);font-size:11px;color:var(--text-secondary,#4D5967);margin-top:2px;}
.mds-toast__close{appearance:none;border:none;background:transparent;cursor:pointer;
  color:var(--text-muted,#6E7781);font-size:16px;line-height:1;padding:0;flex-shrink:0;}
.mds-toast__close:hover{color:var(--text-primary,#22272E);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","toast");
  el.textContent = css;
  document.head.appendChild(el);
}

// Global event bus
const listeners = new Set();
window.MeridianToast = {
  show({ tone = "info", title, detail, duration = 4000 }) {
    const id = Date.now() + Math.random();
    listeners.forEach(fn => fn({ id, tone, title, detail, duration }));
  },
  success(title, detail, duration) { this.show({ tone:"success", title, detail, duration }); },
  warning(title, detail, duration) { this.show({ tone:"warning", title, detail, duration }); },
  error(title, detail, duration)   { this.show({ tone:"error",   title, detail, duration }); },
  info(title, detail, duration)    { this.show({ tone:"info",    title, detail, duration }); },
};

export function ToastProvider() {
  inject();
  const [toasts, setToasts] = React.useState([]);

  React.useEffect(() => {
    const handler = (toast) => {
      setToasts(prev => [...prev.slice(-4), { ...toast, exiting: false }]);
      setTimeout(() => {
        setToasts(prev => prev.map(t => t.id === toast.id ? { ...t, exiting: true } : t));
        setTimeout(() => setToasts(prev => prev.filter(t => t.id !== toast.id)), 180);
      }, toast.duration);
    };
    listeners.add(handler);
    return () => listeners.delete(handler);
  }, []);

  const dismiss = (id) => {
    setToasts(prev => prev.map(t => t.id === id ? { ...t, exiting: true } : t));
    setTimeout(() => setToasts(prev => prev.filter(t => t.id !== id)), 180);
  };

  if (!toasts.length) return null;
  return (
    <div className="mds-toast-stack">
      {toasts.map(t => (
        <div key={t.id} className={`mds-toast mds-toast--${t.tone}${t.exiting ? " mds-toast--out" : ""}`}>
          <div className="mds-toast__bar" />
          <div className="mds-toast__body">
            <div className="mds-toast__title">{t.title}</div>
            {t.detail && <div className="mds-toast__detail">{t.detail}</div>}
          </div>
          <button className="mds-toast__close" onClick={() => dismiss(t.id)}>×</button>
        </div>
      ))}
    </div>
  );
}

export function useToast() {
  return window.MeridianToast;
}

// Alias so the bundler finds a named "Toast" export from Toast.jsx
export const Toast = ToastProvider;
