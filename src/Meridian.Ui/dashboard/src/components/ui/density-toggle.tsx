import { type KeyboardEvent, type RefObject, useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";
import { applyDensity as applyDensityToElement, isDensity, type Density } from "@/lib/density";

export type { Density };

export interface DensityToggleProps {
  value?: Density;
  defaultValue?: Density;
  onChange?: (value: Density) => void;
  target?: "body" | "root" | "html" | string | RefObject<HTMLElement> | HTMLElement | null;
  apply?: boolean;
  persist?: string | null;
  showLabels?: boolean;
  size?: "sm" | "md";
  fullWidth?: boolean;
  className?: string;
}

const LEVELS: Array<{ value: Density; label: string; gap: number }> = [
  { value: "terminal", label: "Terminal", gap: 0.5 },
  { value: "compact", label: "Compact", gap: 1 },
  { value: "default", label: "Default", gap: 2 },
  { value: "spacious", label: "Spacious", gap: 4 }
];

function resolveTarget(target: DensityToggleProps["target"]): HTMLElement | null {
  if (typeof document === "undefined") {
    return null;
  }
  if (!target || target === "body") {
    return document.body;
  }
  if (target === "root" || target === "html") {
    return document.documentElement;
  }
  if (typeof target === "string") {
    return document.querySelector<HTMLElement>(target);
  }
  if (typeof target === "object" && "current" in target) {
    return target.current;
  }
  return target;
}

function Glyph({ gap }: { gap: number }) {
  return (
    <span aria-hidden="true" className="inline-flex w-[11px] flex-col" style={{ gap }}>
      <i className="block h-[1.5px] rounded-[1px] bg-current" />
      <i className="block h-[1.5px] rounded-[1px] bg-current" />
      <i className="block h-[1.5px] rounded-[1px] bg-current" />
    </span>
  );
}

/**
 * Operator control for the terminal, compact, default, and spacious density scopes.
 * Writes the attribute to a target element (`document.body` by default) and can `persist` the
 * choice. Concrete segmented look — flat, hairline, 2px corners.
 */
export function DensityToggle({
  value: controlled,
  defaultValue = "default",
  onChange,
  target = "body",
  apply = true,
  persist = null,
  showLabels = true,
  size = "md",
  fullWidth = false,
  className
}: DensityToggleProps) {
  const buttons = useRef(new Map<Density, HTMLButtonElement>());
  const isControlled = controlled != null;
  const [internal, setInternal] = useState<Density>(() => {
    if (isControlled) {
      return controlled;
    }
    if (persist) {
      try {
        const saved = localStorage.getItem(persist);
        if (isDensity(saved)) return saved;
      } catch { /* Storage is optional; keep the usable default. */ }
    }
    return defaultValue;
  });
  const value = isControlled ? controlled : internal;

  useEffect(() => {
    if (!apply) {
      return;
    }
    applyDensityToElement(value, resolveTarget(target));
  }, [value, apply, target]);

  const select = (next: Density) => {
    if (!isControlled) {
      setInternal(next);
    }
    if (persist) {
      try { localStorage.setItem(persist, next); }
      catch { /* The choice still applies when persistence is unavailable. */ }
    }
    onChange?.(next);
  };

  const handleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, current: Density) => {
    const index = LEVELS.findIndex((level) => level.value === current);
    let nextIndex: number;
    switch (event.key) {
      case "ArrowRight": case "ArrowDown": nextIndex = (index + 1) % LEVELS.length; break;
      case "ArrowLeft": case "ArrowUp": nextIndex = (index - 1 + LEVELS.length) % LEVELS.length; break;
      case "Home": nextIndex = 0; break;
      case "End": nextIndex = LEVELS.length - 1; break;
      default: return;
    }
    event.preventDefault();
    const next = LEVELS[nextIndex]!.value;
    buttons.current.get(next)?.focus();
    select(next);
  };

  return (
    <div
      role="radiogroup"
      aria-label="Display density"
      aria-orientation="horizontal"
      className={cn(
        "inline-flex items-stretch gap-0.5 rounded-[2px] border border-border bg-card p-0.5",
        fullWidth && "flex w-full",
        className
      )}
    >
      {LEVELS.map((level) => {
        const active = level.value === value;
        return (
          <button
            key={level.value}
            type="button"
            role="radio"
            aria-checked={active}
            tabIndex={active ? 0 : -1}
            ref={(element) => {
              if (element) buttons.current.set(level.value, element);
              else buttons.current.delete(level.value);
            }}
            onKeyDown={(event) => handleKeyDown(event, level.value)}
            title={level.label}
            onClick={() => select(level.value)}
            className={cn(
              "inline-flex min-h-6 flex-1 items-center justify-center gap-1.5 rounded-[2px] font-medium leading-none transition-colors",
              "focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40",
              size === "sm" ? "px-2.5 py-1 text-[11px]" : "px-3 py-1.5 text-xs",
              active
                ? "bg-background font-semibold text-foreground shadow-[inset_0_0_0_1px_hsl(var(--border))]"
                : "bg-transparent text-muted-foreground hover:bg-secondary hover:text-foreground"
            )}
          >
            <Glyph gap={level.gap} />
            {showLabels ? <span>{level.label}</span> : null}
          </button>
        );
      })}
    </div>
  );
}
