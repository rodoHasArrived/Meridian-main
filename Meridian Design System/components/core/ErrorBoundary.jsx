// Meridian ErrorBoundary — panel-level crash isolation. One failed chart or table renders a
// quiet Meridian-styled fault panel instead of blanking the whole workstation. Voice rule
// applies: name the surface, show the evidence (error message, mono), offer retry.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-errbound{border:1px solid var(--red,#BA3F55);background:var(--red-a10,rgba(186,63,85,.10));
  padding:16px;display:flex;flex-direction:column;gap:8px;font-family:var(--font-body);box-sizing:border-box;}
.mds-errbound__title{font-size:12px;font-weight:600;color:var(--red-dim,#8C2F40);
  font-variant:all-small-caps;letter-spacing:.03em;}
.mds-errbound__msg{font-family:var(--font-data);font-size:11px;color:var(--text-secondary,#4D5967);
  white-space:pre-wrap;word-break:break-word;max-height:72px;overflow:hidden;}
.mds-errbound__btn{align-self:flex-start;appearance:none;cursor:pointer;padding:5px 12px;
  border:1px solid var(--red,#BA3F55);background:var(--bg-light,#fff);color:var(--red-dim,#8C2F40);
  font-family:var(--font-body);font-size:12px;font-weight:600;}
.mds-errbound__btn:hover{background:var(--red-a10,rgba(186,63,85,.10));}
.mds-errbound__btn:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:1px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "errorboundary");
  el.textContent = css;
  document.head.appendChild(el);
}

export class ErrorBoundary extends React.Component {
  constructor(props) {
    super(props);
    this.state = { error: null };
  }
  static getDerivedStateFromError(error) {
    return { error };
  }
  componentDidCatch(error, info) {
    if (this.props.onError) this.props.onError(error, info);
  }
  render() {
    inject();
    if (!this.state.error) return this.props.children;
    const { label = "This panel", fallback } = this.props;
    if (fallback) return fallback;
    return (
      <div className="mds-errbound" role="alert">
        <span className="mds-errbound__title">{label} failed to render</span>
        <span className="mds-errbound__msg">{String(this.state.error && this.state.error.message || this.state.error)}</span>
        <button type="button" className="mds-errbound__btn" onClick={() => this.setState({ error: null })}>
          Try again
        </button>
      </div>
    );
  }
}
