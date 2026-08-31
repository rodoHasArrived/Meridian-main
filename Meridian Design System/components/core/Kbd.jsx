// Meridian Kbd — keyboard shortcut hint. Operator workstations are keyboard-driven;
// this renders crisp key caps and combos (⌘K, Ctrl+Enter). Functional, no transitions.
import React from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-kbd{display:inline-flex;align-items:center;gap:3px;font-family:var(--font-body);vertical-align:middle;}
.mds-kbd__key{display:inline-flex;align-items:center;justify-content:center;
  min-width:18px;height:18px;padding:0 5px;
  font-family:var(--font-data,monospace);font-size:11px;font-weight:600;line-height:1;
  color:var(--text-secondary,#4D5967);background:var(--bg-medium,#F5F7FA);
  border:1px solid var(--border,#D7DCE2);border-bottom-width:2px;
  border-radius:var(--radius-chip,3px);white-space:nowrap;}
.mds-kbd__plus{font-size:10px;color:var(--text-muted,#59636F);font-weight:500;padding:0 1px;}
.mds-kbd--solid .mds-kbd__key{color:var(--text-primary,#22272E);background:var(--bg-light,#FAFBFC);}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "kbd");
  el.textContent = css;
  document.head.appendChild(el);
}

// Split a combo string like "Ctrl+Enter" or "⌘ K" into displayable keys.
function parseKeys(keys, children) {
  if (Array.isArray(keys)) return keys;
  if (typeof keys === "string") return keys.split(/\s*\+\s*|\s+/).filter(Boolean);
  if (typeof children === "string") return children.split(/\s*\+\s*|\s+/).filter(Boolean);
  return null;
}

export function Kbd({ keys, variant = "default", className = "", children, ...rest }) {
  inject();
  const parts = parseKeys(keys, children);
  return (
    <span className={`mds-kbd mds-kbd--${variant}${className ? " " + className : ""}`} {...rest}>
      {parts
        ? parts.map((k, i) => (
            <React.Fragment key={i}>
              {i > 0 && <span className="mds-kbd__plus" aria-hidden="true">+</span>}
              <kbd className="mds-kbd__key">{k}</kbd>
            </React.Fragment>
          ))
        : <kbd className="mds-kbd__key">{children}</kbd>}
    </span>
  );
}
