// Meridian connection state — the last-known-good contract made visible.
// live · delayed · partial · offline · error. The rule the strip enforces:
// a view that has rendered valid data never reverts to an empty skeleton;
// it keeps the evidence and states its age, what is missing, and the recovery.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-link{display:flex;align-items:center;gap:8px;min-height:24px;padding:3px 8px;
  border:1px solid var(--border);border-radius:var(--radius-chip,2px);background:var(--bg-light);
  font-family:var(--font-body);font-size:11px;color:var(--text-secondary);width:fit-content;max-width:100%;}
.mds-link--block{width:100%;}
.mds-link__dot{width:7px;height:7px;border-radius:50%;flex:0 0 auto;}
.mds-link__label{font-weight:var(--weight-semibold,600);letter-spacing:.01em;}
.mds-link__meta{font-family:var(--font-data);font-size:10.5px;color:var(--text-muted);
  white-space:nowrap;overflow:hidden;text-overflow:ellipsis;}
.mds-link__sep{color:var(--text-muted);opacity:.55;}
.mds-link__act{margin-left:auto;font:inherit;font-size:11px;font-weight:var(--weight-medium,500);
  color:var(--link,#2A5F96);background:none;border:0;padding:0 2px;cursor:pointer;text-decoration:underline;
  text-underline-offset:2px;}
.mds-link__act:focus-visible{outline:2px solid var(--border-focus);outline-offset:2px;}
.mds-link--live .mds-link__dot{background:var(--link-live-fg);}
.mds-link--live .mds-link__label{color:var(--link-live-fg);}
.mds-link--delayed .mds-link__dot{background:var(--link-delayed-fg);}
.mds-link--delayed .mds-link__label{color:var(--link-delayed-fg);}
.mds-link--partial .mds-link__dot{background:var(--link-partial-fg);}
.mds-link--partial .mds-link__label{color:var(--link-partial-fg);}
.mds-link--offline .mds-link__dot{background:var(--link-offline-fg);}
.mds-link--offline .mds-link__label{color:var(--text-secondary);}
.mds-link--error{border-color:var(--cond-exception-bd);background:var(--cond-exception-bg);}
.mds-link--error .mds-link__dot{background:var(--link-error-fg);}
.mds-link--error .mds-link__label,.mds-link--error .mds-link__meta{color:var(--text-primary);}
.mds-link--live .mds-link__dot{box-shadow:0 0 0 3px color-mix(in srgb,var(--link-live-fg) 16%,transparent);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "connection-state");
  el.textContent = css;
  document.head.appendChild(el);
}

const COPY = {
  unknown: { label: "Unknown", verb: "last known" },
  live:    { label: "Live",    verb: "updated" },
  delayed: { label: "Delayed", verb: "last update" },
  partial: { label: "Partial", verb: "as of" },
  offline: { label: "Offline", verb: "cached" },
  error:   { label: "Error",   verb: "last good" },
};

export function ConnectionState({
  state = "live", subject, age, cadence, detail, actionLabel, onAction, block = false, className = "", ...rest
}) {
  inject();
  const key = COPY[state] ? state : "unknown";
  const c = COPY[key];
  const meta = [
    age ? `${c.verb} ${age}` : null,
    cadence ? `every ${cadence}` : null,
    detail || null,
  ].filter(Boolean);
  return (
    <div
      className={`mds-link mds-link--${key}${block ? " mds-link--block" : ""}${className ? " " + className : ""}`}
      role="status"
      aria-live={key === "error" ? "assertive" : "polite"}
      {...rest}
    >
      <span className="mds-link__dot" aria-hidden="true" />
      <span className="mds-link__label">{subject ? `${subject} ${c.label.toLowerCase()}` : c.label}</span>
      {meta.length ? <span className="mds-link__sep" aria-hidden="true">·</span> : null}
      {meta.length ? <span className="mds-link__meta">{meta.join(" · ")}</span> : null}
      {onAction ? (
        <button type="button" className="mds-link__act" onClick={onAction}>{actionLabel || "Retry"}</button>
      ) : null}
    </div>
  );
}
