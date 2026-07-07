// Meridian ContextMenu — right-click menu for table rows and UI elements.
// Supports icons, dividers, disabled items, and danger (red) styling.
// Positioning: smart placement relative to the trigger point (no overflow off-screen).

import React from "react";
const { useState, useRef, useEffect } = React;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.cm-menu{position:fixed;z-index:9999;background:var(--bg-light,#fff);
  border:1px solid var(--border,#D7DCE2);
  min-width:180px;padding:4px 0;font-family:var(--font-body);
  font-size:13px;overflow:hidden;}
.cm-item{display:flex;align-items:center;gap:8px;padding:8px 12px;
  cursor:pointer;color:var(--text-primary,#22272E);white-space:nowrap;
  border:none;background:transparent;width:100%;text-align:left;}
.cm-item:hover{background:var(--bg-hover,#F1F4F7);}
.cm-item:active{background:var(--bg-active,#E6EEF5);}
.cm-item:focus-visible{outline:var(--focus-ring);outline-offset:-2px;background:var(--bg-hover,#F1F4F7);}
.cm-item--disabled{cursor:not-allowed;background:transparent;color:var(--text-disabled,#889099);}
.cm-item--disabled:hover{background:transparent;}
.cm-item--danger{color:var(--red,#BA3F55);}
.cm-item--danger:hover{background:var(--red-a10,rgba(186,63,85,0.10));}
.cm-icon{flex:0 0 auto;font-size:14px;color:var(--text-muted,#59636F);}
.cm-label{flex:1;overflow:hidden;text-overflow:ellipsis;}
.cm-divider{height:1px;background:var(--border,#D7DCE2);margin:4px 0;}
.cm-overlay{position:fixed;top:0;left:0;right:0;bottom:0;z-index:9998;cursor:default;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "context-menu");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ContextMenu({ items = [], x = 0, y = 0, onClose = () => {} }) {
  inject();
  const menuRef = useRef(null);
  const itemRefs = useRef([]);
  const [pos, setPos] = useState({ x, y });

  // Re-measure and clamp within the viewport whenever the anchor point changes.
  useEffect(() => {
    const el = menuRef.current;
    if (!el) return;
    const rect = el.getBoundingClientRect();
    let nx = x, ny = y;
    if (nx + rect.width > window.innerWidth - 8) nx = Math.max(8, window.innerWidth - rect.width - 8);
    if (ny + rect.height > window.innerHeight - 8) ny = Math.max(8, window.innerHeight - rect.height - 8);
    setPos({ x: nx, y: ny });
  }, [x, y]);

  // Focus the first enabled item when the menu opens (ARIA menu pattern).
  useEffect(() => {
    const first = itemRefs.current.find((b) => b && !b.disabled);
    first && first.focus();
  }, []);

  const enabledIndices = () =>
    itemRefs.current.map((b, i) => (b && !b.disabled ? i : -1)).filter((i) => i >= 0);

  const moveFocus = (dir) => {
    const en = enabledIndices();
    if (!en.length) return;
    const cur = itemRefs.current.findIndex((b) => b === document.activeElement);
    const curPos = en.indexOf(cur);
    let next;
    if (dir === "first") next = en[0];
    else if (dir === "last") next = en[en.length - 1];
    else if (curPos < 0) next = en[0];
    else next = en[(curPos + (dir === "down" ? 1 : en.length - 1)) % en.length];
    itemRefs.current[next] && itemRefs.current[next].focus();
  };

  const onKeyDown = (e) => {
    switch (e.key) {
      case "Escape": e.preventDefault(); onClose(); break;
      case "ArrowDown": e.preventDefault(); moveFocus("down"); break;
      case "ArrowUp": e.preventDefault(); moveFocus("up"); break;
      case "Home": e.preventDefault(); moveFocus("first"); break;
      case "End": e.preventDefault(); moveFocus("last"); break;
      case "Tab": e.preventDefault(); moveFocus(e.shiftKey ? "up" : "down"); break;
      default: break;
    }
  };

  const handleItemClick = (item) => {
    if (item.disabled) return;
    item.onClick?.();
    onClose();
  };

  return (
    <>
      <div className="cm-overlay" onClick={onClose} />
      <div
        className="cm-menu"
        ref={menuRef}
        role="menu"
        aria-orientation="vertical"
        style={{ left: pos.x, top: pos.y }}
        onKeyDown={onKeyDown}
      >
        {items.map((item, idx) => {
          if (item.type === "divider") {
            return <div key={idx} className="cm-divider" role="separator" />;
          }

          return (
            <button
              key={idx}
              ref={(el) => { itemRefs.current[idx] = el; }}
              role="menuitem"
              tabIndex={-1}
              className={`cm-item ${item.disabled ? "cm-item--disabled" : ""} ${
                item.dangerous ? "cm-item--danger" : ""
              }`.trim()}
              onClick={() => handleItemClick(item)}
              disabled={item.disabled}
            >
              {item.icon && <span className="cm-icon" aria-hidden="true">{item.icon}</span>}
              <span className="cm-label">{item.label}</span>
              {item.submenuIcon && <span aria-hidden="true" style={{ fontSize: "10px", opacity: 0.5 }}>›</span>}
            </button>
          );
        })}
      </div>
    </>
  );
}

/**
 * useContextMenu — hook to attach context menu to an element.
 * Returns [onContextMenu, isOpen, closeMenu, position] tuple.
 * Usage:
 *   const [onContextMenu, isOpen, closeMenu, position] = useContextMenu();
 *   <div onContextMenu={onContextMenu}>Right-click me</div>
 *   {isOpen && position && <ContextMenu items={[...]} x={position.x} y={position.y} onClose={closeMenu} />}
 */
export function useContextMenu() {
  const [position, setPosition] = useState(null);

  const handleContextMenu = (e) => {
    e.preventDefault();
    setPosition({ x: e.clientX, y: e.clientY });
  };

  const closeMenu = () => setPosition(null);
  const isOpen = position !== null;

  return [handleContextMenu, isOpen, closeMenu, position];
}
