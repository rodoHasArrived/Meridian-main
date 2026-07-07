// Meridian Toolbar — the standard band above a data surface: search/filters at the start,
// actions at the end. One consistent surface (mirrors the FilteredDataTable toolbar
// treatment) instead of per-screen hand-rolled bands.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-toolbar{display:flex;align-items:center;gap:12px;padding:8px 12px;flex-wrap:wrap;
  background:var(--bg-medium,#EBEFF4);border:1px solid var(--border,#CBD3DC);}
.mds-toolbar__group{display:flex;align-items:center;gap:8px;min-width:0;}
.mds-toolbar__spacer{flex:1;}
.mds-toolbar__divider{width:1px;align-self:stretch;margin:2px 0;background:var(--border-divider,#D2D9E2);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "toolbar");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Toolbar({ children, className = "", style = {}, ...rest }) {
  inject();
  return (
    <div className={`mds-toolbar${className ? " " + className : ""}`} role="toolbar" style={style} {...rest}>
      {children}
    </div>
  );
}

export function ToolbarGroup({ children, style = {}, ...rest }) {
  return <div className="mds-toolbar__group" style={style} {...rest}>{children}</div>;
}

export function ToolbarSpacer() {
  return <div className="mds-toolbar__spacer" />;
}

export function ToolbarDivider() {
  return <div className="mds-toolbar__divider" role="separator" aria-orientation="vertical" />;
}
