// Meridian SplitPane — two-panel resizable layout for workstation rails. A 1px load-bearing
// divider with a 7px grab zone; keyboard-resizable (role="separator", arrows, Home/End);
// optional localStorage persistence; double-click resets — or collapses, when `collapsible`.
// Sizes accept px numbers or "%" strings and are re-clamped when the container resizes.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-split{display:flex;width:100%;height:100%;min-height:0;min-width:0;overflow:hidden;}
.mds-split--v{flex-direction:column;}
.mds-split__pane{min-width:0;min-height:0;overflow:auto;}
.mds-split__pane--collapsed{overflow:hidden;}
.mds-split__div{flex:none;position:relative;background:var(--border,#D2CDC3);z-index:1;touch-action:none;}
.mds-split--h > .mds-split__div{width:1px;cursor:col-resize;}
.mds-split--v > .mds-split__div{height:1px;cursor:row-resize;}
.mds-split__div--locked{cursor:default;}
.mds-split__div::before{content:"";position:absolute;}
.mds-split--h > .mds-split__div::before{top:0;bottom:0;left:-3px;right:-3px;}
.mds-split--v > .mds-split__div::before{left:0;right:0;top:-3px;bottom:-3px;}
.mds-split__div:hover,.mds-split__div--drag{background:var(--border-hover,#BEB8AD);}
.mds-split__div--locked:hover{background:var(--border,#D2CDC3);}
.mds-split__div:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:1px;}
/* Dragging: kill text selection and hold the resize cursor across the whole window, so the
   pointer crossing a pane does not flicker the cursor or start selecting copy. */
body.mds-split-dragging{user-select:none;-webkit-user-select:none;}
body.mds-split-dragging--h,body.mds-split-dragging--h *{cursor:col-resize !important;} /* important: the pointer is captured by <body>, and any descendant's own cursor is the more specific rule */
body.mds-split-dragging--v,body.mds-split-dragging--v *{cursor:row-resize !important;} /* important: same, vertical drag */
@media print{.mds-split{display:block;height:auto;overflow:visible;}
  .mds-split__pane{height:auto !important;width:auto !important;overflow:visible;} /* important: unrolls the inline height/width the drag wrote onto the element */
  .mds-split__div{display:none;}}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "splitpane");
  el.textContent = css;
  document.head.appendChild(el);
}

// Accepts 280 or "30%" — percentages resolve against the container's current extent.
function resolve(v, total) {
  if (typeof v === "string" && v.trim().endsWith("%")) return (parseFloat(v) / 100) * (total || 0);
  return parseFloat(v);
}

export function SplitPane({
  direction = "horizontal",
  children,
  initial = 280,
  min = 160,
  max = 560,
  minSecondary = 0,
  primary = "start",
  collapsible = false,
  collapsed: collapsedProp,
  onCollapsedChange,
  disabled = false,
  onResize,
  onResizeEnd,
  persistKey = null,
  dividerLabel = "Resize panel",
  style = {},
  className = "",
  ...rest
}) {
  inject();
  const horiz = direction === "horizontal";
  const rootRef = React.useRef(null);
  const [total, setTotal] = React.useState(0);

  // Clamp against min/max AND against what the other pane needs — without minSecondary a large
  // max could crush the fluid pane to nothing.
  const clamp = React.useCallback((v) => {
    const lo = resolve(min, total);
    let hi = resolve(max, total);
    if (total > 0) hi = Math.min(hi, total - resolve(minSecondary, total) - 1);
    return Math.min(Math.max(v, lo), Math.max(lo, hi));
  }, [min, max, minSecondary, total]);

  const [size, setSize] = React.useState(() => {
    if (persistKey && typeof localStorage !== "undefined") {
      const v = parseFloat(localStorage.getItem("mds-split:" + persistKey));
      if (Number.isFinite(v)) return v;
    }
    return resolve(initial, 0) || 280;
  });
  const [drag, setDrag] = React.useState(false);
  const [selfCollapsed, setSelfCollapsed] = React.useState(false);
  const collapsed = collapsedProp != null ? collapsedProp : selfCollapsed;

  const persist = (v) => {
    if (persistKey && typeof localStorage !== "undefined") localStorage.setItem("mds-split:" + persistKey, String(v));
  };

  // Track the container so percentage bounds and re-clamping work on window/layout resize.
  React.useEffect(() => {
    const el = rootRef.current;
    if (!el || typeof ResizeObserver === "undefined") return;
    const ro = new ResizeObserver(([e]) => {
      const r = e.contentRect;
      setTotal(horiz ? r.width : r.height);
    });
    ro.observe(el);
    return () => ro.disconnect();
  }, [horiz]);

  React.useEffect(() => {
    if (!total) return;
    setSize((s) => { const c = clamp(s); return c === s ? s : c; });
  }, [total, clamp]);

  const setCollapsed = (next) => {
    if (collapsedProp == null) setSelfCollapsed(next);
    onCollapsedChange?.(next);
  };

  const onPointerDown = (e) => {
    if (disabled || e.button !== 0) return;
    e.preventDefault();
    const node = e.currentTarget;
    node.setPointerCapture?.(e.pointerId);
    setDrag(true);
    if (collapsed) setCollapsed(false);
    document.body.classList.add("mds-split-dragging", `mds-split-dragging--${horiz ? "h" : "v"}`);
    let last = size;
    const move = (ev) => {
      // Re-read the rect each move: a pane's own content can reflow mid-drag.
      const rect = rootRef.current.getBoundingClientRect();
      const pos = horiz ? ev.clientX - rect.left : ev.clientY - rect.top;
      const extent = horiz ? rect.width : rect.height;
      last = clamp(primary === "start" ? pos : extent - pos);
      setSize(last);
      onResize?.(last);
    };
    const up = () => {
      setDrag(false);
      document.body.classList.remove("mds-split-dragging", "mds-split-dragging--h", "mds-split-dragging--v");
      node.releasePointerCapture?.(e.pointerId);
      node.removeEventListener("pointermove", move);
      node.removeEventListener("pointerup", up);
      node.removeEventListener("pointercancel", up);
      persist(last); // write once at drag end, not on every pointermove
      onResizeEnd?.(last);
    };
    node.addEventListener("pointermove", move);
    node.addEventListener("pointerup", up);
    node.addEventListener("pointercancel", up);
  };

  const commit = (v) => {
    const c = clamp(v);
    setSize(c);
    persist(c);
    onResize?.(c);
    onResizeEnd?.(c);
  };

  const onKeyDown = (e) => {
    if (disabled) return;
    const step = e.shiftKey ? 40 : 8;
    const grow = horiz ? "ArrowRight" : "ArrowDown";
    const shrink = horiz ? "ArrowLeft" : "ArrowUp";
    const sign = primary === "start" ? 1 : -1;
    if (e.key === grow) { e.preventDefault(); if (collapsed) setCollapsed(false); else commit(size + step * sign); }
    else if (e.key === shrink) { e.preventDefault(); if (!collapsed) commit(size - step * sign); }
    else if (e.key === "Home") { e.preventDefault(); commit(resolve(min, total)); }
    else if (e.key === "End") { e.preventDefault(); commit(resolve(max, total)); }
    else if (e.key === "Enter" && collapsible) { e.preventDefault(); setCollapsed(!collapsed); }
  };

  const onDoubleClick = () => {
    if (disabled) return;
    if (collapsible) setCollapsed(!collapsed);
    else commit(resolve(initial, total));
  };

  const kids = React.Children.toArray(children);
  const shown = collapsed ? 0 : clamp(size);
  const fixed = { flex: "none", [horiz ? "width" : "height"]: shown };
  const fluid = { flex: 1 };
  const paneCls = (isFixed) => `mds-split__pane${isFixed && collapsed ? " mds-split__pane--collapsed" : ""}`;
  const startFixed = primary === "start";

  return (
    <div
      ref={rootRef}
      className={`mds-split mds-split--${horiz ? "h" : "v"}${className ? " " + className : ""}`}
      style={style}
      {...rest}
    >
      <div className={paneCls(startFixed)} style={startFixed ? fixed : fluid} aria-hidden={startFixed && collapsed ? true : undefined}>{kids[0]}</div>
      <div
        className={`mds-split__div${drag ? " mds-split__div--drag" : ""}${disabled ? " mds-split__div--locked" : ""}`}
        role="separator"
        aria-orientation={horiz ? "vertical" : "horizontal"}
        aria-valuenow={Math.round(shown)}
        aria-valuemin={Math.round(resolve(min, total)) || 0}
        aria-valuemax={Math.round(resolve(max, total)) || 0}
        aria-valuetext={collapsed ? "Collapsed" : `${Math.round(shown)} pixels`}
        aria-label={dividerLabel}
        aria-disabled={disabled || undefined}
        tabIndex={disabled ? -1 : 0}
        onPointerDown={onPointerDown}
        onKeyDown={onKeyDown}
        onDoubleClick={onDoubleClick}
      />
      <div className={paneCls(!startFixed)} style={startFixed ? fluid : fixed} aria-hidden={!startFixed && collapsed ? true : undefined}>{kids[1]}</div>
    </div>
  );
}

SplitPane.displayName = "SplitPane";
