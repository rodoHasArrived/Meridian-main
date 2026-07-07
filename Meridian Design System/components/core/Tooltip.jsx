// Meridian Tooltip — hover/focus overlay. Renders above or below the trigger.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-tip-host{position:relative;display:inline-flex;align-items:center;}
.mds-tip-box{position:absolute;z-index:300;pointer-events:none;
  background:var(--text-primary,#22272E);color:var(--bg-light,#FAFBFC);
  padding:8px 12px;font-family:var(--font-body);font-size:11px;line-height:1.4;
  white-space:nowrap;border:1px solid var(--border-strong,#AAB4BF);box-shadow:var(--shadow-menu,0 4px 12px rgba(0,0,0,.10));}
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

export function Tooltip({ content, children, placement = "above", delay = 300 }) {
  inject();
  const [visible, setVisible] = React.useState(false);
  const timer = React.useRef(null);

  const show = () => { timer.current = setTimeout(() => setVisible(true), 200); };
  const hide = () => { clearTimeout(timer.current); setVisible(false); };

  return (
    <span className="mds-tip-host"
      onMouseEnter={show} onMouseLeave={hide}
      onFocus={show} onBlur={hide}
    >
      {children}
      {visible && content && (
        <span className={`mds-tip-box mds-tip-box--${placement}`}>{content}</span>
      )}
    </span>
  );
}
