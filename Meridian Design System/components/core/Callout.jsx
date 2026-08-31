// Meridian Callout — inline messaging block (info / success / warning / danger).
// Distinct from StatusBanner (run results) — this is for prose guidance inside content.
// Functional only: desaturated alpha-wash fill, solid left rule, no radius.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-callout{display:flex;gap:10px;padding:12px 14px;border:1px solid var(--border,#D7DCE2);
  border-left-width:3px;background:var(--bg-light,#FAFBFC);font-family:var(--font-body);}
.mds-callout-icon{flex:0 0 auto;font-size:14px;line-height:20px;}
.mds-callout-body{flex:1;min-width:0;}
.mds-callout-title{font-size:13px;font-weight:600;color:var(--text-primary,#22272E);
  margin:0 0 2px;line-height:20px;}
.mds-callout-text{font-size:13px;line-height:20px;color:var(--text-secondary,#4D5967);margin:0;}
.mds-callout--info{border-left-color:var(--accent,#2F6F8F);background:var(--blue-a10,rgba(45,95,127,.10));}
.mds-callout--info .mds-callout-icon{color:var(--accent,#2F6F8F);}
.mds-callout--success{border-left-color:var(--green,#16885F);background:var(--green-a10,rgba(27,126,92,.10));}
.mds-callout--success .mds-callout-icon{color:var(--green,#16885F);}
.mds-callout--warning{border-left-color:var(--orange,#8A520E);background:var(--orange-a10,rgba(168,111,26,.10));}
.mds-callout--warning .mds-callout-icon{color:var(--orange,#8A520E);}
.mds-callout--danger{border-left-color:var(--red,#BA3F55);background:var(--red-a10,rgba(166,61,74,.10));}
.mds-callout--danger .mds-callout-icon{color:var(--red,#BA3F55);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "callout");
  el.textContent = css;
  document.head.appendChild(el);
}

const ICONS = { info: "ℹ", success: "✓", warning: "▲", danger: "✕" };

export function Callout({ tone = "info", title, children, icon }) {
  inject();
  return (
    <div className={`mds-callout mds-callout--${tone}`} role="note">
      <span className="mds-callout-icon" aria-hidden="true">{icon ?? ICONS[tone]}</span>
      <div className="mds-callout-body">
        {title && <p className="mds-callout-title">{title}</p>}
        {children && <p className="mds-callout-text">{children}</p>}
      </div>
    </div>
  );
}

Callout.displayName = "Callout";
