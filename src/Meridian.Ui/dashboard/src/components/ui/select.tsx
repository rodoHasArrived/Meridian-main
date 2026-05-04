import { forwardRef } from "react";
import { cn } from "@/lib/utils";

/**
 * Native `<select>` with Meridian styling and a custom chevron overlay.
 * Appearance is reset (`appearance-none`) so the chevron SVG is consistent across browsers.
 *
 * **Error state:** set `error` to apply danger border and focus ring tones and add
 * `aria-invalid` for screen readers. Mirror the `Input` error behaviour.
 *
 * **Placeholder:** pass `placeholder` to render a disabled first `<option>` with that text.
 * Useful for required selects that should not have a default value.
 *
 * Populate options as children: `<option value="buy">Buy</option>`.
 *
 * @example
 * <Select value={side} onChange={(e) => setSide(e.target.value)} error={!side}>
 *   <option value="buy">Buy</option>
 *   <option value="sell">Sell</option>
 * </Select>
 */
export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  error?: boolean;
  placeholder?: string;
}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(
  ({ children, className, error = false, placeholder, ...props }, ref) => (
    <div className="relative flex items-center">
      <select
        ref={ref}
        className={cn(
          "w-full appearance-none rounded-md border bg-secondary/40 text-sm text-foreground",
          "min-h-9 py-2 pl-3 pr-8",
          "transition-colors duration-150",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          "disabled:cursor-not-allowed disabled:opacity-50",
          error
            ? "border-danger/60 focus-visible:ring-danger/40"
            : "border-border/80 hover:border-border focus-visible:border-primary/60",
          className
        )}
        aria-invalid={error || undefined}
        {...props}
      >
        {placeholder && (
          <option value="" disabled>
            {placeholder}
          </option>
        )}
        {children}
      </select>
      <span aria-hidden="true" className="pointer-events-none absolute right-2.5 flex items-center text-muted-foreground">
        <svg
          xmlns="http://www.w3.org/2000/svg"
          viewBox="0 0 16 16"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.75"
          className="h-3.5 w-3.5"
        >
          <path d="M4 6l4 4 4-4" />
        </svg>
      </span>
    </div>
  )
);

Select.displayName = "Select";
