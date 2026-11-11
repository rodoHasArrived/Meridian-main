// Meridian Tooltip — hover/focus overlay. Renders above/below/left/right of the trigger.
// Wired for AT: role="tooltip" + aria-describedby; Escape dismisses; wraps long content.
// `focusable` adds a tab stop when the child isn't natively focusable (plain text/icon).
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-tip-host{position:relative;display:inline-flex;align-items:center;}
.mds-tip-host:focus-visible{outline:var(--focus-ring,2px solid #2D6FB3);outline-offset:var(--focus-ring-offset,1px);}
.mds-tip-box{position:absolute;z-index:var(--z-tooltip,500);pointer-events:none;
  background:var(--text-primary,#20252B);color:var(--bg-light,#F8F7F3);
  padding:8px 12px;font-family:var(--font-body);font-size:11px;line-height:1.4;
  width:max-content;max-width:280px;border:1px solid var(--border-strong,#AAA49A);box-shadow:var(--shadow-menu,0 4px 12px rgba(17,23,29,.10));}
.mds-tip-box--above{bottom:calc(100% + 6px);left:50%;transform:translateX(-50%);}
.mds-tip-box--below{top:calc(100% + 6px);left:50%;transform:translateX(-50%);}
.mds-tip-box--left{right:calc(100% + 6px);top:50%;transform:translateY(-50%);}
.mds-tip-box--right{left:calc(100% + 6px);top:50%;transform:translateY(-50%);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds","tooltip");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Tooltip({ content, children, placement = "above", delay = 300, focusable = false }) {
  inject();
  const uid = React.useId();
  const [visible, setVisible] = React.useState(false);
  const timer = React.useRef(null);

  const show = () => { clearTimeout(timer.current); timer.current = setTimeout(() => setVisible(true), delay); };
  const hide = () => { clearTimeout(timer.current); setVisible(false); };
  React.useEffect(() => () => clearTimeout(timer.current), []);
  const onKeyDown = (e) => { if (e.key === "Escape" && visible) { e.stopPropagation(); hide(); } };

  return (
    <span className="mds-tip-host"
      tabIndex={focusable ? 0 : undefined}
      aria-describedby={visible ? uid : undefined}
      onMouseEnter={show} onMouseLeave={hide}
      onFocus={show} onBlur={hide} onKeyDown={onKeyDown}
    >
      {children}
      {visible && content && (
        <span className={`mds-tip-box mds-tip-box--${placement}`} role="tooltip" id={uid}>{content}</span>
      )}
    </span>
  );
}
