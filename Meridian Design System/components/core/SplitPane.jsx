// Meridian SplitPane — two-panel resizable layout for workstation rails. A 1px load-bearing
// divider with a 7px grab zone; keyboard-resizable (role="separator", arrows, Home/End);
// optional localStorage persistence; double-click resets to the initial size.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-split{display:flex;width:100%;height:100%;min-height:0;min-width:0;overflow:hidden;}
.mds-split--v{flex-direction:column;}
.mds-split__pane{min-width:0;min-height:0;overflow:auto;}
.mds-split__div{flex:none;position:relative;background:var(--border,#CBD3DC);z-index:1;touch-action:none;}
.mds-split--h > .mds-split__div{width:1px;cursor:col-resize;}
.mds-split--v > .mds-split__div{height:1px;cursor:row-resize;}
.mds-split__div::before{content:"";position:absolute;}
.mds-split--h > .mds-split__div::before{top:0;bottom:0;left:-3px;right:-3px;}
.mds-split--v > .mds-split__div::before{left:0;right:0;top:-3px;bottom:-3px;}
.mds-split__div:hover,.mds-split__div--drag{background:var(--border-hover,#ADB8C4);}
.mds-split__div:focus-visible{outline:var(--focus-ring,2px solid #2F6F8F);outline-offset:1px;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "splitpane");
  el.textContent = css;
  document.head.appendChild(el);
}

export function SplitPane({
  direction = "horizontal",
  children,
  initial = 280,
  min = 160,
  max = 560,
  primary = "start",
  persistKey = null,
  style = {},
}) {
  inject();
  const horiz = direction === "horizontal";
  const rootRef = React.useRef(null);
  const clamp = (v) => Math.min(max, Math.max(min, v));
  const [size, setSize] = React.useState(() => {
    if (persistKey && typeof localStorage !== "undefined") {
      const v = parseFloat(localStorage.getItem("mds-split:" + persistKey));
      if (Number.isFinite(v)) return clamp(v);
    }
    return initial;
  });
  const [drag, setDrag] = React.useState(false);
  const commit = (v) => {
    const c = clamp(v);
    setSize(c);
    if (persistKey && typeof localStorage !== "undefined") {
      localStorage.setItem("mds-split:" + persistKey, String(c));
    }
  };
  const onPointerDown = (e) => {
    e.preventDefault();
    setDrag(true);
    const rect = rootRef.current.getBoundingClientRect();
    const move = (ev) => {
      const pos = horiz ? ev.clientX - rect.left : ev.clientY - rect.top;
      const total = horiz ? rect.width : rect.height;
      commit(primary === "start" ? pos : total - pos);
    };
    const up = () => {
      setDrag(false);
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", up);
    };
    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", up);
  };
  const onKeyDown = (e) => {
    const step = e.shiftKey ? 40 : 8;
    const grow = horiz ? "ArrowRight" : "ArrowDown";
    const shrink = horiz ? "ArrowLeft" : "ArrowUp";
    const sign = primary === "start" ? 1 : -1;
    if (e.key === grow) { e.preventDefault(); commit(size + step * sign); }
    else if (e.key === shrink) { e.preventDefault(); commit(size - step * sign); }
    else if (e.key === "Home") { e.preventDefault(); commit(min); }
    else if (e.key === "End") { e.preventDefault(); commit(max); }
  };
  const kids = React.Children.toArray(children);
  const fixed = { flex: "none", [horiz ? "width" : "height"]: size };
  const fluid = { flex: 1 };
  return (
    <div ref={rootRef} className={`mds-split mds-split--${horiz ? "h" : "v"}`} style={style}>
      <div className="mds-split__pane" style={primary === "start" ? fixed : fluid}>{kids[0]}</div>
      <div
        className={`mds-split__div${drag ? " mds-split__div--drag" : ""}`}
        role="separator"
        aria-orientation={horiz ? "vertical" : "horizontal"}
        aria-valuenow={Math.round(size)}
        aria-valuemin={min}
        aria-valuemax={max}
        aria-label="Resize panel"
        tabIndex={0}
        onPointerDown={onPointerDown}
        onKeyDown={onKeyDown}
        onDoubleClick={() => commit(initial)}
      />
      <div className="mds-split__pane" style={primary === "start" ? fluid : fixed}>{kids[1]}</div>
    </div>
  );
}
