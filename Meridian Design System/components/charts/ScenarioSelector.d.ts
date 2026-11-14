import * as React from "react";

export type ScenarioTone = "positive" | "negative" | "warning";
export interface ScenarioMetric {
  label: string;
  value: React.ReactNode;
  tone?: ScenarioTone;
}
export interface ScenarioOption {
  id: string;
  label: string;
  sub?: string;
  headline?: React.ReactNode;
  tone?: ScenarioTone;
  spark?: number[];
  metrics?: ScenarioMetric[];
}
export interface ScenarioSelectorProps {
  scenarios?: ScenarioOption[];
  value?: string | null;
  /** Omit for a noninteractive selected-scenario record. */
  onChange?: ((id: string) => void) | null;
  /** @default "rows" */
  layout?: "rows" | "columns";
  /** @default 104 */
  sparkWidth?: number;
  note?: React.ReactNode;
  /** Radio-group name. @default "Scenario" */
  ariaLabel?: string;
}
export declare function ScenarioSelector(props: ScenarioSelectorProps): React.ReactElement;
