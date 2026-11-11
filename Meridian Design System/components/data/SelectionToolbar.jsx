// Meridian SelectionToolbar — a floating toolbar that appears when rows are selected in a table.
// Shows selection count, "Select all" / "Clear" shortcuts, and one primary action + ghost options.
// Animates in from the bottom with a subtle slide. Designed to sit above/below a DataGrid.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-seltb{display:none;position:relative;
  background:var(--bg-light,#F8F7F3);border:1px solid var(--border,#D2CDC3);
  border-radius:var(--radius-card,2px);padding:12px 14px;
  box-shadow:var(--shadow-card,none);
  margin-bottom:12px;}
.mds-seltb--active{display:flex;align-items:center;justify-content:space-between;gap:12px;
  animation:mds-seltb-slide-in 150ms ease;}
@keyframes mds-seltb-slide-in{from{opacity:0;transform:translateY(8px);}to{opacity:1;transform:translateY(0);}}
.mds-seltb__count{display:flex;align-items:center;gap:12px;flex-shrink:0;}
.mds-seltb__badge{font-family:var(--font-body);font-size:12px;font-weight:600;
  color:var(--text-primary,#20252B);}
.mds-seltb__shortcuts{display:flex;gap:12px;}
.mds-seltb__shortcut{appearance:none;border:none;background:transparent;cursor:pointer;
  font-family:var(--font-body);font-size:11px;font-weight:500;color:var(--brand-signal,#C56343);
  text-decoration:underline;padding:0;line-height:1;}
.mds-seltb__shortcut:hover{opacity:.8;}
.mds-seltb__actions{display:flex;align-items:center;gap:8px;flex-shrink:0;}
.mds-seltb__btn{appearance:none;border:1px solid;border-radius:var(--radius-button,2px);
  font-family:var(--font-body);font-size:11px;font-weight:600;font-variant:all-small-caps;
  letter-spacing:.02em;padding:6px 12px;cursor:pointer;
  transition:background-color 100ms ease,border-color 100ms ease;}
.mds-seltb__btn--primary{background:var(--brand-signal,#C56343);border-color:var(--brand-signal,#C56343);
  color:var(--text-on-accent,#fff);}
.mds-seltb__btn--primary:hover{background:var(--brand-signal-hover,#D2795A);border-color:var(--brand-signal-hover,#D2795A);}
.mds-seltb__btn--primary:active{background:var(--brand-signal-dim,#A64E33);}
.mds-seltb__btn--ghost{background:var(--bg-light,#F8F7F3);border-color:var(--border,#D2CDC3);
  color:var(--text-primary,#20252B);}
.mds-seltb__btn--ghost:hover{background:var(--bg-hover,#EDEFF0);border-color:var(--border-hover,#AAA49A);}
.mds-seltb__btn--danger{background:var(--bg-light,#F8F7F3);border-color:var(--border,#D2CDC3);
  color:var(--red,#B54B42);}
.mds-seltb__btn--danger:hover{background:var(--red-a10,rgba(166,67,63,.10));border-color:var(--red,#B54B42);}
.mds-seltb__btn--danger:active{background:var(--red-a20,color-mix(in srgb,var(--red) 35%,transparent));}
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
