import { type ReactNode, useRef } from "react";
import { cn } from "@/lib/utils";

export interface SegmentedOption {
  value: string;
  label: ReactNode;
  icon?: ReactNode;
  count?: ReactNode;
  disabled?: boolean;
  /** Link a tab to its existing panel; omit for value controls. */
  panelId?: string;
  id?: string;
}

export interface SegmentedControlProps {
  options: Array<SegmentedOption | string>;
  value?: string;
  onChange?: (value: string) => void;
  size?: "sm" | "md";
  fullWidth?: boolean;
  className?: string;
  "aria-label"?: string;
  /** Choose radio semantics for values such as time resolution. Existing view tabs remain tabs. */
  semantics?: "tabs" | "radio";
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
  semantics = "tabs",
  "aria-label": ariaLabel
}: SegmentedControlProps) {
  const items = normalize(options);
  const buttons = useRef<Array<HTMLButtonElement | null>>([]);
  const enabled = items.map((item, index) => item.disabled ? -1 : index).filter((index) => index >= 0);
  const selectedIndex = items.findIndex((item) => item.value === value && !item.disabled);
  const tabStop = selectedIndex >= 0 ? selectedIndex : enabled[0];

  return (
    <div
      role={semantics === "tabs" ? "tablist" : "radiogroup"}
      aria-label={ariaLabel}
      className={cn(
        "inline-flex items-stretch gap-0.5 rounded-[2px] border border-border bg-[var(--ws-surface-raised)] p-0.5",
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
            id={option.id}
            role={semantics === "tabs" ? "tab" : "radio"}
            aria-selected={semantics === "tabs" ? active : undefined}
            aria-checked={semantics === "radio" ? active : undefined}
            aria-controls={semantics === "tabs" ? option.panelId : undefined}
            tabIndex={index === tabStop ? 0 : -1}
            ref={(element) => { buttons.current[index] = element; }}
            disabled={option.disabled}
            onClick={() => !option.disabled && onChange?.(option.value)}
            onKeyDown={(event) => {
              const position = enabled.indexOf(index);
              let next: number | undefined;
              if (event.key === "ArrowRight" || event.key === "ArrowDown") next = enabled[(position + 1) % enabled.length];
              else if (event.key === "ArrowLeft" || event.key === "ArrowUp") next = enabled[(position + enabled.length - 1) % enabled.length];
              else if (event.key === "Home") next = enabled[0];
              else if (event.key === "End") next = enabled[enabled.length - 1];
              else return;
              event.preventDefault();
              if (next === undefined) return;
              buttons.current[next]?.focus();
              onChange?.(items[next].value);
            }}
            className={cn(
              "inline-flex flex-1 items-center justify-center gap-1.5 whitespace-nowrap rounded-[2px] font-medium leading-none transition-colors",
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
