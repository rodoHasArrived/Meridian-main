import { type ReactNode, useState } from "react";
import { cn } from "@/lib/utils";

export interface NumberInputProps {
  value?: number;
  defaultValue?: number;
  onChange?: (value: number) => void;
  label?: ReactNode;
  min?: number;
  max?: number;
  step?: number;
  disabled?: boolean;
  placeholder?: string;
  error?: boolean;
  className?: string;
  id?: string;
  "aria-label"?: string;
}

/**
 * Numeric input with +/- steppers. Concrete: flat field, hairline border, mono value, 2px
 * corners; buttons clamp to `min`/`max`. Controlled via `value`, or uncontrolled via
 * `defaultValue`.
 */
export function NumberInput({
  value,
  defaultValue = 0,
  onChange,
  label,
  min,
  max,
  step = 1,
  disabled = false,
  placeholder,
  error = false,
  className,
  id,
  "aria-label": ariaLabel
}: NumberInputProps) {
  const controlled = value !== undefined;
  const [internal, setInternal] = useState(defaultValue);
  const current = controlled ? value! : internal;

  const clamp = (next: number) => {
    let result = next;
    if (min !== undefined && result < min) result = min;
    if (max !== undefined && result > max) result = max;
    return result;
  };

  const commit = (next: number) => {
    const clamped = clamp(next);
    if (!controlled) {
      setInternal(clamped);
    }
    onChange?.(clamped);
  };

  return (
    <div className={className}>
      {label ? (
        <label htmlFor={id} className="mb-1.5 block font-mono text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          {label}
        </label>
      ) : null}
      <div
        className={cn(
          "flex h-9 items-center rounded-[2px] border bg-[#F3F6F9]",
          "focus-within:border-primary focus-within:ring-2 focus-within:ring-primary/40",
          error ? "border-danger/60" : "border-border",
          disabled && "cursor-not-allowed opacity-55"
        )}
      >
        <button
          type="button"
          aria-label="Decrement"
          disabled={disabled || (min !== undefined && current <= min)}
          onClick={() => commit(current - step)}
          className="flex h-full w-8 items-center justify-center border-r border-border text-base font-semibold text-foreground transition-colors hover:bg-[#EAEEF3] focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-50 [outline-offset:-2px]"
        >
          −
        </button>
        <input
          id={id}
          type="number"
          inputMode="decimal"
          aria-label={ariaLabel}
          value={current}
          min={min}
          max={max}
          step={step}
          disabled={disabled}
          placeholder={placeholder}
          onChange={(event) => commit(Number.parseFloat(event.target.value) || 0)}
          className="min-w-0 flex-1 bg-transparent px-2 text-center font-mono text-sm text-foreground focus:outline-none"
        />
        <button
          type="button"
          aria-label="Increment"
          disabled={disabled || (max !== undefined && current >= max)}
          onClick={() => commit(current + step)}
          className="flex h-full w-8 items-center justify-center border-l border-border text-base font-semibold text-foreground transition-colors hover:bg-[#EAEEF3] focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:opacity-50 [outline-offset:-2px]"
        >
          +
        </button>
      </div>
    </div>
  );
}
