import { useCallback, useLayoutEffect, useRef, useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface ContextMenuItem {
  danger?: boolean;
  disabled?: boolean;
  icon?: ReactNode;
  id: string;
  label: ReactNode;
  onSelect?: () => void;
  type?: "item";
}

export interface ContextMenuDivider {
  id: string;
  type: "divider";
}

export type ContextMenuEntry = ContextMenuItem | ContextMenuDivider;

export interface ContextMenuPosition {
  x: number;
  y: number;
}

export interface ContextMenuProps {
  className?: string;
  items: ContextMenuEntry[];
  label?: string;
  onClose: () => void;
  open: boolean;
  position: ContextMenuPosition | null;
}

export function ContextMenu({ className, items, label = "Context menu", onClose, open, position }: ContextMenuProps) {
  const menuRef = useRef<HTMLDivElement>(null);

  useLayoutEffect(() => {
    if (!open || !position || !menuRef.current || typeof window === "undefined") {
      return;
    }

    const rect = menuRef.current.getBoundingClientRect();
    const left = Math.min(position.x, window.innerWidth - rect.width - 8);
    const top = Math.min(position.y, window.innerHeight - rect.height - 8);
    menuRef.current.style.left = `${Math.max(8, left)}px`;
    menuRef.current.style.top = `${Math.max(8, top)}px`;
  }, [open, position]);

  if (!open || !position) {
    return null;
  }

  return (
    <>
      <div className="fixed inset-0 z-50" aria-hidden="true" onMouseDown={onClose} />
      <div
        ref={menuRef}
        role="menu"
        aria-label={label}
        className={cn(
          "fixed z-[60] min-w-44 overflow-hidden rounded-[var(--radius-button,0.375rem)] border border-border bg-popover py-1 text-sm text-popover-foreground shadow-[var(--shadow-float)]",
          className
        )}
        style={{ left: position.x, top: position.y }}
      >
        {items.map((item) => item.type === "divider" ? (
          <div key={item.id} role="separator" className="my-1 h-px bg-border" />
        ) : (
          <button
            key={item.id}
            type="button"
            role="menuitem"
            disabled={item.disabled}
            className={cn(
              "flex w-full items-center gap-2 px-3 py-2 text-left transition-colors",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
              item.danger ? "text-danger hover:bg-danger/10" : "hover:bg-secondary/70",
              item.disabled && "cursor-not-allowed opacity-50"
            )}
            onClick={() => {
              if (item.disabled) {
                return;
              }

              item.onSelect?.();
              onClose();
            }}
          >
            {item.icon ? <span className="inline-flex h-4 w-4 items-center justify-center text-muted-foreground">{item.icon}</span> : null}
            <span className="min-w-0 flex-1 truncate">{item.label}</span>
          </button>
        ))}
      </div>
    </>
  );
}

export function useContextMenu() {
  const [position, setPosition] = useState<ContextMenuPosition | null>(null);

  const onContextMenu = useCallback((event: React.MouseEvent<HTMLElement>) => {
    event.preventDefault();
    setPosition({ x: event.clientX, y: event.clientY });
  }, []);

  const closeMenu = useCallback(() => setPosition(null), []);

  return {
    closeMenu,
    onContextMenu,
    open: position !== null,
    position
  };
}
