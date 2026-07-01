import { type HTMLAttributes, type ReactNode, useCallback, useEffect, useRef } from "react";
import { X } from "lucide-react";
import { resolveDialogTabTarget, resolveInitialDialogFocus } from "@/components/ui/dialog.view-model";
import { cn } from "@/lib/utils";

type DrawerSide = "left" | "right";

export interface DrawerProps {
  open: boolean;
  onClose?: () => void;
  side?: DrawerSide;
  title?: ReactNode;
  closeOnEsc?: boolean;
  closeOnBackdrop?: boolean;
  children?: ReactNode;
  className?: string;
}

function getActiveElement(): HTMLElement | null {
  if (typeof document === "undefined") {
    return null;
  }
  return document.activeElement instanceof HTMLElement ? document.activeElement : null;
}

/**
 * Side panel for inspection/filters without leaving context. Slides from an edge,
 * traps focus, and dismisses via Escape or backdrop. Concrete: flat panel, hairline
 * border, the detached overlay carries the only shadow.
 *
 * Compose the body with `DrawerHeader` / `DrawerBody` / `DrawerFooter`, or pass a `title`
 * for the default header + close button.
 */
export function Drawer({
  open,
  onClose,
  side = "right",
  title,
  closeOnEsc = true,
  closeOnBackdrop = true,
  children,
  className
}: DrawerProps) {
  const overlayRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);

  const requestClose = useCallback(() => onClose?.(), [onClose]);

  useEffect(() => {
    if (!open || typeof window === "undefined") {
      return undefined;
    }

    const handleKeyDown = (event: KeyboardEvent) => {
      if (closeOnEsc && event.key === "Escape") {
        event.preventDefault();
        requestClose();
        return;
      }
      if (event.key !== "Tab") {
        return;
      }
      const nextFocus = resolveDialogTabTarget(panelRef.current, getActiveElement(), event.shiftKey);
      if (nextFocus) {
        event.preventDefault();
        nextFocus.focus();
      }
    };

    window.addEventListener("keydown", handleKeyDown, true);
    return () => window.removeEventListener("keydown", handleKeyDown, true);
  }, [open, closeOnEsc, requestClose]);

  useEffect(() => {
    if (!open) {
      return undefined;
    }
    restoreFocusRef.current = getActiveElement();
    const frame = window.requestAnimationFrame(() => {
      resolveInitialDialogFocus(panelRef.current, getActiveElement())?.focus();
    });
    return () => {
      window.cancelAnimationFrame(frame);
      const restore = restoreFocusRef.current;
      restoreFocusRef.current = null;
      if (restore?.isConnected) {
        restore.focus();
      }
    };
  }, [open]);

  if (!open) {
    return null;
  }

  return (
    <div
      ref={overlayRef}
      className={cn("fixed inset-0 z-50 flex bg-background/70", side === "left" ? "justify-start" : "justify-end")}
      onMouseDown={(event) => {
        if (closeOnBackdrop && event.target === overlayRef.current) {
          requestClose();
        }
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        tabIndex={-1}
        className={cn(
          "flex h-full w-full max-w-[360px] flex-col bg-card shadow-[0_2px_6px_rgba(0,0,0,0.18)] focus:outline-none",
          side === "left" ? "border-r border-border sheet-slide-left" : "border-l border-border sheet-slide-right",
          className
        )}
      >
        {title ? <DrawerHeader title={title} onClose={onClose} /> : null}
        {children}
      </div>
    </div>
  );
}

export interface DrawerHeaderProps extends Omit<HTMLAttributes<HTMLDivElement>, "title"> {
  title?: ReactNode;
  onClose?: () => void;
}

export function DrawerHeader({ title, onClose, className, children, ...props }: DrawerHeaderProps) {
  return (
    <div
      className={cn(
        "flex items-center justify-between gap-2 border-b border-border bg-[#F3F6F9] px-4 py-3",
        className
      )}
      {...props}
    >
      {title ? <h2 className="text-sm font-semibold text-foreground">{title}</h2> : null}
      {children}
      {onClose ? (
        <button
          type="button"
          aria-label="Close drawer"
          onClick={onClose}
          className="ml-auto inline-flex h-8 w-8 items-center justify-center rounded-[2px] text-muted-foreground transition-colors [outline-offset:-2px] hover:bg-[#EAEEF3] hover:text-foreground focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        >
          <X className="h-4 w-4" aria-hidden="true" />
        </button>
      ) : null}
    </div>
  );
}

export function DrawerBody({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("flex-1 space-y-4 overflow-y-auto p-4 text-sm text-foreground", className)} {...props} />;
}

export function DrawerFooter({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn("flex flex-wrap items-center justify-end gap-2 border-t border-border px-4 py-3", className)}
      {...props}
    />
  );
}
