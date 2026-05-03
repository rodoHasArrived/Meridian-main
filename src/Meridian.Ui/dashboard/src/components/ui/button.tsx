import { Children, cloneElement, forwardRef, isValidElement } from "react";
import { Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";
import { buildButtonCommandViewModel } from "@/components/ui/button.view-model";

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  asChild?: boolean;
  variant?: "default" | "secondary" | "outline" | "ghost" | "destructive";
  size?: "default" | "sm";
  busy?: boolean;
  busyLabel?: string | null;
  disabledReason?: string | null;
}

const variantClasses: Record<NonNullable<ButtonProps["variant"]>, string> = {
  default: "border-primary/40 bg-primary text-primary-foreground hover:bg-primary/85",
  secondary: "border-border/80 bg-secondary text-secondary-foreground hover:bg-secondary/80",
  outline: "border-border/80 bg-transparent text-foreground hover:bg-secondary/60",
  ghost: "border-transparent bg-transparent text-muted-foreground hover:bg-secondary/55 hover:text-foreground",
  destructive: "border-danger/40 bg-danger/10 text-danger hover:bg-danger/15"
};

const sizeClasses: Record<NonNullable<ButtonProps["size"]>, string> = {
  default: "min-h-9 px-4 py-2 text-sm",
  sm: "min-h-8 px-3 py-1.5 text-xs"
};

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(
  ({
    asChild = false,
    children,
    className,
    variant = "default",
    size = "default",
    busy = false,
    busyLabel = null,
    disabled = false,
    disabledReason = null,
    title,
    ...props
  }, ref) => {
    const vm = buildButtonCommandViewModel({ disabled, busy, busyLabel, disabledReason, title });
    const classes = cn(
      "inline-flex items-center justify-center gap-2 rounded-md border font-semibold transition-colors duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-50",
      variantClasses[variant],
      sizeClasses[size],
      vm.disabled && asChild && "pointer-events-none opacity-50",
      className
    );

    if (asChild && isValidElement(children)) {
      const child = Children.only(children) as React.ReactElement<{
        className?: string;
        "aria-busy"?: true;
        "aria-disabled"?: true;
        title?: string;
      }>;

      return cloneElement(child, {
        className: cn(classes, child.props.className),
        "aria-busy": vm.ariaBusy,
        "aria-disabled": vm.ariaDisabled,
        title: vm.title ?? child.props.title
      });
    }

    return (
      <button
        ref={ref}
        className={classes}
        disabled={vm.disabled}
        aria-busy={vm.ariaBusy}
        title={vm.title}
        {...props}
      >
        {vm.showBusyIndicator && <Loader2 className="h-4 w-4 animate-spin" aria-hidden={vm.iconAriaHidden} />}
        {vm.displayBusyLabel ?? children}
      </button>
    );
  }
);

Button.displayName = "Button";
