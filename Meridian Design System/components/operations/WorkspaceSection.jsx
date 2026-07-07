// Meridian workspace section — mirrors `.workspace-section-band`. A sticky section
// header (title + summary on the left, optional jump affordance on the right) over a
// content body. Used to band an operator workspace into labelled, jump-to-able
// regions that keep their heading pinned while the region scrolls.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-wsection{display:grid;gap:12px;scroll-margin-top:12px;min-width:0;}
.mds-wsection__head{position:sticky;top:0;z-index:15;display:flex;flex-wrap:wrap;align-items:center;
  justify-content:space-between;gap:10px;min-width:0;
  border:1px solid var(--border,#D7DCE2);border-radius:var(--radius-card,2px);background:var(--bg-medium,#F5F7FA);
  box-shadow:var(--shadow-soft,0 1px 1px rgba(0,0,0,.06));padding:9px 12px;}
.mds-wsection__heading{min-width:0;}
.mds-wsection__title{margin:0;color:var(--text-primary,#22272E);font-size:13px;font-weight:700;line-height:1.2;}
.mds-wsection__summary{margin:1px 0 0;color:var(--text-muted,#59636F);font-size:12px;line-height:1.35;}
.mds-wsection__jump{display:inline-flex;align-items:center;gap:6px;min-height:30px;
  border:1px solid var(--accent,#2F6F8F);border-radius:var(--radius-chip,2px);
  background:var(--blue-a10,rgba(47,111,143,.10));color:var(--accent,#2F6F8F);
  padding:4px 10px;font-family:var(--font-body);font-size:11px;font-weight:700;cursor:pointer;text-decoration:none;
  white-space:nowrap;}
.mds-wsection__jump:hover{background:var(--bg-active,#E6EEF5);}
.mds-wsection__body{display:grid;gap:12px;min-width:0;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "workspace-section");
  el.textContent = css;
  document.head.appendChild(el);
}

export function WorkspaceSection({ id, title, summary, jump, onJump, jumpHref, children, className = "" }) {
  inject();
  return (
    <section id={id} className={`mds-wsection${className ? " " + className : ""}`}>
      <div className="mds-wsection__head">
        <div className="mds-wsection__heading">
          {title && <h3 className="mds-wsection__title">{title}</h3>}
          {summary && <p className="mds-wsection__summary">{summary}</p>}
        </div>
        {jump && (
          jumpHref
            ? <a className="mds-wsection__jump" href={jumpHref}>{jump}</a>
            : <button type="button" className="mds-wsection__jump" onClick={onJump}>{jump}</button>
        )}
      </div>
      <div className="mds-wsection__body">{children}</div>
    </section>
  );
}
