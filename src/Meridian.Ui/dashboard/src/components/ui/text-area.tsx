import { forwardRef, useId, type ReactNode, type TextareaHTMLAttributes } from "react";
import { cn } from "@/lib/utils";
import { useFormFieldProps } from "@/components/ui/form-field-context";

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
    const generatedId = useId();
    const fieldProps = useFormFieldProps({ ...props, id }, Boolean(error));
    const controlId = fieldProps.id ?? (label || error ? `textarea-${generatedId}` : undefined);
    const errorId = error ? `${controlId}-textarea-error` : undefined;
    const describedBy = [fieldProps["aria-describedby"], errorId].filter(Boolean).join(" ") || undefined;
    const invalid = Boolean(fieldProps["aria-invalid"] && fieldProps["aria-invalid"] !== "false");
    const control = (
      <textarea
        ref={ref}
        id={controlId}
        rows={rows}
        className={cn(
          "w-full rounded-[2px] border bg-[var(--ws-surface-raised)] px-2.5 py-2 text-sm leading-relaxed text-foreground placeholder:text-muted-foreground/70",
          "transition-colors duration-150 focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40",
          "disabled:cursor-not-allowed disabled:opacity-55",
          "resize-y",
          invalid ? "border-danger/60 focus-visible:ring-danger/40" : "border-border hover:border-[var(--ws-border-hover)] focus-visible:border-primary",
          className
        )}
        {...props}
        aria-invalid={fieldProps["aria-invalid"]}
        aria-describedby={describedBy}
      />
    );

    if (!label && !error) {
      return control;
    }

    return (
      <div className="block">
        {label ? (
          <label htmlFor={controlId} className="form-field-label mb-1.5 block">
            {label}
          </label>
        ) : null}
        {control}
        {error ? <span id={errorId} className="mt-1.5 block text-xs leading-5 text-danger">{error}</span> : null}
      </div>
    );
  }
);

TextArea.displayName = "TextArea";
