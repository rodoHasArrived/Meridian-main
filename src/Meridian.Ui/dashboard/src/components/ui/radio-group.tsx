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

  const move = (currentValue: string, delta: number) => {
    if (enabled.length === 0) {
      return;
    }
    const index = enabled.findIndex((option) => option.value === currentValue);
    const nextIndex = index < 0 ? 0 : (index + delta + enabled.length) % enabled.length;
    onChange?.(enabled[nextIndex]!.value);
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
            <span
              aria-hidden="true"
              className={cn(
                "mt-px flex h-4 w-4 shrink-0 items-center justify-center rounded-full border bg-[#F3F6F9] transition-colors",
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
              tabIndex={checked || (!value && option.value === enabled[0]?.value) ? 0 : -1}
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
