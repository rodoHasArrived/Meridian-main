import { type HTMLAttributes, type ReactNode } from "react";
import { X } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  buildDialogCloseButtonViewModel,
  buildDialogContentViewModel,
  useDialogInteractionViewModel
} from "@/components/ui/dialog.view-model";

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
