import { forwardRef, type ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * Single-line text input with Meridian styling. Wraps a native `<input>` in a relative
 * container so leading/trailing icon slots can be absolutely positioned.
 *
 * **Error state:** set `error` to switch the border and focus ring to danger tones and add
 * `aria-invalid` for screen readers.
 *
 * **Icons:** pass a Lucide icon (or any ReactNode) to `leadingIcon` / `trailingIcon`. Icons
 * are `pointer-events-none` so they never interfere with click/focus on the input itself.
 *
 * Focus ring uses `focus-visible:` so it does not appear on mouse click.
 *
 * @example
 * <Input
 *   placeholder="AAPL"
 *   leadingIcon={<Search className="h-4 w-4" />}
 *   error={fieldInvalid}
 *   aria-describedby="symbol-help"
 * />
 */
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
            "w-full rounded-[2px] border bg-[#F3F6F9] text-sm text-foreground placeholder:text-muted-foreground/60",
            "min-h-9 px-3 py-2",
            "transition-[background-color,border-color] duration-150",
            "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
            "disabled:cursor-not-allowed disabled:opacity-50",
            error
              ? "border-danger/60 focus-visible:ring-danger/40"
              : "border-border hover:border-[#ADB8C4] focus-visible:border-primary",
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
