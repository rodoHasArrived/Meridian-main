// The operating-scope period: presets the scope picker offers and how a window maps back to one.
// Calendar days only, on the operator's own calendar (`LocalDate`), so a preset never shifts by a
// day for an operator off UTC. A preset writes `from`/`to`; "As of" writes `asOf`; "All periods"
// writes nothing. Choosing any of them replaces the whole window, including a `date` or `asOf` an
// originating workflow set.
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import { LocalDate } from "@/lib/local-date";

export type OperatingScopePeriodPresetId = "today" | "mtd" | "qtd" | "ytd" | "last-month";

export interface OperatingScopePeriodPreset {
  id: OperatingScopePeriodPresetId;
  label: string;
  from: string;
  to: string;
}

export type OperatingScopeWindow = Pick<AppShellOperatingScopeInput, "from" | "to" | "date" | "asOf">;

/** The window fields a period choice owns; all four are always written so none is left stale. */
export const EMPTY_OPERATING_SCOPE_WINDOW = {
  from: null,
  to: null,
  date: null,
  asOf: null
} as const satisfies OperatingScopeWindow;

/** Today, MTD, QTD, YTD and Last month as inclusive day ranges, for an injectable `today`. */
export function operatingScopePeriodPresets(today: Date = new Date()): OperatingScopePeriodPreset[] {
  const day = new Date(today.getFullYear(), today.getMonth(), today.getDate());
  const end = LocalDate.toISO(day);
  const quarterStartMonth = Math.floor(day.getMonth() / 3) * 3;
  const lastMonthStart = new Date(day.getFullYear(), day.getMonth() - 1, 1);
  const lastMonthEnd = new Date(day.getFullYear(), day.getMonth(), 0);
  return [
    { id: "today", label: "Today", from: end, to: end },
    { id: "mtd", label: "MTD", from: LocalDate.toISO(new Date(day.getFullYear(), day.getMonth(), 1)), to: end },
    { id: "qtd", label: "QTD", from: LocalDate.toISO(new Date(day.getFullYear(), quarterStartMonth, 1)), to: end },
    { id: "ytd", label: "YTD", from: LocalDate.toISO(new Date(day.getFullYear(), 0, 1)), to: end },
    { id: "last-month", label: "Last month", from: LocalDate.toISO(lastMonthStart), to: LocalDate.toISO(lastMonthEnd) }
  ];
}

/** The window a preset writes: its range, with any `date`/`asOf` cleared. */
export function windowForPeriodPreset(preset: OperatingScopePeriodPreset): OperatingScopeWindow {
  return { ...EMPTY_OPERATING_SCOPE_WINDOW, from: preset.from, to: preset.to };
}

export type OperatingScopePeriodMode = "all" | OperatingScopePeriodPresetId | "custom" | "as-of" | "workflow";

/**
 * Which period choice a window corresponds to, judged on the given `today`: a preset when its
 * exact range matches, "custom" for any other range, "as-of" for an as-of date alone, "workflow"
 * for a single `date` (set by an originating workflow), and "all" when nothing is set. A preset
 * applied on an earlier day reads as "custom", because its range no longer is that preset.
 */
export function operatingScopePeriodMode(window: OperatingScopeWindow, today: Date = new Date()): OperatingScopePeriodMode {
  const { from, to, date, asOf } = window;
  if (from || to) {
    if (!date && !asOf) {
      const preset = operatingScopePeriodPresets(today).find((candidate) => candidate.from === from && candidate.to === to);
      if (preset) return preset.id;
    }
    return "custom";
  }
  if (date) return "workflow";
  if (asOf) return "as-of";
  return "all";
}
