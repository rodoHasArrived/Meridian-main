// Meridian status bar — near-black footer mirroring the WPF StatusBar palette.
// A row of mono fields: connection, sync state, record counts, latency. Always-on telemetry.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.ws-statusbar{display:flex;align-items:center;flex-wrap:wrap;gap:6px 0;min-height:28px;min-width:0;max-width:100%;box-sizing:border-box;padding:4px 12px;
  background:var(--statusbar-bg,#29323A);border-top:1px solid var(--statusbar-border,#3A444D);
  font-family:var(--font-data);font-size:11px;color:var(--statusbar-text,#C9CFD4);
  font-variant-numeric:tabular-nums;}
.ws-statusbar__item{display:inline-flex;align-items:center;gap:6px;padding:0 12px;min-width:0;max-width:100%;box-sizing:border-box;
  border-right:1px solid var(--statusbar-border,#3A444D);min-height:16px;}
.ws-statusbar__item>span:last-child{min-width:0;overflow-wrap:anywhere;}
.ws-statusbar__item:first-child{padding-left:0;}
.ws-statusbar__item--push{margin-left:auto;border-right:none;border-left:1px solid var(--statusbar-border,#3A444D);}
.ws-statusbar__label{font-family:var(--font-body);font-variant:all-small-caps;
  letter-spacing:.03em;color:var(--topbar-text-faint,#8A929B);}
.ws-statusbar__dot{width:6px;height:6px;border-radius:50%;flex:0 0 auto;}
.ws-st--ok{background:var(--chrome-ok,#6FAE8B);} .ws-st--warn{background:var(--chrome-warn,#CDA84D);} .ws-st--err{background:var(--chrome-err,#D98B76);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "statusbar");
  el.textContent = css;
  document.head.appendChild(el);
}

export function StatusBar({ items = [] }) {
  inject();
  // items: [{ label?, value, status?: "ok"|"warn"|"err", push?: boolean }]
  return (
    <footer className="ws-statusbar">
      {items.map((it, i) => (
        <span key={i} className={`ws-statusbar__item${it.push ? " ws-statusbar__item--push" : ""}`}>
          {it.status && <span className={`ws-statusbar__dot ws-st--${it.status}`} />}
          {it.label && <span className="ws-statusbar__label">{it.label}</span>}
          <span>{it.value}</span>
        </span>
      ))}
    </footer>
  );
}
