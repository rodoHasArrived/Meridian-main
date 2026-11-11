// Meridian DataStateSwitch — the data-state ladder as one wrapper. Renders children only when
// `state === "ready"`; otherwise it paints the loading / empty / error slot at surface scale, so a
// template demonstrates all four states instead of shipping the happy path alone.
// A11y (3.5): non-ready states announce via `announce`: "polite" | "assertive" | "off"
// (default: assertive for error, polite otherwise). Ready content is returned UNWRAPPED —
// no live region ever wraps the working surface.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "data-state-switch");
  el.textContent = `
.mds-dss{display:flex;flex-direction:column;height:100%;min-height:0;background:var(--bg,#F7F5F0);font-family:var(--font-body);}
.mds-dss__hd{display:flex;align-items:center;gap:10px;padding:10px 16px;background:var(--bg-medium,#EFEDE7);border-bottom:1px solid var(--border-strong,#CFC9BC);}
.mds-dss__eyebrow{font-size:10px;font-weight:600;font-variant:all-small-caps;letter-spacing:.07em;color:var(--text-muted,#6B737B);}
.mds-dss__state{margin-left:auto;font:600 10px var(--font-data,monospace);letter-spacing:.08em;text-transform:uppercase;color:var(--text-secondary,#535C65);}
.mds-dss__body{flex:1;min-height:0;display:flex;flex-direction:column;justify-content:center;padding:24px;overflow:auto;}
.mds-dss__bars{display:flex;flex-direction:column;gap:9px;max-width:1100px;width:100%;margin:0 auto;}
.mds-dss__row{display:grid;gap:12px;}
.mds-dss__bar{height:12px;background:var(--bg-active,#E7EDF2);border-radius:var(--radius-chip,3px);position:relative;overflow:hidden;}
.mds-dss__bar::after{content:"";position:absolute;inset:0;transform:translateX(-100%);background:linear-gradient(90deg,transparent,var(--bg-hover,#EDEFF0),transparent);animation:mds-dss-sweep 1.3s ease-in-out infinite;}
.mds-dss__center{display:flex;flex-direction:column;align-items:center;justify-content:center;gap:10px;text-align:center;padding:32px 24px;}
.mds-dss__title{font-size:14px;font-weight:600;color:var(--text-secondary,#535C65);}
.mds-dss__detail{font-size:12px;line-height:1.5;max-width:44ch;color:var(--text-muted,#6B737B);}
.mds-dss__glyph{width:34px;height:34px;border:1px solid var(--border-strong,#CFC9BC);border-radius:var(--radius-chip,3px);color:var(--text-disabled,#A4ABB2);display:flex;align-items:center;justify-content:center;font:600 15px var(--font-data,monospace);}
.mds-dss--error .mds-dss__glyph{border-color:var(--red,#B54B42);color:var(--red-dim,#7D322F);background:var(--red-a10,rgba(166,67,63,.10));}
.mds-dss--error .mds-dss__title{color:var(--red-dim,#7D322F);}
.mds-dss__btn{margin-top:2px;padding:7px 16px;background:var(--brand-signal,#C56343);color:var(--text-on-fill,#fff);border:none;border-radius:var(--radius-button,2px);font:600 12px var(--font-body);cursor:pointer;}
.mds-dss__note{font:11px var(--font-data,monospace);color:var(--text-disabled,#A4ABB2);}
@keyframes mds-dss-sweep{100%{transform:translateX(100%);}}
@media (prefers-reduced-motion:reduce){.mds-dss__bar::after{animation:none;}}
`;
  document.head.appendChild(el);
}

const WIDTHS = ["72%", "46%", "58%", "40%", "63%", "51%"];

export function DataStateSwitch({
  state = "ready",
  label = "This surface",
  rows = 8,
  columns = 6,
  detail,
  actionLabel,
  onAction,
  announce,
  children,
}) {
  if (!state || state === "ready") return <React.Fragment>{children}</React.Fragment>;
  inject();
  const mode = announce || (state === "error" ? "assertive" : "polite");
  const live = mode === "off" ? {} : { role: mode === "assertive" ? "alert" : "status", "aria-live": mode };
  const cols = `repeat(${columns}, 1fr)`;
  const copy = {
    loading: {
      glyph: "\u2026",
      title: `Loading ${label.toLowerCase()}`,
      detail: detail || "Fetching the working set. Column shape is known, values are not — the layout does not move when data lands.",
    },
    empty: {
      glyph: "\u2205",
      title: `No ${label.toLowerCase()} to show`,
      detail: detail || "The query ran and returned nothing. Widen the period, clear a filter, or pick a different account.",
    },
    error: {
      glyph: "!",
      title: `${label} could not load`,
      detail: detail || "The upstream request failed. Nothing shown is stale — the surface refuses to render partial state.",
    },
  }[state] || {};
  return (
    <div className={`mds-dss mds-dss--${state}`} {...live}>
      <div className="mds-dss__hd">
        <span className="mds-dss__eyebrow">{label}</span>
        <span className="mds-dss__state">{state}</span>
      </div>
      <div className="mds-dss__body">
        {state === "loading" ? (
          <div className="mds-dss__bars">
            {Array.from({ length: rows }).map((_, r) => (
              <div key={r} className="mds-dss__row" style={{ gridTemplateColumns: cols }}>
                {Array.from({ length: columns }).map((_, c) => (
                  <span key={c} className="mds-dss__bar" style={{ width: WIDTHS[(r + c) % WIDTHS.length] }} />
                ))}
              </div>
            ))}
            <span className="mds-dss__note" style={{ marginTop: 6 }}>{copy.title}</span>
          </div>
        ) : (
          <div className="mds-dss__center">
            <span className="mds-dss__glyph" aria-hidden="true">{copy.glyph}</span>
            <div className="mds-dss__title">{copy.title}</div>
            <div className="mds-dss__detail">{copy.detail}</div>
            {actionLabel && (
              <button type="button" className="mds-dss__btn" onClick={onAction}>{actionLabel}</button>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
