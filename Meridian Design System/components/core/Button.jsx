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
.mds-btn:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:var(--focus-ring-offset,2px);}
.mds-btn:disabled{cursor:not-allowed;background:var(--bg-medium,#F5F7FA);border-color:var(--border,#D7DCE2);color:var(--text-disabled,#889099);}
/* sizes */
.mds-btn--sm{padding:6px 12px;font-size:12px;}
.mds-btn--lg{padding:12px 20px;height:40px;font-size:14px;}
.mds-btn--icon{padding:0;width:32px;height:32px;}
/* primary — solid accent, white text (hover/press track the white-label brand + dark) */
.mds-btn--primary{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);color:var(--text-on-accent,#fff);font-weight:600;}
.mds-btn--primary:hover{background:var(--accent-hover,#3B82A6);border-color:var(--accent-hover,#3B82A6);}
.mds-btn--primary:active{background:var(--accent-dim,#255B75);border-color:var(--accent-dim,#255B75);}
/* ghost — hairline border, secondary action */
.mds-btn--ghost{background:var(--bg-light,#fff);border-color:var(--border,#D7DCE2);color:var(--text-primary,#22272E);}
.mds-btn--ghost:hover{background:var(--bg-hover,#F1F4F7);border-color:var(--border-hover,#B8C2CC);}
.mds-btn--ghost:active{background:var(--bg-active,#E1EAF2);border-color:var(--border-strong,#AAB4BF);}
/* danger — red text, ghost styling */
.mds-btn--danger{background:var(--bg-light,#fff);border-color:var(--border,#D7DCE2);color:var(--red,#BA3F55);font-weight:600;}
.mds-btn--danger:hover{background:var(--red-a10,rgba(166,61,74,.10));border-color:var(--red,#BA3F55);}
.mds-btn--danger:active{background:var(--red-a20,rgba(166,61,74,.20));border-color:var(--red-dim,#8C2F40);}
/* link — text-only */
.mds-btn--link{background:transparent;border:none;padding:0;color:var(--accent,#2F6F8F);font-weight:600;font-size:12px;}
.mds-btn--link:hover{text-decoration:underline;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "button");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Button({
  variant = "primary", size = "default", icon, busy = false, busyLabel = null,
  disabled = false, className = "", children, ...rest
}) {
  inject();
  const cls = `mds-btn mds-btn--${size} mds-btn--${variant}${className ? " " + className : ""}`;
  return (
    <button className={cls} disabled={disabled || busy} aria-busy={busy || undefined} {...rest}>
      {busy && <span className="mds-spin" aria-hidden="true" />}
      {!busy && icon && <span style={{ display: "inline-flex" }}>{icon}</span>}
      {busy ? (busyLabel ?? children) : children}
    </button>
  );
}
