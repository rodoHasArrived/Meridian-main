import { type CSSProperties, type ReactNode, useEffect, useLayoutEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

type PopoverPosition = "top" | "bottom" | "left" | "right";

export interface PopoverProps {
  open: boolean;
  onClose?: () => void;
  anchorEl?: HTMLElement | null;
  position?: PopoverPosition;
  offset?: number;
  closeOnEsc?: boolean;
  closeOnBackdrop?: boolean;
  children?: ReactNode;
  className?: string;
}

/**
 * Lightweight floating panel anchored to a control — no backdrop weight, positioned to the
 * anchor. Concrete: flat panel with a hairline border; the detached overlay carries the only
 * shadow (`0 2px 6px rgba(0,0,0,.18)`).
 */
export function Popover({
  open,
  onClose,
  anchorEl,
  position = "bottom",
  offset = 8,
  closeOnEsc = true,
  closeOnBackdrop = true,
  children,
  className
}: PopoverProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const [style, setStyle] = useState<CSSProperties>({});

  useLayoutEffect(() => {
    if (!open || !anchorEl || !panelRef.current || typeof window === "undefined") {
      return undefined;
    }

    const update = () => {
      const anchor = anchorEl.getBoundingClientRect();
      const panel = panelRef.current!.getBoundingClientRect();
      let top = 0;
      let left = 0;

      switch (position) {
        case "top":
          top = anchor.top - panel.height - offset;
          left = anchor.left + anchor.width / 2 - panel.width / 2;
          break;
        case "bottom":
          top = anchor.bottom + offset;
          left = anchor.left + anchor.width / 2 - panel.width / 2;
          break;
        case "left":
          top = anchor.top + anchor.height / 2 - panel.height / 2;
          left = anchor.left - panel.width - offset;
          break;
        case "right":
          top = anchor.top + anchor.height / 2 - panel.height / 2;
          left = anchor.right + offset;
          break;
        default:
          break;
      }

      const margin = 8;
      left = Math.max(margin, Math.min(left, window.innerWidth - panel.width - margin));
      top = Math.max(margin, Math.min(top, window.innerHeight - panel.height - margin));
      setStyle({ top: `${top}px`, left: `${left}px` });
    };

    update();
    window.addEventListener("scroll", update, true);
    window.addEventListener("resize", update);
    return () => {
      window.removeEventListener("scroll", update, true);
      window.removeEventListener("resize", update);
    };
  }, [open, anchorEl, position, offset]);

  useEffect(() => {
    if (!open || typeof document === "undefined") {
      return undefined;
    }
    const handleKey = (event: KeyboardEvent) => {
      if (closeOnEsc && event.key === "Escape") {
        onClose?.();
      }
    };
    document.addEventListener("keydown", handleKey);
    return () => document.removeEventListener("keydown", handleKey);
  }, [open, closeOnEsc, onClose]);

  if (!open) {
    return null;
  }

  return (
    <>
      {closeOnBackdrop ? <div className="fixed inset-0 z-[1000]" aria-hidden="true" onMouseDown={onClose} /> : null}
      <div
        ref={panelRef}
        role="dialog"
        style={style}
        className={cn(
          "fixed z-[1001] max-w-[280px] rounded-[2px] border border-border bg-popover p-3 text-sm text-popover-foreground shadow-[0_2px_6px_rgba(0,0,0,0.18)]",
          className
        )}
      >
        {children}
      </div>
    </>
  );
}
