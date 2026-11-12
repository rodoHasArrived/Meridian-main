import { type KeyboardEvent, type ReactNode, useId } from "react";
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
  const groupName = name ?? generatedName;
  const items = normalize(options);
  const enabled = items.filter((option) => !(disabled || option.disabled));
  const tabStop = enabled.find((option) => option.value === value)?.value ?? enabled[0]?.value;

  const select = (event: KeyboardEvent<HTMLLabelElement>, optionValue: string) => {
    const group = event.currentTarget.closest("[role='radiogroup']");
    const target = Array.from(group?.querySelectorAll<HTMLInputElement>("input[type='radio']") ?? [])
      .find((input) => input.value === optionValue);
    target?.focus();
    onChange?.(optionValue);
  };

  const move = (event: KeyboardEvent<HTMLLabelElement>, currentValue: string, delta: number) => {
    if (enabled.length === 0) {
      return;
    }
    const index = enabled.findIndex((option) => option.value === currentValue);
    const nextIndex = index < 0 ? 0 : (index + delta + enabled.length) % enabled.length;
    select(event, enabled[nextIndex]!.value);
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLLabelElement>, optionValue: string) => {
    switch (event.key) {
      case "ArrowDown":
      case "ArrowRight":
        event.preventDefault();
        move(event, optionValue, 1);
        break;
      case "ArrowUp":
      case "ArrowLeft":
        event.preventDefault();
        move(event, optionValue, -1);
        break;
      case " ":
      case "Enter":
        event.preventDefault();
        select(event, optionValue);
        break;
      default:
        break;
    }
  };

  return (
    <div
      role="radiogroup"
      aria-label={ariaLabel}
      className={cn("flex", orientation === "horizontal" ? "flex-row flex-wrap gap-4" : "flex-col gap-2.5", className)}
    >
      {items.map((option) => {
        const checked = option.value === value;
        const itemDisabled = disabled || option.disabled;
        return (
          <label
            key={option.value}
            className={cn(
              "inline-flex items-start gap-2 text-sm",
              itemDisabled ? "cursor-not-allowed opacity-55" : "cursor-pointer"
            )}
            onKeyDown={(event) => !itemDisabled && handleKeyDown(event, option.value)}
          >
            <input
              type="radio"
              name={groupName}
              value={option.value}
              checked={checked}
              disabled={itemDisabled}
              onChange={() => onChange?.(option.value)}
              className="peer sr-only"
              tabIndex={option.value === tabStop ? 0 : -1}
            />
            <span
              aria-hidden="true"
              className={cn(
                "mt-px flex h-4 w-4 shrink-0 items-center justify-center rounded-full border bg-[var(--ws-surface-raised)] transition-colors peer-focus-visible:ring-2 peer-focus-visible:ring-[var(--interaction-focus)]",
                checked ? "border-[var(--interaction-selected)]" : "border-border"
              )}
            >
              {checked ? <span className="h-2 w-2 rounded-full bg-[var(--interaction-selected)]" /> : null}
            </span>
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
