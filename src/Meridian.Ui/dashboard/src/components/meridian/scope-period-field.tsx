import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { DatePicker } from "@/components/ui/date-picker";
import { DateRangePicker } from "@/components/ui/date-range-picker";
import {
  EMPTY_OPERATING_SCOPE_WINDOW,
  operatingScopePeriodMode,
  operatingScopePeriodPresets,
  windowForPeriodPreset,
  type OperatingScopePeriodMode,
  type OperatingScopeWindow
} from "@/lib/operating-scope/period";

/** One-line reading of a window for the picker: every value it carries, never "None". */
export function describeOperatingScopeWindow(window: OperatingScopeWindow): string {
  const parts: string[] = [];
  if (window.from && window.from === window.to) parts.push(window.from);
  else if (window.from || window.to) parts.push(`${window.from ?? "Start"} to ${window.to ?? "Now"}`);
  if (window.date) parts.push(`date ${window.date}`);
  if (window.asOf) parts.push(`as of ${window.asOf}`);
  return parts.length > 0 ? parts.join(" · ") : "All periods";
}

/**
 * The scope picker's period control: All periods, the desk presets, a custom range on the shared
 * date-range picker, or a single as-of date. Every choice replaces the whole window (`from`, `to`,
 * `date`, `asOf`), so a stale workflow date never rides along with a new range.
 */
export function ScopePeriodField({
  value,
  onChange,
  applicability,
  now
}: {
  value: OperatingScopeWindow;
  onChange: (next: OperatingScopeWindow) => void;
  applicability: string;
  /** The clock presets are computed on; injectable for tests. */
  now?: Date;
}) {
  const id = useId();
  const today = now ?? new Date();
  const presets = operatingScopePeriodPresets(today);
  const derived = operatingScopePeriodMode(value, today);
  // "Custom range" and "As of" stay open while the operator is still choosing dates.
  const [editing, setEditing] = useState<"custom" | "as-of" | null>(null);
  const mode: OperatingScopePeriodMode = editing ?? derived;
  const choose = (next: OperatingScopePeriodMode) => {
    if (next === "custom" || next === "as-of") {
      setEditing(next);
      if (next === "as-of") {
        onChange({ ...EMPTY_OPERATING_SCOPE_WINDOW, asOf: value.asOf ?? null });
      } else {
        onChange({ ...EMPTY_OPERATING_SCOPE_WINDOW, from: value.from ?? null, to: value.to ?? null });
      }
      return;
    }
    setEditing(null);
    const preset = presets.find((candidate) => candidate.id === next);
    onChange(preset ? windowForPeriodPreset(preset) : { ...EMPTY_OPERATING_SCOPE_WINDOW });
  };
  const choices: { id: OperatingScopePeriodMode; label: string }[] = [
    { id: "all", label: "All periods" },
    ...presets.map((preset) => ({ id: preset.id, label: preset.label })),
    { id: "custom", label: "Custom range" },
    { id: "as-of", label: "As of" }
  ];

  return (
    <fieldset aria-describedby={`${id}-current ${id}-help`} className="space-y-1.5">
      <legend className="text-sm font-medium text-foreground">Period</legend>
      <div className="flex flex-wrap gap-1.5" role="group" aria-label="Period choices">
        {choices.map((choice) => (
          <Button
            key={choice.id}
            type="button"
            size="sm"
            variant={mode === choice.id ? "default" : "outline"}
            aria-pressed={mode === choice.id}
            onClick={() => choose(choice.id)}
          >
            {choice.label}
          </Button>
        ))}
      </div>
      {mode === "custom" ? (
        <DateRangePicker
          label="Custom range"
          start={value.from ?? null}
          end={value.to ?? null}
          onChange={(range) => onChange({ ...EMPTY_OPERATING_SCOPE_WINDOW, from: range.start, to: range.end })}
        />
      ) : null}
      {mode === "as-of" ? (
        <DatePicker
          label="As of date"
          value={value.asOf ?? null}
          onChange={(asOf) => onChange({ ...EMPTY_OPERATING_SCOPE_WINDOW, asOf })}
        />
      ) : null}
      <p id={`${id}-current`} className="text-xs text-foreground">
        Period: <span className="font-mono">{describeOperatingScopeWindow(value)}</span>
        {mode === "workflow" ? " (set by the originating workflow)" : null}
      </p>
      <p id={`${id}-help`} className="text-xs text-muted-foreground">{applicability}. Presets use your calendar day.</p>
    </fieldset>
  );
}
