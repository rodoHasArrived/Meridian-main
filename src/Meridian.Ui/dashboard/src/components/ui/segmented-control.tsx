import { type KeyboardEvent, type ReactNode, useRef } from "react";
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
 * hairline inset border, 2px corners.
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
  const buttons = useRef(new Map<string, HTMLButtonElement>());
  const enabled = items.filter((option) => !option.disabled);
  const tabStop = enabled.some((option) => option.value === value) ? value : enabled[0]?.value;
  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, current: string) => {
    if (enabled.length === 0) return;
    const index = enabled.findIndex((option) => option.value === current);
    let nextIndex: number;
    switch (event.key) {
      case "ArrowRight": case "ArrowDown": nextIndex = (index + 1) % enabled.length; break;
      case "ArrowLeft": case "ArrowUp": nextIndex = (index - 1 + enabled.length) % enabled.length; break;
      case "Home": nextIndex = 0; break;
      case "End": nextIndex = enabled.length - 1; break;
      default: return;
    }
    event.preventDefault();
    const next = enabled[nextIndex]!.value;
    buttons.current.get(next)?.focus();
    onChange?.(next);
  };

  return (
    <div
      role="tablist"
      aria-label={ariaLabel}
      aria-orientation="horizontal"
      className={cn(
        "inline-flex items-stretch gap-0.5 rounded-[2px] border border-border bg-[var(--ws-surface-raised)] p-0.5",
        fullWidth && "flex w-full",
        className
      )}
    >
      {items.map((option) => {
        const active = option.value === value;
        return (
          <button
            key={option.value}
            type="button"
            role="tab"
            aria-selected={active}
            disabled={option.disabled}
            tabIndex={option.value === tabStop ? 0 : -1}
            ref={(element) => {
              if (element) buttons.current.set(option.value, element);
              else buttons.current.delete(option.value);
            }}
            onKeyDown={(event) => handleKeyDown(event, option.value)}
            onClick={() => !option.disabled && onChange?.(option.value)}
            className={cn(
              "inline-flex min-h-6 flex-1 items-center justify-center gap-1.5 whitespace-nowrap rounded-[2px] font-medium leading-none transition-colors",
              "focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40",
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
              <span className={cn("font-mono text-[11px] font-semibold", active ? "text-primary" : "text-muted-foreground")}>
                {option.count}
              </span>
            ) : null}
          </button>
        );
      })}
    </div>
  );
}
