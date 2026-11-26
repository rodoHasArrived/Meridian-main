// Small presentational helpers shared by the Settings screen and its section modules.
import { diagnosticToneClass, itemToneClass } from "./settings-provider-integration";
import { kpiAttributes, type ResolvedKpi } from "@/lib/kpi-registry";

export function SettingsChip({ label, value, kpi }: { label: string } & (
  | { value: string; kpi?: never }
  | { kpi: ResolvedKpi; value?: never }
)) {
  const displayValue = kpi?.value ?? value;
  return (
    <div className="toolbar-chip" aria-label={`${label} ${displayValue}`} {...(kpi ? kpiAttributes(kpi) : {})}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono text-foreground" data-kpi-rendered-value={kpi ? "" : undefined}>{displayValue}</span>
    </div>
  );
}

export function settingsBannerTone(tone: keyof typeof diagnosticToneClass | keyof typeof itemToneClass): "success" | "warning" | "danger" | "info" {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "info";
}

export function capabilityTone(tone: "default" | "success" | "warning" | "danger" | "outline"): keyof typeof diagnosticToneClass {
  if (tone === "success") return "success";
  if (tone === "warning") return "warning";
  if (tone === "danger") return "danger";
  return "default";
}

export const setupStepToneClass = {
  success: "border-success/30 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10",
  muted: "border-border/70 bg-secondary/25"
} as const;
