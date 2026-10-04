// Meridian Spinner — async loading indicator. Minimal ring, semantic stroke. Motion is the
// whole function here, so it animates (but respects prefers-reduced-motion).
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-spin{display:inline-block;flex:0 0 auto;border-radius:50%;
  border:2px solid var(--border,#E4E3DE);border-top-color:var(--accent,#A85436);
  animation:mds-spin-rot .7s linear infinite;}
.mds-spin--sm{width:14px;height:14px;border-width:2px;}
.mds-spin--md{width:20px;height:20px;border-width:2px;}
.mds-spin--lg{width:32px;height:32px;border-width:3px;}
.mds-spin--muted{border-top-color:var(--text-muted,#5E666F);}
.mds-spin--onDark{border-color:color-mix(in srgb, var(--topbar-text,#F4F2ED) 25%, transparent);border-top-color:var(--topbar-text,#F4F2ED);}
.mds-spin-row{display:inline-flex;align-items:center;gap:8px;font-family:var(--font-body);
  font-size:13px;color:var(--text-secondary,#4E5258);}
@keyframes mds-spin-rot{to{transform:rotate(360deg);}}
@media (prefers-reduced-motion:reduce){.mds-spin{animation-duration:1.6s;}}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "spinner");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Spinner({ size = "md", variant = "accent", label, className = "", ...rest }) {
  inject();
  const ring = (
    <span className={`mds-spin mds-spin--${size}${variant !== "accent" ? " mds-spin--" + variant : ""}${className ? " " + className : ""}`}
      role="status" aria-label={label || "Loading"} {...rest} />
  );
  if (!label) return ring;
  return (
    <span className="mds-spin-row">
      {ring}
      <span>{label}</span>
    </span>
  );
}
