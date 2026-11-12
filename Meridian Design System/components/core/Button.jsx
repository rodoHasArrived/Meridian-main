// Meridian button — functional only. No radius, no transitions, no opacity tricks.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-btn{display:inline-flex;align-items:center;justify-content:center;gap:8px;
  padding:8px 16px;height:32px;border:1px solid;
  font-family:var(--font-body);font-size:13px;cursor:pointer;}
.mds-btn:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:var(--focus-ring-offset,2px);}
.mds-btn:disabled{cursor:not-allowed;background:var(--bg-medium,#EBE8E1);border-color:var(--border,#D2CDC3);color:var(--text-disabled,#A4ABB2);}
/* sizes */
.mds-btn--sm{padding:4px 12px;height:26px;font-size:12px;}
.mds-btn--lg{padding:12px 20px;height:40px;font-size:14px;}
.mds-btn--icon{padding:0;width:32px;height:32px;}
/* busy spinner — self-contained (never depend on Spinner.jsx having rendered) */
.mds-btn__spin{display:inline-block;flex:0 0 auto;width:12px;height:12px;border-radius:50%;
  border:2px solid color-mix(in srgb,currentColor 35%,transparent);border-top-color:currentColor;
  animation:mds-btn-spin-rot .7s linear infinite;}
@keyframes mds-btn-spin-rot{to{transform:rotate(360deg);}}
@media (prefers-reduced-motion:reduce){.mds-btn__spin{animation-duration:1.6s;}}
/* primary — structural graphite/paper action; terracotta remains a brand/scenario signal. */
.mds-btn--primary{background:var(--brand-axis,#29323A);border-color:var(--brand-axis,#29323A);color:var(--ink-inverse,#F5F3EE);font-weight:600;}
.mds-btn--primary:hover{background:color-mix(in srgb,var(--brand-axis,#29323A) 88%,var(--bg-light,#F8F7F3));border-color:var(--brand-axis,#29323A);}
.mds-btn--primary:active{background:color-mix(in srgb,var(--brand-axis,#29323A) 78%,var(--bg-light,#F8F7F3));border-color:var(--brand-axis,#29323A);}
/* ghost — hairline border, secondary action */
.mds-btn--ghost{background:var(--bg-light,#F8F7F3);border-color:var(--border,#D2CDC3);color:var(--text-primary,#20252B);}
.mds-btn--ghost:hover{background:var(--bg-hover,#EDEFF0);border-color:var(--border-hover,#BEB8AD);}
.mds-btn--ghost:active{background:var(--bg-active,#E7EDF2);border-color:var(--border-strong,#AAA49A);}
/* danger — red text, ghost styling */
.mds-btn--danger{background:var(--bg-light,#F8F7F3);border-color:var(--border,#D2CDC3);color:var(--red,#B54B42);font-weight:600;}
.mds-btn--danger:hover{background:var(--red-a10,rgba(181,75,66,.10));border-color:var(--red,#B54B42);}
.mds-btn--danger:active{background:var(--red-a20,rgba(181,75,66,.20));border-color:var(--red-dim,#883832);}
/* link — text-only */
.mds-btn--link{background:transparent;border:none;padding:0;color:var(--border-focus,#2D6FB3);font-weight:600;font-size:12px;}
.mds-btn--link:hover{text-decoration:underline;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "button");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Button({
  variant = "primary", size = "default", icon, busy = false, busyLabel = null,
  // Explicit by default: an unnamed <button> inside a form submits it (4.1's DatePicker defect).
  type = "button", disabled = false, className = "", children, ...rest
}) {
  inject();
  const cls = `mds-btn${size !== "default" ? ` mds-btn--${size}` : ""} mds-btn--${variant}${className ? " " + className : ""}`;
  return (
    <button type={type} className={cls} disabled={disabled || busy} aria-busy={busy || undefined} {...rest}>
      {busy && <span className="mds-btn__spin" aria-hidden="true" />}
      {!busy && icon && <span style={{ display: "inline-flex" }}>{icon}</span>}
      {busy ? (busyLabel ?? children) : children}
    </button>
  );
}
