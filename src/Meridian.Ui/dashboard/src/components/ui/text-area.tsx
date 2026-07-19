import { forwardRef, type ReactNode, type TextareaHTMLAttributes } from "react";
import { cn } from "@/lib/utils";

export interface TextAreaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: ReactNode;
  error?: ReactNode;
}

/**
 * Multi-line text input. Concrete: flat field, hairline border with a hover-darkened rule and
 * teal focus ring, 2px corners; small-caps label and dim-red error line.
 */
export const TextArea = forwardRef<HTMLTextAreaElement, TextAreaProps>(
  ({ label, error, rows = 4, className, id, ...props }, ref) => {
    const control = (
      <textarea
        ref={ref}
        id={id}
        rows={rows}
        aria-invalid={error ? true : undefined}
        className={cn(
          "w-full rounded-[2px] border bg-[var(--surface-input)] px-2.5 py-2 font-sans text-sm leading-relaxed text-foreground placeholder:text-muted-foreground/70",
          "transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          "disabled:cursor-not-allowed disabled:opacity-55",
          "resize-y",
          error ? "border-danger/60 focus-visible:ring-danger/40" : "border-border hover:border-[var(--border-hover)] focus-visible:border-primary",
          className
        )}
        {...props}
      />
    );

    if (!label && !error) {
      return control;
    }

    return (
      <label className="block">
        {label ? (
          <span className="mb-1.5 block font-sans text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
            {label}
          </span>
        ) : null}
        {control}
        {error ? <span className="mt-1.5 block text-xs leading-5 text-danger">{error}</span> : null}
      </label>
    );
  }
);

TextArea.displayName = "TextArea";
