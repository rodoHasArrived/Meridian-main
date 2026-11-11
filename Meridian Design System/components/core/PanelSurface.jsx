// Meridian panel/card — mirrors CardStyle (white paper, 1px border, radius 8, padding 20,
// whisper shadow) and CompactCardStyle. The outermost surface primitive.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-panel{background:var(--card-surface,#F8F7F3);border:1px solid var(--border,#D2CDC3);}
.mds-panel--raised{background:var(--card-surface-raised,#FCFBF8);}
.mds-panel--elevated{border:1px solid var(--border,#D2CDC3);}
.mds-panel--flat{border:none;}
.mds-panel--strong{border-color:var(--border-strong,#AAA49A);}
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
