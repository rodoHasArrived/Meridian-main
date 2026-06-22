// Meridian SelectionToolbar — a floating toolbar that appears when rows are selected in a table.
// Shows selection count, "Select all" / "Clear" shortcuts, and one primary action + ghost options.
// Animates in from the bottom with a subtle slide. Designed to sit above/below a DenseDataTable.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-seltb{display:none;position:relative;
  background:var(--bg-light,#fff);border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-card,8px);padding:12px 14px;
  box-shadow:var(--shadow-card,0 1px 1px rgba(0,0,0,.08));
  margin-bottom:12px;}
.mds-seltb--active{display:flex;align-items:center;justify-content:space-between;gap:12px;
  animation:mds-seltb-slide-in 150ms ease;}
@keyframes mds-seltb-slide-in{from{opacity:0;transform:translateY(8px);}to{opacity:1;transform:translateY(0);}}
.mds-seltb__count{display:flex;align-items:center;gap:12px;flex-shrink:0;}
.mds-seltb__badge{font-family:var(--font-body);font-size:12px;font-weight:600;
  color:var(--text-primary,#22272E);}
.mds-seltb__shortcuts{display:flex;gap:12px;}
.mds-seltb__shortcut{appearance:none;border:none;background:transparent;cursor:pointer;
  font-family:var(--font-body);font-size:11px;font-weight:500;color:var(--accent,#2F6F8F);
  text-decoration:underline;padding:0;line-height:1;}
.mds-seltb__shortcut:hover{opacity:.8;}
.mds-seltb__actions{display:flex;align-items:center;gap:8px;flex-shrink:0;}
.mds-seltb__btn{appearance:none;border:1px solid;border-radius:var(--radius-button,6px);
  font-family:var(--font-body);font-size:11px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.02em;padding:6px 12px;cursor:pointer;
  transition:background-color 100ms ease,border-color 100ms ease;}
.mds-seltb__btn--primary{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);
  color:#fff;}
.mds-seltb__btn--primary:hover{background:rgba(47,111,143,.80);border-color:var(--accent,#2F6F8F);}
.mds-seltb__btn--primary:active{background:var(--accent-dim,#255B75);}
.mds-seltb__btn--ghost{background:var(--bg-light,#fff);border-color:var(--border,#D7DCE2);
  color:var(--text-primary,#22272E);}
.mds-seltb__btn--ghost:hover{background:var(--bg-hover,#F1F4F7);border-color:var(--border-hover,#B8C2CC);}
.mds-seltb__btn--danger{background:var(--bg-light,#fff);border-color:var(--border,#D7DCE2);
  color:var(--red,#BA3F55);}
.mds-seltb__btn--danger:hover{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);}
.mds-seltb__btn--danger:active{background:var(--red-a20,rgba(186,63,85,.20));}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "seltb");
  el.textContent = css;
  document.head.appendChild(el);
}

export function SelectionToolbar({
  count = 0,
  total = 0,
  onSelectAll,
  onClear,
  primaryAction,
  actions = [],
  className = "",
}) {
  inject();
  const active = count > 0;

  return (
    <div className={`mds-seltb ${active ? "mds-seltb--active" : ""} ${className}`}>
      <div className="mds-seltb__count">
        <span className="mds-seltb__badge">
          {count} of {total} selected
        </span>
        {count < total && onSelectAll && (
          <div className="mds-seltb__shortcuts">
            <button className="mds-seltb__shortcut" onClick={onSelectAll} type="button">
              Select all
            </button>
          </div>
        )}
        {count > 0 && onClear && (
          <div className="mds-seltb__shortcuts">
            <button className="mds-seltb__shortcut" onClick={onClear} type="button">
              Clear
            </button>
          </div>
        )}
      </div>
      {(primaryAction || actions.length > 0) && (
        <div className="mds-seltb__actions">
          {primaryAction && (
            <button
              className="mds-seltb__btn mds-seltb__btn--primary"
              onClick={primaryAction.onClick}
              type="button"
            >
              {primaryAction.label}
            </button>
          )}
          {actions.map((action, i) => (
            <button
              key={i}
              className={`mds-seltb__btn mds-seltb__btn--${action.variant || "ghost"}`}
              onClick={action.onClick}
              type="button"
              title={action.tooltip}
            >
              {action.label}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
