import { Check } from "lucide-react";
import { forwardRef, useState, type ChangeEvent, type InputHTMLAttributes, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, "onChange" | "type"> {
  label?: ReactNode;
  hint?: ReactNode;
  error?: ReactNode;
  onCheckedChange?: (checked: boolean) => void;
  onChange?: (event: ChangeEvent<HTMLInputElement>) => void;
}

export const Checkbox = forwardRef<HTMLInputElement, CheckboxProps>(
  ({ checked, className, defaultChecked = false, disabled = false, error, hint, label, onChange, onCheckedChange, ...props }, ref) => {
    const controlled = checked !== undefined;
    const [uncontrolledChecked, setUncontrolledChecked] = useState(Boolean(defaultChecked));
    const checkedState = controlled ? Boolean(checked) : uncontrolledChecked;

    const handleChange = (event: ChangeEvent<HTMLInputElement>) => {
      if (!controlled) {
        setUncontrolledChecked(event.target.checked);
      }

      onChange?.(event);
      onCheckedChange?.(event.target.checked);
    };

    return (
      <label className={cn("inline-flex items-start gap-2.5 text-sm", disabled ? "cursor-not-allowed opacity-55" : "cursor-pointer", className)}>
        <span className="relative mt-0.5 inline-flex h-4 w-4 shrink-0">
          <input
            ref={ref}
            type="checkbox"
            checked={checkedState}
            disabled={disabled}
            onChange={handleChange}
            className="peer sr-only"
            aria-invalid={error ? true : undefined}
            {...props}
          />
          <span
            aria-hidden="true"
            className={cn(
              "flex h-4 w-4 items-center justify-center rounded-[var(--radius-checkbox,0.1875rem)] border bg-background shadow-[var(--shadow-panel)]",
              "transition-[background-color,border-color,box-shadow] duration-150",
              "peer-focus-visible:ring-2 peer-focus-visible:ring-primary/40",
              checkedState ? "border-primary bg-primary" : "border-border",
              error ? "border-danger" : ""
            )}
          >
            <Check className={cn("h-3 w-3 text-primary-foreground transition-opacity", checkedState ? "opacity-100" : "opacity-0")} />
          </span>
        </span>
        {(label || hint || error) ? (
          <span className="grid min-w-0 gap-0.5">
            {label ? <span className="font-medium text-foreground">{label}</span> : null}
            {error ? <span className="text-xs leading-5 text-danger">{error}</span> : hint ? <span className="text-xs leading-5 text-muted-foreground">{hint}</span> : null}
          </span>
        ) : null}
      </label>
    );
  }
);

Checkbox.displayName = "Checkbox";

export interface ToggleProps extends Omit<React.ButtonHTMLAttributes<HTMLButtonElement>, "onChange"> {
  checked?: boolean;
  defaultChecked?: boolean;
  label?: ReactNode;
  onCheckedChange?: (checked: boolean) => void;
}

export const Toggle = forwardRef<HTMLButtonElement, ToggleProps>(
  ({ checked, className, defaultChecked = false, disabled = false, label, onCheckedChange, onClick, ...props }, ref) => {
    const controlled = checked !== undefined;
    const [uncontrolledChecked, setUncontrolledChecked] = useState(Boolean(defaultChecked));
    const checkedState = controlled ? Boolean(checked) : uncontrolledChecked;

    const handleClick: React.MouseEventHandler<HTMLButtonElement> = (event) => {
      onClick?.(event);

      if (event.defaultPrevented || disabled) {
        return;
      }

      const nextChecked = !checkedState;
      if (!controlled) {
        setUncontrolledChecked(nextChecked);
      }

      onCheckedChange?.(nextChecked);
    };

    return (
      <button
        ref={ref}
        type="button"
        role="switch"
        aria-checked={checkedState}
        disabled={disabled}
        className={cn(
          "inline-flex items-center gap-2.5 text-sm text-foreground transition-opacity",
          "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
          disabled ? "cursor-not-allowed opacity-55" : "cursor-pointer",
          className
        )}
        onClick={handleClick}
        {...props}
      >
        <span
          aria-hidden="true"
          className={cn(
            "relative h-5 w-9 rounded-full border shadow-[var(--shadow-panel)] transition-colors duration-150",
            checkedState ? "border-primary bg-primary" : "border-border bg-secondary"
          )}
        >
          <span
            className={cn(
              "absolute top-0.5 h-4 w-4 rounded-full bg-background shadow-[var(--shadow-panel)] transition-transform duration-150",
              checkedState ? "translate-x-[1.0625rem]" : "translate-x-0.5"
            )}
          />
        </span>
        {label ? <span>{label}</span> : null}
      </button>
    );
  }
);

Toggle.displayName = "Toggle";
