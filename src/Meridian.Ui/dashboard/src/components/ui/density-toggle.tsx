import { type RefObject, useEffect, useState } from "react";
import { cn } from "@/lib/utils";

export type Density = "compact" | "default" | "spacious";

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

function applyDensity(el: HTMLElement | null, value: Density) {
  if (!el) {
    return;
  }
  if (value === "default") {
    el.removeAttribute("data-theme-density");
  } else {
    el.setAttribute("data-theme-density", value);
  }
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
 * Operator control for the density token scopes (`data-theme-density="compact"|"spacious"`).
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
  const isControlled = controlled != null;
  const [internal, setInternal] = useState<Density>(() => {
    if (isControlled) {
      return controlled;
    }
    if (persist && typeof localStorage !== "undefined") {
      const saved = localStorage.getItem(persist);
      if (saved === "compact" || saved === "default" || saved === "spacious") {
        return saved;
      }
    }
    return defaultValue;
  });
  const value = isControlled ? controlled : internal;

  useEffect(() => {
    if (!apply) {
      return;
    }
    applyDensity(resolveTarget(target), value);
  }, [value, apply, target]);

  const select = (next: Density) => {
    if (!isControlled) {
      setInternal(next);
    }
    if (persist && typeof localStorage !== "undefined") {
      localStorage.setItem(persist, next);
    }
    onChange?.(next);
  };

  return (
    <div
      role="radiogroup"
      aria-label="Display density"
      className={cn(
        "inline-flex items-stretch gap-0.5 rounded-[2px] border border-border bg-[#F3F6F9] p-0.5",
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
            title={level.label}
            onClick={() => select(level.value)}
            className={cn(
              "inline-flex flex-1 items-center justify-center gap-1.5 rounded-[2px] font-medium leading-none transition-colors",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
              size === "sm" ? "px-2.5 py-1 text-[11px]" : "px-3 py-1.5 text-xs",
              active
                ? "bg-card font-semibold text-foreground shadow-[inset_0_0_0_1px_hsl(var(--border))]"
                : "bg-transparent text-muted-foreground hover:bg-[#EAEEF3] hover:text-foreground"
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
