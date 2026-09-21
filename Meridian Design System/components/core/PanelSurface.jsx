// Meridian panel/card — mirrors CardStyle (white paper, 1px border, radius 8, padding 20,
// whisper shadow) and CompactCardStyle. The outermost surface primitive.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-panel{background:var(--card-surface,#FBFAF8);border:1px solid var(--border,#E4E3DE);}
.mds-panel--raised{background:var(--card-surface-raised,#F6F4F0);}
.mds-panel--elevated{border:1px solid var(--border,#E4E3DE);}
.mds-panel--flat{border:none;}
.mds-panel--strong{border-color:var(--border-strong,#AFABA1);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "panel");
  el.textContent = css;
  document.head.appendChild(el);
}

export function PanelSurface({ raised = false, elevated = false, flat = false, strong = false, className = "", children, ...rest }) {
  inject();
  const cls = `mds-panel${raised ? " mds-panel--raised" : ""}${elevated ? " mds-panel--elevated" : ""}` +
    `${flat ? " mds-panel--flat" : ""}${strong ? " mds-panel--strong" : ""}${className ? " " + className : ""}`;
  return <div className={cls} {...rest}>{children}</div>;
}
