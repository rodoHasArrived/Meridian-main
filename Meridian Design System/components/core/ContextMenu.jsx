// Meridian ContextMenu — right-click menu for table rows and UI elements.
// Supports icons, dividers, disabled items, and danger (red) styling.
// Positioning: smart placement relative to the trigger point (no overflow off-screen).

import React, { useState, useRef, useEffect } from "react";

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.cm-menu{position:fixed;z-index:9999;background:var(--bg-light,#fff);
  border:1px solid var(--border,#D7DCE2);border-radius:4px;
  box-shadow:0 2px 8px rgba(0,0,0,0.12),0 1px 3px rgba(0,0,0,0.08);
  min-width:180px;padding:4px 0;font-family:var(--font-body);
  font-size:13px;overflow:hidden;}
.cm-item{display:flex;align-items:center;gap:8px;padding:8px 12px;
  cursor:pointer;color:var(--text-primary,#22272E);
  transition:background-color 100ms ease;white-space:nowrap;
  border:none;background:transparent;width:100%;text-align:left;}
.cm-item:hover{background:var(--bg-hover,#F1F4F7);}
.cm-item:active{background:var(--bg-active,#E6EEF5);}
.cm-item--disabled{cursor:not-allowed;opacity:.5;}
.cm-item--disabled:hover{background:transparent;}
.cm-item--danger{color:var(--red,#BA3F55);}
.cm-item--danger:hover{background:var(--red-a10,rgba(186,63,85,0.10));}
.cm-icon{flex:0 0 auto;font-size:14px;opacity:.7;}
.cm-label{flex:1;overflow:hidden;text-overflow:ellipsis;}
.cm-divider{height:1px;background:var(--border,#D7DCE2);margin:4px 0;}
.cm-overlay{position:fixed;top:0;left:0;right:0;bottom:0;z-index:9998;cursor:default;}
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "context-menu");
  el.textContent = css;
  document.head.appendChild(el);
}

export function ContextMenu({ items = [], onClose = () => {} }) {
  const [position, setPosition] = useState(null);
  const menuRef = useRef(null);

  useEffect(() => {
    if (!position || !menuRef.current) return;

    const rect = menuRef.current.getBoundingClientRect();
    let { x, y } = position;

    // Adjust if menu would overflow right
    if (x + rect.width > window.innerWidth - 8) {
      x = window.innerWidth - rect.width - 8;
    }

    // Adjust if menu would overflow bottom
    if (y + rect.height > window.innerHeight - 8) {
      y = Math.max(8, window.innerHeight - rect.height - 8);
    }

    menuRef.current.style.left = `${x}px`;
    menuRef.current.style.top = `${y}px`;
  }, [position, menuRef]);

  const handleItemClick = (item) => {
    if (item.disabled) return;
    item.onClick?.();
    onClose();
  };

  if (!position) return null;

  return (
    <>
      <div className="cm-overlay" onClick={onClose} />
      <div className="cm-menu" ref={menuRef}>
        {items.map((item, idx) => {
          if (item.type === "divider") {
            return <div key={idx} className="cm-divider" />;
          }

          return (
            <button
              key={idx}
              className={`cm-item ${item.disabled ? "cm-item--disabled" : ""} ${
                item.dangerous ? "cm-item--danger" : ""
              }`.trim()}
              onClick={() => handleItemClick(item)}
              disabled={item.disabled}
            >
              {item.icon && <span className="cm-icon">{item.icon}</span>}
              <span className="cm-label">{item.label}</span>
              {item.submenuIcon && <span style={{ fontSize: "10px", opacity: 0.5 }}>›</span>}
            </button>
          );
        })}
      </div>
    </>
  );
}

/**
 * useContextMenu — hook to attach context menu to an element.
 * Returns (onContextMenu, isOpen, closeMenu) tuple.
 * Usage:
 *   const [onContextMenu, isOpen, closeMenu] = useContextMenu();
 *   <div onContextMenu={onContextMenu}>Right-click me</div>
 *   {isOpen && <ContextMenu items={[...]} onClose={closeMenu} />}
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
