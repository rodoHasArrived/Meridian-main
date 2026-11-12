// Meridian ProgressBar — determinate (value 0–100) or indeterminate. For batch ops,
// reconciliation runs, sync status, and any bounded ratio (utilisation, coverage, funded
// share) — the one bar in the system: no radial dial, no second linear variant. Thin,
// squared, semantic. `target` draws a dotted datum at the expected/threshold ratio so a
// bounded value can be read against its reference. Motion only when indeterminate.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-prog{display:flex;flex-direction:column;gap:5px;width:100%;font-family:var(--font-body);}
.mds-prog__head{display:flex;align-items:baseline;justify-content:space-between;gap:8px;}
.mds-prog__label{font-size:12px;color:var(--text-secondary,#535C65);}
.mds-prog__value{font-family:var(--font-data,monospace);font-size:12px;font-weight:600;
  color:var(--text-primary,#20252B);letter-spacing:var(--letter-spacing-data,.01em);}
.mds-prog__track{position:relative;width:100%;height:6px;overflow:hidden;
  background:var(--bg-active,#E7EDF2);border-radius:var(--radius-chip,3px);}
.mds-prog--sm .mds-prog__track{height:4px;}
.mds-prog--lg .mds-prog__track{height:10px;}
.mds-prog__fill{position:absolute;top:0;left:0;height:100%;border-radius:inherit;
  background:var(--brand-signal,#C56343);}
.mds-prog--success .mds-prog__fill{background:var(--green,#2E7655);}
.mds-prog--warning .mds-prog__fill{background:var(--orange,#986717);}
.mds-prog--danger  .mds-prog__fill{background:var(--red,#B54B42);}
.mds-prog__fill--indet{width:35%;animation:mds-prog-indet 1.1s ease-in-out infinite;}
/* the datum overhangs the track, so it lives OUTSIDE the clipped track element */
.mds-prog__bar{position:relative;width:100%;}
.mds-prog__target{position:absolute;top:-3px;bottom:-3px;width:0;
  border-left:2px dotted var(--brand-axis,#29323A);}
@keyframes mds-prog-indet{0%{left:-35%;}100%{left:100%;}}
@media (prefers-reduced-motion:reduce){.mds-prog__fill--indet{animation:none;left:0;width:100%;opacity:.4;}}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "progressbar");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ProgressBar({
  value = null, max = 100, target = null, label, showValue = false, variant = "accent",
  size = "md", className = "", "aria-label": ariaLabel, ...rest
}) {
  inject();
  const indeterminate = value === null || value === undefined;
  const scale = (v) => Math.max(0, Math.min(100, (v / (max || 100)) * 100));
  const pct = indeterminate ? 0 : scale(value);
  const targetPct = target === null || target === undefined ? null : scale(target);
  const variantClass = variant && variant !== "accent" ? ` mds-prog--${variant}` : "";
  return (
    <div className={`mds-prog mds-prog--${size}${variantClass}${className ? " " + className : ""}`} {...rest}>
      {(label || showValue) && (
        <div className="mds-prog__head">
          {label && <span className="mds-prog__label">{label}</span>}
          {showValue && !indeterminate && <span className="mds-prog__value">{Math.round(pct)}%</span>}
        </div>
      )}
      <div className="mds-prog__bar">
        <div className="mds-prog__track" role="progressbar"
          aria-label={ariaLabel || label || "Progress"}
          aria-valuenow={indeterminate ? undefined : Math.round(pct)}
          aria-valuemin={0} aria-valuemax={100}
          aria-valuetext={indeterminate || targetPct === null ? undefined
            : `${Math.round(pct)}% of target ${Math.round(targetPct)}%`}>
          <div className={`mds-prog__fill${indeterminate ? " mds-prog__fill--indet" : ""}`}
            style={indeterminate ? undefined : { width: pct + "%" }} />
        </div>
        {targetPct === null ? null : (
          <span className="mds-prog__target" style={{ left: targetPct + "%" }} aria-hidden="true" />
        )}
      </div>
    </div>
  );
}
