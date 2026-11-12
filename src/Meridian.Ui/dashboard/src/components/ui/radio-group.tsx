import { type KeyboardEvent, type ReactNode, useId, useRef } from "react";
import { cn } from "@/lib/utils";

export interface RadioOption {
  value: string;
  label: ReactNode;
  hint?: ReactNode;
  disabled?: boolean;
}

export interface RadioGroupProps {
  options: Array<RadioOption | string>;
  value?: string;
  onChange?: (value: string) => void;
  name?: string;
  orientation?: "vertical" | "horizontal";
  disabled?: boolean;
  className?: string;
  "aria-label"?: string;
}

function normalize(options: Array<RadioOption | string>): RadioOption[] {
  return options.map((option) => (typeof option === "string" ? { value: option, label: option } : option));
}

/**
 * Single-select form primitive: round ring with a filled center dot. Roving-focus keyboard
 * nav per the Concrete keyboard matrix (Arrow keys move within the group, Space selects).
 */
export function RadioGroup({
  options,
  value,
  onChange,
  name,
  orientation = "vertical",
  disabled = false,
  className,
  "aria-label": ariaLabel
}: RadioGroupProps) {
  const generatedName = useId();
  const inputs = useRef(new Map<string, HTMLInputElement>());
  const groupName = name ?? generatedName;
  const items = normalize(options);
  const enabled = items.filter((option) => !(disabled || option.disabled));

  const tabStop = enabled.some((option) => option.value === value) ? value : enabled[0]?.value;

  const move = (currentValue: string, delta: number | "first" | "last") => {
    if (enabled.length === 0) {
      return;
    }
    const index = enabled.findIndex((option) => option.value === currentValue);
    const nextIndex = delta === "first" ? 0 : delta === "last" ? enabled.length - 1
      : index < 0 ? 0 : (index + delta + enabled.length) % enabled.length;
    const next = enabled[nextIndex]!.value;
    inputs.current.get(next)?.focus();
    onChange?.(next);
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLLabelElement>, optionValue: string) => {
    switch (event.key) {
      case "ArrowDown":
      case "ArrowRight":
        event.preventDefault();
        move(optionValue, 1);
        break;
      case "ArrowUp":
      case "ArrowLeft":
        event.preventDefault();
        move(optionValue, -1);
        break;
      case "Home":
        event.preventDefault();
        move(optionValue, "first");
        break;
      case "End":
        event.preventDefault();
        move(optionValue, "last");
        break;
      case " ":
      case "Enter":
        event.preventDefault();
        onChange?.(optionValue);
        break;
      default:
        break;
    }
  };

  return (
    <div
      role="radiogroup"
      aria-label={ariaLabel}
      aria-orientation={orientation}
      aria-disabled={disabled || undefined}
      className={cn("flex", orientation === "horizontal" ? "flex-row flex-wrap gap-4" : "flex-col gap-2.5", className)}
    >
      {items.map((option) => {
        const checked = option.value === value;
        const itemDisabled = disabled || option.disabled;
        return (
          <label
            key={option.value}
            className={cn(
              "inline-flex min-h-6 items-start gap-2 text-sm focus-within:outline-2 focus-within:outline-primary focus-within:outline-offset-2",
              itemDisabled ? "cursor-not-allowed opacity-55" : "cursor-pointer"
            )}
            onKeyDown={(event) => !itemDisabled && handleKeyDown(event, option.value)}
          >
            <span
              aria-hidden="true"
              className={cn(
                "mt-px flex h-4 w-4 shrink-0 items-center justify-center rounded-full border bg-[var(--ws-surface-raised)] transition-colors",
                checked ? "border-primary" : "border-border"
              )}
            >
              {checked ? <span className="h-2 w-2 rounded-full bg-primary" /> : null}
            </span>
            <input
              type="radio"
              name={groupName}
              value={option.value}
              checked={checked}
              disabled={itemDisabled}
              onChange={() => onChange?.(option.value)}
              className="sr-only"
              tabIndex={option.value === tabStop ? 0 : -1}
              ref={(element) => {
                if (element) inputs.current.set(option.value, element);
                else inputs.current.delete(option.value);
              }}
            />
            <span className="grid gap-0.5">
              <span className="leading-[18px] text-foreground">{option.label}</span>
              {option.hint ? <span className="text-xs leading-5 text-muted-foreground">{option.hint}</span> : null}
            </span>
          </label>
        );
      })}
    </div>
  );
}
