import { Children, cloneElement, forwardRef, isValidElement } from "react";
import { cn } from "@/lib/utils";

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  asChild?: boolean;
  variant?: "default" | "secondary" | "outline" | "ghost" | "destructive";
  size?: "default" | "sm";
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
  ({ asChild = false, children, className, variant = "default", size = "default", ...props }, ref) => {
    const classes = cn(
      "inline-flex items-center justify-center gap-2 rounded-md border font-semibold transition-colors duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-50",
      variantClasses[variant],
      sizeClasses[size],
      className
    );

    if (asChild && isValidElement(children)) {
      const child = Children.only(children) as React.ReactElement<{ className?: string }>;

      return cloneElement(child, {
        className: cn(classes, child.props.className)
      });
    }

    return (
      <button
        ref={ref}
        className={classes}
        {...props}
      >
        {children}
      </button>
    );
  }
);

Button.displayName = "Button";
