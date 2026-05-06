import { type HTMLAttributes, type ReactNode } from "react";
import { X } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  buildDialogCloseButtonViewModel,
  buildDialogContentViewModel,
  useDialogInteractionViewModel
} from "@/components/ui/dialog.view-model";

/**
 * Controlled modal dialog with backdrop, focus trap, and Escape-to-close.
 *
 * **Usage pattern:** control open state externally via `open` / `onOpenChange`.
 * The dialog renders `null` when closed, so no lazy-loading is needed.
 *
 * **Sub-components:**
 * - `DialogContent` — the white/card panel, handles `role="dialog"` and `aria-modal`.
 * - `DialogHeader` — spacing wrapper for title and description.
 * - `DialogTitle` — `<h2>` — pass an `id` and reference it via `aria-labelledby` on `DialogContent`.
 * - `DialogDescription` — muted subtitle — reference via `aria-describedby` on `DialogContent`.
 * - `DialogCloseButton` — absolute-positioned ✕ button with optional `disabledReason` tooltip.
 *
 * Clicking the backdrop or pressing Escape calls `onOpenChange(false)`.
 *
 * @example
 * <Dialog open={open} onOpenChange={setOpen}>
 *   <DialogContent aria-labelledby="title-id" aria-describedby="desc-id">
 *     <DialogHeader>
 *       <DialogTitle id="title-id">Confirm cancellation</DialogTitle>
 *       <DialogDescription id="desc-id">This action cannot be undone.</DialogDescription>
 *     </DialogHeader>
 *     <div className="flex justify-end gap-3 pt-2">
 *       <Button variant="outline" onClick={() => setOpen(false)}>Cancel</Button>
 *       <Button onClick={handleConfirm}>Confirm</Button>
 *     </div>
 *   </DialogContent>
 * </Dialog>
 */
interface DialogProps {
  open: boolean;
  onOpenChange?: (open: boolean) => void;
  children: ReactNode;
}

export function Dialog({ open, onOpenChange, children }: DialogProps) {
  const vm = useDialogInteractionViewModel({ open, onOpenChange });

  if (!open) {
    return null;
  }

  return (
    <div
      ref={vm.overlayRef}
      className="fixed inset-0 z-50 flex items-center justify-center bg-background/70 p-4"
      onMouseDown={vm.handleBackdropMouseDown}
    >
      {children}
    </div>
  );
}

export function DialogContent({ className, tabIndex = -1, ...props }: HTMLAttributes<HTMLDivElement>) {
  const vm = buildDialogContentViewModel(tabIndex);

  return (
    <div
      role={vm.role}
      aria-modal={vm.ariaModal}
      tabIndex={vm.tabIndex}
      className={cn(vm.className, className)}
      {...props}
    />
  );
}

export function DialogHeader({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div className={cn("mb-4 space-y-2", className)} {...props} />;
}

export function DialogTitle({ className, ...props }: HTMLAttributes<HTMLHeadingElement>) {
  return <h2 className={cn("text-lg font-semibold", className)} {...props} />;
}

export function DialogDescription({ className, ...props }: HTMLAttributes<HTMLParagraphElement>) {
  return <p className={cn("text-sm leading-6 text-muted-foreground", className)} {...props} />;
}

interface DialogCloseButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  label?: string;
  disabledReason?: string | null;
}

export function DialogCloseButton({
  label = "Close dialog",
  disabled = false,
  disabledReason = null,
  className,
  ...props
}: DialogCloseButtonProps) {
  const vm = buildDialogCloseButtonViewModel({ label, disabled, disabledReason });

  return (
    <button
      type={vm.type}
      aria-label={vm.ariaLabel}
      title={vm.title}
      disabled={vm.disabled}
      className={cn(vm.className, className)}
      {...props}
    >
      <X className="h-4 w-4" aria-hidden={vm.iconAriaHidden} />
    </button>
  );
}
