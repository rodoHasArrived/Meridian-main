// Meridian NotificationCenter — the "what fired while I was away" rail toasts can't provide.
// A bell button with an unread count; clicking opens an anchored panel of persistent
// notifications (tone dot, title, detail, relative time). Controlled: the consumer owns the
// items array; the component reports reads/dismissals.
import React from "react";
import { Timestamp } from "./Timestamp";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-notif{position:relative;display:inline-block;font-family:var(--font-body);}
.mds-notif__bell{appearance:none;cursor:pointer;display:inline-flex;align-items:center;gap:6px;
  padding:5px 10px;border:1px solid var(--border,#D2CDC3);background:var(--bg-light,#F8F7F3);
  color:var(--text-secondary,#535C65);font-family:var(--font-body);font-size:12px;}
.mds-notif__bell:hover{background:var(--bg-hover,#EDEFF0);}
.mds-notif__bell:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
.mds-notif__count{font-family:var(--font-data);font-size:10px;font-weight:700;line-height:1;
  padding:2px 5px;background:var(--red,#B54B42);color:var(--text-on-fill,#fff);
  border-radius:var(--radius-chip,2px);}
.mds-notif__panel{position:absolute;right:0;top:calc(100% + 4px);width:340px;max-height:420px;
  overflow-y:auto;z-index:var(--z-dropdown,100);background:var(--bg-light,#F8F7F3);
  border:1px solid var(--border-strong,#AAA49A);box-shadow:var(--shadow-menu);}
.mds-notif__hd{display:flex;align-items:center;justify-content:space-between;padding:9px 12px;
  border-bottom:1px solid var(--border,#D2CDC3);position:sticky;top:0;background:var(--bg-light,#F8F7F3);}
.mds-notif__hd-title{font-size:11px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--text-muted,#606870);}
.mds-notif__mark{appearance:none;border:none;background:transparent;cursor:pointer;
  font-size:11px;color:var(--brand-signal,#C56343);padding:0;font-family:var(--font-body);}
.mds-notif__mark:hover{text-decoration:underline;}
.mds-notif__item{display:grid;grid-template-columns:10px 1fr auto;gap:8px;padding:9px 12px;
  border-top:1px solid var(--border-divider,#E2DED5);align-items:start;}
.mds-notif__item:first-of-type{border-top:none;}
.mds-notif__item--unread{background:var(--card-surface-raised,#FCFBF8);}
.mds-notif__dot{width:7px;height:7px;border-radius:50%;margin-top:4px;}
.mds-notif__dot--info{background:var(--semantic-info,#2F648A);}
.mds-notif__dot--success{background:var(--green,#2E7655);}
.mds-notif__dot--warning{background:var(--orange,#986717);}
.mds-notif__dot--error{background:var(--red,#B54B42);}
.mds-notif__title{font-size:12px;font-weight:600;color:var(--text-primary,#20252B);}
.mds-notif__detail{font-size:11px;color:var(--text-secondary,#535C65);line-height:1.4;margin-top:1px;}
.mds-notif__time{font-size:10px;color:var(--text-muted,#606870);white-space:nowrap;}
.mds-notif__empty{padding:24px 12px;text-align:center;font-size:12px;color:var(--text-muted,#606870);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "notificationcenter");
  el.textContent = css;
  document.head.appendChild(el);
}

export function NotificationCenter({ items = [], onMarkAllRead, onSelect, label = "Alerts" }) {
  inject();
  const [open, setOpen] = React.useState(false);
  const rootRef = React.useRef(null);
  const buttonRef = React.useRef(null);
  const unread = items.filter((n) => !n.read).length;
  React.useEffect(() => {
    if (!open) return;
    const onDown = (e) => {
      if (rootRef.current && !rootRef.current.contains(e.target)) setOpen(false);
    };
    const onKey = (e) => { if (e.key === "Escape" && rootRef.current?.contains(e.target)) { e.stopPropagation(); setOpen(false); buttonRef.current?.focus(); } };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);
  return (
    <div className="mds-notif" ref={rootRef}>
      <button ref={buttonRef} type="button" className="mds-notif__bell" aria-expanded={open} aria-haspopup="true"
        onClick={() => setOpen((o) => !o)}>
        {label}
        {unread > 0 && <span className="mds-notif__count">{unread}</span>}
      </button>
      {open && (
        <div className="mds-notif__panel" role="region" aria-label="Notifications">
          <div className="mds-notif__hd">
            <span className="mds-notif__hd-title">Notifications</span>
            {unread > 0 && onMarkAllRead && (
              <button type="button" className="mds-notif__mark" onClick={onMarkAllRead}>Mark all read</button>
            )}
          </div>
          {items.length === 0 && <div className="mds-notif__empty">Nothing while you were away.</div>}
          {items.map((n) => (
            <div key={n.id} className={`mds-notif__item${n.read ? "" : " mds-notif__item--unread"}`}
              role={onSelect ? "button" : undefined} tabIndex={onSelect ? 0 : undefined}
              onKeyDown={(e) => { if (onSelect && (e.key === "Enter" || e.key === " ")) { e.preventDefault(); onSelect(n); } }}
              onClick={() => onSelect && onSelect(n)} style={onSelect ? { cursor: "pointer" } : undefined}>
              <span className={`mds-notif__dot mds-notif__dot--${n.tone || "info"}`} aria-hidden="true" />
              <span>
                <div className="mds-notif__title">{n.title}</div>
                {n.detail && <div className="mds-notif__detail">{n.detail}</div>}
              </span>
              {n.time != null && <Timestamp className="mds-notif__time" value={n.time} format="relative" />}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
