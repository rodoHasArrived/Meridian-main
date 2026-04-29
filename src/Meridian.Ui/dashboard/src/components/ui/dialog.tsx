import { type HTMLAttributes, type ReactNode } from "react";
import { cn } from "@/lib/utils";
import { useDialogInteractionViewModel } from "@/components/ui/dialog.view-model";

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
  return (
    <div
      role="dialog"
      aria-modal="true"
      tabIndex={tabIndex}
      className={cn("w-full max-w-lg rounded-lg border border-border bg-card p-5 text-card-foreground shadow-float backdrop-blur-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40", className)}
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
