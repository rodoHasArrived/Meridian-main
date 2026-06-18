import { type HTMLAttributes, type ReactNode, useCallback, useEffect, useRef } from "react";
import { X } from "lucide-react";
import { resolveDialogTabTarget, resolveInitialDialogFocus } from "@/components/ui/dialog.view-model";
import { cn } from "@/lib/utils";

/**
 * Slide-in side panel (drawer) for contextual controls and detail views.
 * Renders over the main content with a semi-transparent backdrop.
 *
 * **Side:** defaults to `"right"` (panel slides in from the right edge). Pass `side="left"`
 * for panels anchored to a left-side context area. The `SheetContent` `side` prop must match.
 *
 * **Sub-components:**
 * - `SheetContent` — the panel surface; handles `role="dialog"`, `aria-modal`, and border side.
 * - `SheetHeader` — sticky header strip with bottom border.
 * - `SheetTitle` — `<h2>` inside the header.
 * - `SheetDescription` — muted subtitle below the title.
 * - `SheetBody` — scrollable body with `flex-1` and consistent padding.
 * - `SheetCloseButton` — absolute-positioned ✕ button in the top-right corner.
 *
 * Pressing Escape or clicking the backdrop calls `onOpenChange(false)`.
 *
 * @example
 * <Sheet open={open} onOpenChange={setOpen}>
 *   <SheetContent>
 *     <SheetHeader>
 *       <SheetTitle>Strategy controls</SheetTitle>
 *       <SheetDescription>Pause or stop the active strategy run.</SheetDescription>
 *       <SheetCloseButton onClick={() => setOpen(false)} />
 *     </SheetHeader>
 *     <SheetBody>…controls…</SheetBody>
 *   </SheetContent>
 * </Sheet>
 */
interface SheetProps {
  open: boolean;
  onOpenChange?: (open: boolean) => void;
  /** Which edge the panel anchors from. Defaults to `"right"`. */
  side?: "left" | "right";
  children: ReactNode;
}

export function Sheet({ open, onOpenChange, side = "right", children }: SheetProps) {
  const overlayRef = useRef<HTMLDivElement>(null);
  const restoreFocusRef = useRef<HTMLElement | null>(null);

  const requestClose = useCallback(() => {
    onOpenChange?.(false);
  }, [onOpenChange]);

  const handleDocumentKeyDown = useCallback((event: KeyboardEvent) => {
    if (!open) {
      return;
    }

    if (event.key === "Escape") {
      event.preventDefault();
      requestClose();
      return;
    }

    if (event.key !== "Tab") {
      return;
    }

    const nextFocus = resolveDialogTabTarget(overlayRef.current, getActiveElement(), event.shiftKey);
    if (!nextFocus) {
      return;
    }

    event.preventDefault();
    nextFocus.focus();
  }, [open, requestClose]);

  useEffect(() => {
    if (!open || typeof window === "undefined") return undefined;

    window.addEventListener("keydown", handleDocumentKeyDown, true);
    return () => window.removeEventListener("keydown", handleDocumentKeyDown, true);
  }, [handleDocumentKeyDown, open]);

  useEffect(() => {
    if (!open) return undefined;

    restoreFocusRef.current = getActiveElement();
    const cancelFocus = scheduleFocus(() => {
      resolveInitialDialogFocus(overlayRef.current, getActiveElement())?.focus();
    });

    return () => {
      cancelFocus();

      const restoreFocus = restoreFocusRef.current;
      restoreFocusRef.current = null;

      if (restoreFocus?.isConnected) {
        restoreFocus.focus();
      }
    };
  }, [open]);

  if (!open) return null;

  return (
    <div
      ref={overlayRef}
      className={cn(
        "fixed inset-0 z-50 flex bg-background/70",
        side === "left" ? "justify-start" : "justify-end"
      )}
      onMouseDown={(e) => {
        if (e.target === overlayRef.current) requestClose();
      }}
    >
      {children}
    </div>
  );
}

function getActiveElement(): HTMLElement | null {
  if (typeof document === "undefined") {
    return null;
  }

  return document.activeElement instanceof HTMLElement ? document.activeElement : null;
}

function scheduleFocus(callback: () => void): () => void {
  if (typeof window === "undefined") {
    return () => undefined;
  }

  if (typeof window.requestAnimationFrame === "function") {
    const frame = window.requestAnimationFrame(callback);
    return () => window.cancelAnimationFrame(frame);
  }

  const timeout = window.setTimeout(callback, 0);
  return () => window.clearTimeout(timeout);
}

interface SheetContentProps extends HTMLAttributes<HTMLDivElement> {
  /** Which edge the panel border appears on. Must match the parent `Sheet` side. Defaults to `"right"` (left border). */
  side?: "left" | "right";
}

export function SheetContent({ className, side = "right", ...props }: SheetContentProps) {
  return (
    <div
      role="dialog"
      aria-modal="true"
      tabIndex={-1}
      className={cn(
        "relative flex h-full w-full max-w-2xl flex-col overflow-y-auto border-border bg-card shadow-float focus:outline-none",
        side === "left" ? "border-r sheet-slide-left" : "border-l sheet-slide-right",
        className
      )}
      {...props}
    />
  );
}

export function SheetHeader({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn("sticky top-0 z-10 border-b border-border bg-card px-6 py-4", className)}
      {...props}
    />
  );
}

export function SheetTitle({ className, ...props }: HTMLAttributes<HTMLHeadingElement>) {
  return (
    <h2
      className={cn("flex items-center gap-2 text-base font-semibold text-foreground", className)}
      {...props}
    />
  );
}

export function SheetDescription({ className, ...props }: HTMLAttributes<HTMLParagraphElement>) {
  return (
    <p className={cn("mt-1 text-sm leading-5 text-muted-foreground", className)} {...props} />
  );
}

export function SheetBody({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("flex-1 space-y-4 p-6", className)} {...props} />;
}

interface SheetCloseButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  label?: string;
}

export function SheetCloseButton({ label = "Close panel", className, onClick, ...props }: SheetCloseButtonProps) {
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      className={cn(
        "absolute right-4 top-4 flex h-8 w-8 items-center justify-center rounded-[var(--radius-button,0.375rem)] text-muted-foreground transition-colors hover:bg-secondary/50 hover:text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40",
        className
      )}
      onClick={onClick}
      {...props}
    >
      <X className="h-4 w-4" aria-hidden="true" />
    </button>
  );
}
