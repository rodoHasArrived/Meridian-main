import { forwardRef, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  error?: boolean;
  leadingIcon?: ReactNode;
  trailingIcon?: ReactNode;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(
  ({ className, error = false, leadingIcon, trailingIcon, type = "text", ...props }, ref) => {
    const hasLeading = Boolean(leadingIcon);
    const hasTrailing = Boolean(trailingIcon);

    return (
      <div className="relative flex items-center">
        {hasLeading && (
          <span
            aria-hidden="true"
            className="pointer-events-none absolute left-3 flex h-4 w-4 shrink-0 items-center justify-center text-muted-foreground"
          >
            {leadingIcon}
          </span>
        )}
        <input
          ref={ref}
          type={type}
          className={cn(
            "w-full rounded-md border bg-secondary/40 text-sm text-foreground placeholder:text-muted-foreground/60",
            "min-h-9 px-3 py-2",
            "transition-colors duration-150",
            "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
            "disabled:cursor-not-allowed disabled:opacity-50",
            error
              ? "border-danger/60 focus-visible:ring-danger/40"
              : "border-border/80 hover:border-border focus-visible:border-primary/60",
            hasLeading && "pl-9",
            hasTrailing && "pr-9",
            className
          )}
          aria-invalid={error || undefined}
          {...props}
        />
        {hasTrailing && (
          <span
            aria-hidden="true"
            className="pointer-events-none absolute right-3 flex h-4 w-4 shrink-0 items-center justify-center text-muted-foreground"
          >
            {trailingIcon}
          </span>
        )}
      </div>
    );
  }
);

Input.displayName = "Input";
