// Meridian TimeframeSwitcher + AsOfControl — chart-furniture pair for the analysis surfaces.
// TimeframeSwitcher: mono segmented resolution picker (1m … 1W), one active value.
// AsOfControl: the session clock — Live (green dot, ticking UTC) ⇄ As-of (frozen stamp,
// amber AS-OF badge, datetime input). Every chart toolbar states its time basis explicitly.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-tf{display:inline-flex;align-items:stretch;padding:2px;gap:2px;background:var(--bg-medium,#F3F6F9);
  border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-button,2px);
  font-family:var(--font-data,'Cascadia Mono',monospace);}
.mds-tf__btn{display:inline-flex;align-items:center;justify-content:center;padding:3px 8px;cursor:pointer;
  font-family:inherit;font-size:11px;font-weight:600;line-height:1.3;white-space:nowrap;
  color:var(--text-secondary,#4D5967);background:transparent;border:none;border-radius:var(--radius-button,2px);}
.mds-tf__btn:hover:not(.mds-tf__btn--active){color:var(--text-primary,#22272E);background:var(--bg-hover,#EAEEF3);}
.mds-tf__btn--active{color:var(--text-primary,#22272E);background:var(--bg-light,#FFFFFF);
  box-shadow:inset 0 0 0 1px var(--accent,#2F6F8F);}
.mds-tf__btn:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
.mds-tf--md .mds-tf__btn{padding:5px 11px;font-size:12px;}
.mds-asof{display:inline-flex;align-items:center;gap:8px;height:26px;box-sizing:border-box;
  border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-chip,2px);background:var(--bg-light,#FFFFFF);
  padding:0 4px 0 9px;font-family:var(--font-data,'Cascadia Mono',monospace);font-size:11.5px;
  color:var(--text-primary,#22272E);white-space:nowrap;}
.mds-asof--frozen{border-color:var(--orange,#8A520E);background:var(--orange-a10,rgba(138,82,14,.08));}
.mds-asof__dot{width:7px;height:7px;border-radius:50%;background:var(--green,#16885F);flex:0 0 auto;}
.mds-asof__label{font-weight:700;font-size:10px;letter-spacing:.05em;color:var(--green-dim,#10663F);}
.mds-asof--frozen .mds-asof__label{color:var(--orange-dim,#683E0B);}
.mds-asof__time{font-variant-numeric:tabular-nums;}
.mds-asof__input{height:20px;box-sizing:border-box;border:1px solid var(--border,#CBD3DC);
  border-radius:var(--radius-input,2px);background:var(--bg-light,#FFFFFF);color:var(--text-primary,#22272E);
  font-family:inherit;font-size:10.5px;padding:0 4px;}
.mds-asof__input:focus-visible{outline:var(--focus-ring);outline-offset:-1px;}
.mds-asof__btn{height:20px;box-sizing:border-box;display:inline-flex;align-items:center;cursor:pointer;
  border:1px solid var(--border,#CBD3DC);border-radius:var(--radius-button,2px);background:transparent;
  font-family:var(--font-body);font-size:10px;font-weight:600;padding:0 7px;color:var(--text-secondary,#4D5967);}
.mds-asof__btn:hover{border-color:var(--border-hover,#ADB8C4);background:var(--bg-hover,#EAEEF3);color:var(--text-primary,#22272E);}
.mds-asof__btn:focus-visible{outline:var(--focus-ring);outline-offset:var(--focus-ring-offset);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "timeframe-asof");
  el.textContent = css;
  document.head.appendChild(el);
}

const DEFAULT_TFS = ["1m", "5m", "15m", "1h", "1D", "1W"];
function pad(n) { return String(n).padStart(2, "0"); }
function utcStamp(d, withDate) {
  const t = `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}:${pad(d.getUTCSeconds())}Z`;
  if (!withDate) return t;
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())} ${t}`;
}
function toLocalInput(d) {
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1)}-${pad(d.getUTCDate())}T${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

export function TimeframeSwitcher({
  options = DEFAULT_TFS,
  value,
  defaultValue,
  onChange,
  size = "sm",
  className = "",
  ...rest
}) {
  inject();
  const opts = options.map((o) => (typeof o === "string" ? { value: o, label: o } : o));
  const isControlled = value != null;
  const [internal, setInternal] = React.useState(defaultValue != null ? defaultValue : (opts[0] || {}).value);
  const current = isControlled ? value : internal;
  const select = (v) => {
    if (!isControlled) setInternal(v);
    onChange && onChange(v);
  };
  return (
    <div role="radiogroup" aria-label="Timeframe"
      className={`mds-tf mds-tf--${size}${className ? " " + className : ""}`} {...rest}>
      {opts.map((o) => (
        <button key={o.value} type="button" role="radio" aria-checked={o.value === current}
          className={`mds-tf__btn${o.value === current ? " mds-tf__btn--active" : ""}`}
          onClick={() => select(o.value)}>
          {o.label}
        </button>
      ))}
    </div>
  );
}
TimeframeSwitcher.displayName = "TimeframeSwitcher";

export function AsOfControl({
  mode: controlledMode,
  defaultMode = "live",
  asOf,
  onChange,
  withDate = true,
  className = "",
  ...rest
}) {
  inject();
  const isControlled = controlledMode != null;
  const [internalMode, setInternalMode] = React.useState(defaultMode);
  const mode = isControlled ? controlledMode : internalMode;
  const [frozen, setFrozen] = React.useState(() => (asOf != null ? new Date(asOf) : new Date()));
  const [, tick] = React.useReducer((n) => n + 1, 0);

  React.useEffect(() => {
    if (mode !== "live") return;
    const t = setInterval(tick, 1000);
    return () => clearInterval(t);
  }, [mode]);
  React.useEffect(() => {
    if (asOf != null) setFrozen(new Date(asOf));
  }, [asOf]);

  const set = (nextMode, nextAsOf) => {
    if (!isControlled) setInternalMode(nextMode);
    if (nextAsOf) setFrozen(nextAsOf);
    onChange && onChange({ mode: nextMode, asOf: nextMode === "asof" ? (nextAsOf || frozen) : null });
  };

  const live = mode === "live";
  const now = new Date();
  return (
    <div className={`mds-asof${live ? "" : " mds-asof--frozen"}${className ? " " + className : ""}`}
      role="group" aria-label="Time basis" {...rest}>
      {live ? (
        <React.Fragment>
          <span className="mds-asof__dot" aria-hidden="true" />
          <span className="mds-asof__label">LIVE</span>
          <span className="mds-asof__time">{utcStamp(now, withDate)}</span>
          <button type="button" className="mds-asof__btn" onClick={() => set("asof", new Date())}>Freeze</button>
        </React.Fragment>
      ) : (
        <React.Fragment>
          <span className="mds-asof__label">AS-OF</span>
          <input type="datetime-local" className="mds-asof__input" aria-label="As-of time (UTC)"
            value={toLocalInput(frozen)}
            onChange={(e) => {
              const d = new Date(e.target.value + "Z");
              if (!isNaN(d.getTime())) set("asof", d);
            }} />
          <span className="mds-asof__time" title={utcStamp(frozen, true)}>UTC</span>
          <button type="button" className="mds-asof__btn" onClick={() => set("live")}>Return to live</button>
        </React.Fragment>
      )}
    </div>
  );
}
AsOfControl.displayName = "AsOfControl";
