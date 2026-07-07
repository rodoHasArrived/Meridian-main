// Meridian DensityToggle — the operator-facing control for the density token scopes
// (body[data-theme-density="terminal" | "compact" | "spacious"]). Terminal is the densest
// scope for multi-monitor ops walls; compact tightens rows/padding for dense monitoring;
// spacious loosens for review. Writes the attribute to a target element (document.body by
// default) and can persist the choice. Segmented, flat, no transitions.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-den{display:inline-flex;align-items:stretch;padding:2px;gap:2px;
  background:var(--bg-medium,#F5F7FA);border:1px solid var(--border,#D7DCE2);
  border-radius:var(--radius-button,2px);font-family:var(--font-body);}
.mds-den--full{display:flex;width:100%;}
.mds-den__btn{flex:1;display:inline-flex;align-items:center;justify-content:center;gap:6px;
  padding:5px 12px;font-size:12px;font-weight:500;line-height:1;white-space:nowrap;cursor:pointer;
  color:var(--text-secondary,#4D5967);background:transparent;border:none;border-radius:var(--radius-button,2px);}
.mds-den__btn:hover:not(.mds-den__btn--active){color:var(--text-primary,#22272E);background:var(--bg-hover,#F1F4F7);}
.mds-den__btn--active{color:var(--text-primary,#22272E);background:var(--bg-light,#FFFFFF);
  font-weight:600;box-shadow:inset 0 0 0 1px var(--border,#D7DCE2);}
.mds-den__btn:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-den--sm .mds-den__btn{padding:3px 9px;font-size:11px;}
.mds-den__glyph{display:inline-flex;flex-direction:column;gap:2px;width:11px;}
.mds-den__glyph i{display:block;height:1.5px;background:currentColor;border-radius:1px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "density-toggle");
  el.textContent = css;
  document.head.appendChild(el);
}

const LEVELS = [
  { value: "terminal", label: "Terminal", gap: 0.5 },
  { value: "compact",  label: "Compact",  gap: 1 },
  { value: "default",  label: "Default",  gap: 2 },
  { value: "spacious", label: "Spacious", gap: 4 },
];

function Glyph({ gap }) {
  return (
    <span className="mds-den__glyph" aria-hidden="true" style={{ gap }}>
      <i /><i /><i />
    </span>
  );
}

function resolveTarget(target) {
  if (typeof document === "undefined") return null;
  if (!target || target === "body") return document.body;
  if (target === "root" || target === "html") return document.documentElement;
  if (typeof target === "string") return document.querySelector(target);
  if (target && target.current) return target.current; // ref
  return target;
}

function applyDensity(el, value) {
  if (!el) return;
  if (value === "default") el.removeAttribute("data-theme-density");
  else el.setAttribute("data-theme-density", value);
}

export function DensityToggle({
  value: controlled,
  defaultValue = "default",
  onChange,
  target = "body",
  apply = true,
  persist = null, // localStorage key, or null
  showLabels = true,
  size = "md",
  fullWidth = false,
  className = "",
  ...rest
}) {
  inject();
  const isControlled = controlled != null;
  const [internal, setInternal] = React.useState(() => {
    if (isControlled) return controlled;
    if (persist && typeof localStorage !== "undefined") {
      const saved = localStorage.getItem(persist);
      if (saved) return saved;
    }
    return defaultValue;
  });
  const value = isControlled ? controlled : internal;

  React.useEffect(() => {
    if (!apply) return;
    applyDensity(resolveTarget(target), value);
  }, [value, apply, target]);

  const select = (v) => {
    if (!isControlled) setInternal(v);
    if (persist && typeof localStorage !== "undefined") localStorage.setItem(persist, v);
    onChange && onChange(v);
  };

  return (
    <div role="radiogroup" aria-label="Display density"
      className={`mds-den mds-den--${size}${fullWidth ? " mds-den--full" : ""}${className ? " " + className : ""}`}
      {...rest}>
      {LEVELS.map((lv) => {
        const active = lv.value === value;
        return (
          <button key={lv.value} type="button" role="radio" aria-checked={active}
            title={lv.label}
            className={`mds-den__btn${active ? " mds-den__btn--active" : ""}`}
            onClick={() => select(lv.value)}>
            <Glyph gap={lv.gap} />
            {showLabels && <span>{lv.label}</span>}
          </button>
        );
      })}
    </div>
  );
}

DensityToggle.displayName = "DensityToggle";
