// Meridian Popover — lightweight floating panel anchored to an element.
// For tooltips, dropdowns, and contextual menus without the full weight of ContextMenu.
import React from "react";
const { useEffect, useLayoutEffect, useRef, useState } = React;

let injected = false;
function inject() {
  if (injected || typeof document === "undefined") return;
  injected = true;
  const css = `
.mds-popover-root { position: fixed; z-index: 1001; pointer-events: none; }
.mds-popover-wrap {
  background: var(--bg-light, #FAFBFC); border: 1px solid var(--border, #D7DCE2);
  border-radius: var(--radius-card, 2px); padding: var(--space-md, 12px);
  box-shadow: var(--shadow-menu); pointer-events: auto;
  font-family: var(--font-body); font-size: var(--type-body, 13px);
  color: var(--text-primary, #22272E); max-width: 280px;
  animation: mds-popover-fade-in var(--motion-base, 150ms) var(--ease-standard, ease-out);
}
@keyframes mds-popover-fade-in {
  from { opacity: 0; transform: scale(0.95); }
  to { opacity: 1; transform: scale(1); }
}
.mds-popover-overlay { position: fixed; inset: 0; z-index: 1000; }
`;
  const el = document.createElement("style");
  el.setAttribute("data-mds", "popover");
  el.textContent = css;
  document.head.appendChild(el);
}

export function Popover({
  open = false,
  onClose = () => {},
  anchorEl,
  position = "bottom",
  offset = 8,
  children,
  closeOnBackdrop = true,
  closeOnEsc = true,
}) {
  inject();
  const rootRef = useRef(null);
  const [style, setStyle] = useState({});

  // useLayoutEffect: measure + position synchronously before paint, so the popover
  // never flashes at (0,0) on open before the position effect fires.
  useLayoutEffect(() => {
    if (!open || !anchorEl || !rootRef.current) return;

    const updatePosition = () => {
      const anchorRect = anchorEl.getBoundingClientRect();
      const popoverRect = rootRef.current.getBoundingClientRect();
      let top = 0,
        left = 0;

      switch (position) {
        case "top":
          top = anchorRect.top - popoverRect.height - offset;
          left = anchorRect.left + anchorRect.width / 2 - popoverRect.width / 2;
          break;
        case "bottom":
          top = anchorRect.bottom + offset;
          left = anchorRect.left + anchorRect.width / 2 - popoverRect.width / 2;
          break;
        case "left":
          top = anchorRect.top + anchorRect.height / 2 - popoverRect.height / 2;
          left = anchorRect.left - popoverRect.width - offset;
          break;
        case "right":
          top = anchorRect.top + anchorRect.height / 2 - popoverRect.height / 2;
          left = anchorRect.right + offset;
          break;
        default:
          break;
      }

      // Clamp to viewport
      const margin = 8;
      if (left < margin) left = margin;
      if (left + popoverRect.width > window.innerWidth - margin) {
        left = window.innerWidth - popoverRect.width - margin;
      }
      if (top < margin) top = margin;
      if (top + popoverRect.height > window.innerHeight - margin) {
        top = window.innerHeight - popoverRect.height - margin;
      }

      setStyle({ top: `${top}px`, left: `${left}px` });
    };

    updatePosition();
    window.addEventListener("scroll", updatePosition, true);
    window.addEventListener("resize", updatePosition);
    return () => {
      window.removeEventListener("scroll", updatePosition, true);
      window.removeEventListener("resize", updatePosition);
    };
  }, [open, anchorEl, position, offset]);

  useEffect(() => {
    if (!open) return;
    const handleEsc = (e) => {
      if (closeOnEsc && e.key === "Escape") {
        onClose();
      }
    };
    const handleClick = (e) => {
      if (closeOnBackdrop && !rootRef.current?.contains(e.target)) {
        onClose();
      }
    };
    document.addEventListener("keydown", handleEsc);
    document.addEventListener("click", handleClick);
    return () => {
      document.removeEventListener("keydown", handleEsc);
      document.removeEventListener("click", handleClick);
    };
  }, [open, closeOnBackdrop, closeOnEsc, onClose]);

  if (!open) return null;

  return (
    <>
      <div className="mds-popover-overlay" onClick={closeOnBackdrop ? onClose : undefined} />
      <div ref={rootRef} className="mds-popover-root" style={style}>
        <div className="mds-popover-wrap">{children}</div>
      </div>
    </>
  );
}
