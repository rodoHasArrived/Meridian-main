// Meridian workstation shell — slot-based composite chrome. One component owns the
// full-viewport frame (topbar / nav rail / scrolling main / optional right rail / status bar)
// so templates and consumers stop hand-rolling the same flex scaffolding. Every slot is
// optional and degrades gracefully: omit `nav` and the main area goes full-width; omit
// `statusBar` and the footer row disappears. Main content is isolated so a crashing panel
// cannot take the chrome down with it.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.ws-shell{display:flex;flex-direction:column;height:100%;min-height:0;background:var(--bg,#F2F0EB);font-family:var(--font-body);}
.ws-shell__row{display:flex;flex:1;min-height:0;}
.ws-shell__main{flex:1;min-width:0;overflow:auto;position:relative;}
.ws-shell__main--padded{padding:var(--space-lg,16px);box-sizing:border-box;}
.ws-shell__aside{flex-shrink:0;box-sizing:border-box;height:100%;overflow:auto;background:var(--card-surface,#F8F7F3);border-left:1px solid var(--border,#D2CDC3);}
.ws-shell__crash{margin:16px;padding:12px 14px;border:1px solid var(--red,#B54B42);border-radius:var(--radius-card,4px);background:var(--red-a10,rgba(166,67,63,.1));font-family:var(--font-data);font-size:12px;color:var(--red-dim,#8A3835);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "workstationshell");
  el.textContent = css;
  document.head.appendChild(el);
}

class ShellBoundary extends React.Component {
  constructor(props) { super(props); this.state = { error: null }; }
  static getDerivedStateFromError(error) { return { error }; }
  render() {
    if (this.state.error) {
      const msg = this.state.error && this.state.error.message ? String(this.state.error.message) : "render error";
      return <div className="ws-shell__crash" role="alert">Panel failed · {msg} · chrome unaffected</div>;
    }
    return this.props.children;
  }
}

export function WorkstationShell({
  topbar,           // node — usually <WorkstationTopbar/>
  nav,              // node — usually <NavRail/>
  aside,            // node — optional right rail (inspector, detail)
  asideWidth = 320, // px or CSS width for the right rail
  asideLabel = "Evidence", // accessible name for the right rail
  statusBar,        // node — usually <StatusBar/>
  padded = false,   // pad the main content area with --space-lg
  height = "100vh", // CSS height of the shell frame
  label,            // accessible name for the main workspace
  labelledBy,       // id of the heading naming the main workspace (wins over `label`)
  archetype,        // monitor | analyze | inspect | compare | reconcile | compose
  children,         // main workspace content
}) {
  inject();
  return (
    <div className="ws-shell" style={{ height }} data-workspace-archetype={archetype}>
      {topbar || null}
      <div className="ws-shell__row">
        {nav || null}
        <main
          className={`ws-shell__main${padded ? " ws-shell__main--padded" : ""}`}
          aria-labelledby={labelledBy}
          aria-label={labelledBy ? undefined : label}
        >
          <ShellBoundary>{children}</ShellBoundary>
        </main>
        {aside ? (
          <aside className="ws-shell__aside" style={{ width: asideWidth }} aria-label={asideLabel}>
            <ShellBoundary>{aside}</ShellBoundary>
          </aside>
        ) : null}
      </div>
      {statusBar || null}
    </div>
  );
}
