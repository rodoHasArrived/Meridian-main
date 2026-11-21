import { MoreHorizontal } from "lucide-react";
import { useCallback, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from "@/components/ui/context-menu";

export interface OverflowMenuProps {
  /** Accessible name for the trigger and the menu, e.g. "More order actions". */
  label: string;
  items: ContextMenuEntry[];
  className?: string;
}

/**
 * A "more actions" button that opens a menu beneath it. Use it for actions that should not sit
 * beside the primary action: rare ones, and destructive ones such as "Cancel all open orders",
 * which still go through their own confirmation step after being chosen.
 */
export function OverflowMenu({ label, items, className }: OverflowMenuProps) {
  const triggerRef = useRef<HTMLButtonElement>(null);
  const [position, setPosition] = useState<ContextMenuPosition | null>(null);

  const toggle = useCallback(() => {
    setPosition((current) => {
      if (current) {
        return null;
      }
      const rect = triggerRef.current?.getBoundingClientRect();
      // The menu clamps itself to the viewport, so a trigger at the right edge still opens on screen.
      return rect ? { x: rect.left, y: rect.bottom + 4 } : { x: 8, y: 8 };
    });
  }, []);

  const close = useCallback(() => {
    setPosition(null);
    // Focus goes back to the trigger unless the chosen action already moved it (a dialog opening).
    window.setTimeout(() => {
      if (!document.activeElement || document.activeElement === document.body) {
        triggerRef.current?.focus();
      }
    }, 0);
  }, []);

  return (
    <>
      <Button
        ref={triggerRef}
        type="button"
        size="icon"
        variant="outline"
        className={className}
        aria-label={label}
        aria-haspopup="menu"
        aria-expanded={position !== null}
        title={label}
        onClick={toggle}
        // With every item disabled, focus stays on the trigger, so Escape has to work from here too.
        onKeyDown={(event) => {
          if (event.key === "Escape" && position !== null) {
            event.preventDefault();
            close();
          }
        }}
      >
        <MoreHorizontal className="h-4 w-4" aria-hidden="true" />
      </Button>
      <ContextMenu open={position !== null} position={position} items={items} label={label} onClose={close} />
    </>
  );
}
