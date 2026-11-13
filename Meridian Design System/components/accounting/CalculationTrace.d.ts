import * as React from "react";
export type CalculationTraceLevel = "summary" | "expanded" | "audit";
export interface CalculationStep {
  id?: string;
  /** Arithmetic operator shown in the gutter: "×", "−", "=", "". */
  operator?: string;
  label: React.ReactNode;
  value: React.ReactNode;
  unit?: string;
  /** Human source name, shown from the expanded level up. */
  source?: string;
  /** Source record identifier, shown only at the audit level. */
  sourceId?: string;
  asOf?: string;
  /** One of the eleven data conditions; renders as glyph + notation suffix. */
  condition?: string;
  /** "input" (default) or "intermediate" — intermediates get their own plate. */
  kind?: "input" | "intermediate";
  /** Free-text note shown when there is no `source`. */
  detail?: string;
  changedBy?: string;
  changedAt?: string;
  /** Keep this row at the summary level. */
  summary?: boolean;
  /** Set false to drop the row from expanded/audit as immaterial. */
  material?: boolean;
}
export interface CalculationTraceOverride { label?: string; value: React.ReactNode; by?: string }
export interface CalculationTraceProps extends Omit<React.HTMLAttributes<HTMLElement>, "title"> {
  title: React.ReactNode;
  /** Domain-readable formula — "Opening balance + activity − settlements = closing balance". */
  formula?: React.ReactNode;
  /** Implementation syntax; audit level only. */
  technicalFormula?: React.ReactNode;
  steps: CalculationStep[];
  result: { label?: React.ReactNode; value: React.ReactNode; unit?: string };
  rounding?: string;
  basis?: string;
  /** Legacy single override; prefer `overrides`. */
  override?: string;
  overrides?: CalculationTraceOverride[];
  condition?: string;
  asOf?: string;
  /** Controlled level. Omit for uncontrolled with `defaultLevel`. */
  level?: CalculationTraceLevel;
  defaultLevel?: CalculationTraceLevel;
  onLevelChange?: (level: CalculationTraceLevel) => void;
  /** Restrict the level switcher, e.g. ["summary","expanded"] where audit is not permitted. */
  availableLevels?: CalculationTraceLevel[];
  calculationVersion?: string;
  calculatedAt?: string;
  executedBy?: string;
  engine?: string;
  /** Why a regenerated value could differ — shown at the audit level. */
  reproducibility?: React.ReactNode;
}
export declare function CalculationTrace(props: CalculationTraceProps): React.JSX.Element;
