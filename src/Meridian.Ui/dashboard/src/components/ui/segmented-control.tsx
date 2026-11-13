import { useRef, type KeyboardEvent, type ReactNode } from "react";
import { cn } from "@/lib/utils";

export interface SegmentedOption {
  value: string;
  label: ReactNode;
  icon?: ReactNode;
  count?: ReactNode;
  disabled?: boolean;
}

export interface SegmentedControlProps {
  options: Array<SegmentedOption | string>;
  value?: string;
  onChange?: (value: string) => void;
  size?: "sm" | "md";
  fullWidth?: boolean;
  className?: string;
  "aria-label"?: string;
}

function normalize(options: Array<SegmentedOption | string>): SegmentedOption[] {
  return options.map((option) => (typeof option === "string" ? { value: option, label: option } : option));
}

/**
 * Compact mutually-exclusive view switch (table/chart, day/week/month). Denser than
 * `RadioGroup`; lives in toolbars. Concrete: inset track, flat active segment carried by a
 * hairline inset border and the shared control radius.
 */
export function SegmentedControl({
  options,
  value,
  onChange,
  size = "md",
  fullWidth = false,
  className,
  "aria-label": ariaLabel
}: SegmentedControlProps) {
  const items = normalize(options);
  const buttons = useRef<Array<HTMLButtonElement | null>>([]);
  const enabled = items.map((option, index) => ({ option, index })).filter(({ option }) => !option.disabled);
  const selectedIndex = items.findIndex((option) => option.value === value && !option.disabled);
  const tabStop = selectedIndex >= 0 ? selectedIndex : enabled[0]?.index;
  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    if (enabled.length === 0) return;
    const position = enabled.findIndex((item) => item.index === index);
    let target;
    switch (event.key) {
      case "ArrowRight":
      case "ArrowDown": target = enabled[(position + 1) % enabled.length]; break;
      case "ArrowLeft":
      case "ArrowUp": target = enabled[(position - 1 + enabled.length) % enabled.length]; break;
      case "Home": target = enabled[0]; break;
      case "End": target = enabled[enabled.length - 1]; break;
      default: return;
    }
    event.preventDefault();
    if (!target) return;
    buttons.current[target.index]?.focus();
    onChange?.(target.option.value);
  };

  return (
    <div
      role="tablist"
      aria-label={ariaLabel}
      className={cn(
        "inline-flex items-stretch gap-0.5 rounded-[var(--radius-button)] border border-border bg-[var(--ws-surface-raised)] p-0.5",
        fullWidth && "flex w-full",
        className
      )}
    >
      {items.map((option, index) => {
        const active = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            ref={(element) => { buttons.current[index] = element; }}
            role="tab"
            aria-selected={active}
            tabIndex={index === tabStop ? 0 : -1}
            disabled={option.disabled}
            onClick={() => !option.disabled && onChange?.(option.value)}
            onKeyDown={(event) => handleKeyDown(event, index)}
            className={cn(
              "inline-flex min-h-6 flex-1 items-center justify-center gap-1.5 whitespace-nowrap rounded-[var(--radius-button)] font-medium leading-none transition-colors",
              "focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-[var(--interaction-focus)]",
              size === "sm" ? "px-2.5 py-1 text-[11px]" : "px-3 py-1.5 text-xs",
              active
                ? "bg-card font-semibold text-foreground shadow-[inset_0_0_0_1px_hsl(var(--border))]"
                : "bg-transparent text-muted-foreground hover:bg-[var(--ws-row-hover)] hover:text-foreground",
              option.disabled && "cursor-not-allowed opacity-45"
            )}
          >
            {option.icon ? <span aria-hidden="true" className="inline-flex">{option.icon}</span> : null}
            <span>{option.label}</span>
            {option.count != null ? (
              <span className={cn("font-mono text-[11px] font-semibold", active ? "text-[var(--interaction-selected)]" : "text-muted-foreground")}>
                {option.count}
              </span>
            ) : null}
          </button>
        );
      })}
    </div>
  );
}
