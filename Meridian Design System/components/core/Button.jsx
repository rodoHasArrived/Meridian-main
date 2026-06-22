// Meridian button — mirrors PrimaryButtonStyle / GhostButtonStyle / LinkButtonStyle
// in src/Meridian.Wpf/Styles/ThemeControls.xaml. Light institutional, 6px radius,
// solid teal-blue primary (one per screen), paper ghost secondary. No glow.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-btn{display:inline-flex;align-items:center;justify-content:center;gap:8px;
  border:1px solid transparent;border-radius:var(--radius-button,6px);
  font-family:var(--font-body);font-size:13px;cursor:pointer;
  transition:background-color .12s ease,border-color .12s ease,color .12s ease;}
.mds-btn:focus-visible{outline:none;border-color:var(--border-focus,#2F6F8F);
  box-shadow:0 0 0 2px rgba(47,111,143,.30);}
.mds-btn:disabled{cursor:not-allowed;opacity:.45;}
/* sizes */
.mds-btn--sm{padding:6px 12px;font-size:12px;}
.mds-btn--default{padding:9px 16px;}
.mds-btn--icon{padding:0;width:32px;height:32px;}
/* primary — solid accent, white text, SemiBold */
.mds-btn--primary{background:var(--accent,#2F6F8F);border-color:var(--accent,#2F6F8F);
  color:#fff;font-weight:600;}
.mds-btn--primary:hover{background:#3477978f;background:rgba(47,111,143,.80);border-color:var(--border-focus,#2F6F8F);}
.mds-btn--primary:active{background:var(--accent-dim,#255B75);border-color:var(--accent-dim,#255B75);}
/* secondary — light accent fill with teal border, no hover state change */
.mds-btn--secondary{background:var(--blue-a10,#E1EAF2);border-color:var(--accent,#2F6F8F);
  color:var(--text-primary,#22272E);font-weight:600;}
.mds-btn--secondary:hover{opacity:.95;}
.mds-btn--secondary:active{background:var(--blue-a20,#D1E0EC);border-color:var(--accent-dim,#255B75);}
/* ghost — paper surface, hairline border, tertiary action */
.mds-btn--ghost{background:var(--bg-light,#fff);border-color:var(--border,#D7DCE2);
  color:var(--text-primary,#22272E);font-weight:400;}
.mds-btn--ghost:hover{background:var(--bg-hover,#F1F4F7);border-color:var(--border-hover,#B8C2CC);}
.mds-btn--ghost:active{background:var(--bg-active,#E6EEF5);border-color:var(--border-focus,#2F6F8F);}
/* danger — ghost that resolves to red on intent */
.mds-btn--danger{background:var(--bg-light,#fff);border-color:var(--border,#D7DCE2);
  color:var(--red,#BA3F55);font-weight:600;}
.mds-btn--danger:hover{background:var(--red-a10,rgba(186,63,85,.10));border-color:var(--red,#BA3F55);}
.mds-btn--danger:active{background:var(--red-a20,rgba(186,63,85,.20));}
/* link — text-only info action */
.mds-btn--link{background:transparent;border-color:transparent;padding-left:0;padding-right:0;
  color:var(--accent,#2F6F8F);font-weight:600;font-size:12px;}
.mds-btn--link:hover{text-decoration:underline;}
.mds-spin{height:14px;width:14px;border:2px solid currentColor;border-right-color:transparent;
  border-radius:50%;display:inline-block;animation:mds-spin .7s linear infinite;}
@keyframes mds-spin{to{transform:rotate(360deg);}}
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
