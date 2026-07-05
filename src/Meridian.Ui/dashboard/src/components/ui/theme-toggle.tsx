import { Monitor, Moon, Sun } from "lucide-react";
import { useEffect, useState } from "react";
import { applyAppearance, APPEARANCE_STORAGE_KEY, readStoredAppearance, writeStoredAppearance, type Appearance } from "@/lib/theme";
import { broadcastCompanionState } from "@/lib/companion-pane/opener-broadcast";
import { cn } from "@/lib/utils";

export interface ThemeToggleProps {
  value?: Appearance;
  defaultValue?: Appearance;
  onChange?: (value: Appearance) => void;
  apply?: boolean;
  persist?: string | null;
  showLabels?: boolean;
  size?: "sm" | "md";
  fullWidth?: boolean;
  className?: string;
}

const OPTIONS: Array<{
  value: Appearance;
  label: string;
  Icon: typeof Monitor;
}> = [
  { value: "system", label: "System", Icon: Monitor },
  { value: "light", label: "Light", Icon: Sun },
  { value: "dark", label: "Dark", Icon: Moon }
];

export function ThemeToggle({
  value: controlled,
  defaultValue = "system",
  onChange,
  apply = true,
  persist = APPEARANCE_STORAGE_KEY,
  showLabels = true,
  size = "md",
  fullWidth = false,
  className
}: ThemeToggleProps) {
  const isControlled = controlled != null;
  const [internal, setInternal] = useState<Appearance>(() => {
    if (isControlled) {
      return controlled;
    }
    if (persist === APPEARANCE_STORAGE_KEY) {
      return readStoredAppearance();
    }
    if (persist && typeof localStorage !== "undefined") {
      const saved = localStorage.getItem(persist);
      if (saved === "system" || saved === "light" || saved === "dark") {
        return saved;
      }
    }
    return defaultValue;
  });
  const value = isControlled ? controlled : internal;

  useEffect(() => {
    if (apply) {
      applyAppearance(value);
    }
  }, [apply, value]);

  const select = (next: Appearance) => {
    if (!isControlled) {
      setInternal(next);
    }
    if (persist === APPEARANCE_STORAGE_KEY) {
      writeStoredAppearance(next);
      // Push the appearance change to any open companion panes so they retheme live.
      broadcastCompanionState({ type: "appearance", appearance: next });
    } else if (persist && typeof localStorage !== "undefined") {
      localStorage.setItem(persist, next);
    }
    onChange?.(next);
  };

  return (
    <div
      role="radiogroup"
      aria-label="Appearance"
      className={cn(
        "inline-flex items-stretch gap-0.5 rounded-[2px] border border-border bg-card p-0.5",
        fullWidth && "flex w-full",
        className
      )}
    >
      {OPTIONS.map(({ value: optionValue, label, Icon }) => {
        const active = optionValue === value;
        return (
          <button
            key={optionValue}
            type="button"
            role="radio"
            aria-checked={active}
            title={label}
            onClick={() => select(optionValue)}
            className={cn(
              "inline-flex flex-1 items-center justify-center gap-1.5 rounded-[2px] font-medium leading-none transition-colors",
              "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
              size === "sm" ? "px-2.5 py-1 text-[11px]" : "px-3 py-1.5 text-xs",
              active
                ? "bg-background font-semibold text-foreground shadow-[inset_0_0_0_1px_hsl(var(--border))]"
                : "bg-transparent text-muted-foreground hover:bg-secondary hover:text-foreground"
            )}
          >
            <Icon className="h-3.5 w-3.5" aria-hidden="true" />
            {showLabels ? <span>{label}</span> : null}
          </button>
        );
      })}
    </div>
  );
}
