import type { MetricCardTone } from "@/components/data/concrete";

export type SharedBadgeVariant = "default" | "outline" | "success" | "warning" | "danger";
export type SharedPanelTone = "success" | "warning" | "danger" | "muted" | "default" | "neutral" | "info" | "ready" | "review" | "blocked" | "pending" | "action" | string;

export function badgeVariantToSeverityStatus(variant: SharedBadgeVariant): string {
  switch (variant) {
    case "success":
      return "Ready";
    case "warning":
      return "ReviewRequired";
    case "danger":
      return "Blocked";
    case "default":
      return "Info";
    case "outline":
    default:
      return "Info";
  }
}

export function categoricalVariantToSeverityStatus(variant: string): string {
  const key = normalizeToneKey(variant);
  if (key === "paper" || key === "research") {
    return "review";
  }

  switch (key) {
    case "success":
      return "ready";
    case "warning":
      return "action";
    case "danger":
      return "blocked";
    default:
      return "info";
  }
}

export function badgeVariantToOperatorSeverity(variant: string): "ready" | "review" | "action" | "blocked" | "info" {
  switch (normalizeToneKey(variant)) {
    case "success":
      return "ready";
    case "warning":
      return "action";
    case "danger":
      return "blocked";
    case "paper":
    case "research":
      return "review";
    default:
      return "info";
  }
}

export function readinessToneToSeverityStatus(tone: SharedPanelTone): string {
  switch (normalizeToneKey(tone)) {
    case "ready":
    case "success":
    case "trusted":
      return "Ready";
    case "blocked":
    case "danger":
    case "critical":
    case "failed":
    case "error":
      return "Blocked";
    case "review":
    case "warning":
    case "action":
    case "attention":
      return "ReviewRequired";
    case "pending":
      return "Pending";
    default:
      return "Info";
  }
}

export function readinessToneToBadgeVariant(tone: SharedPanelTone): SharedBadgeVariant {
  switch (normalizeToneKey(tone)) {
    case "ready":
    case "success":
    case "trusted":
      return "success";
    case "blocked":
    case "danger":
    case "critical":
    case "failed":
    case "error":
      return "danger";
    case "review":
    case "warning":
    case "pending":
    case "action":
    case "attention":
      return "warning";
    case "default":
      return "default";
    default:
      return "outline";
  }
}

export function badgeVariantToMetricTone(variant: SharedBadgeVariant): MetricCardTone {
  switch (variant) {
    case "success":
      return "success";
    case "warning":
      return "warning";
    case "danger":
      return "danger";
    case "default":
      return "info";
    case "outline":
    default:
      return "neutral";
  }
}

export function semanticToneToMetricCardTone(tone: SharedPanelTone): MetricCardTone {
  switch (normalizeToneKey(tone)) {
    case "ready":
    case "success":
    case "trusted":
      return "success";
    case "review":
    case "warning":
    case "pending":
    case "action":
    case "attention":
      return "warning";
    case "blocked":
    case "danger":
    case "critical":
    case "failed":
    case "error":
      return "danger";
    case "default":
      return "neutral";
    case "info":
    case "primary":
      return "info";
    default:
      return "neutral";
  }
}

export function readinessToneToPanelClass(tone: SharedPanelTone): string {
  switch (normalizeToneKey(tone)) {
    case "ready":
    case "success":
    case "trusted":
      return "border-success/35 bg-success/10";
    case "review":
    case "warning":
    case "pending":
    case "action":
    case "attention":
      return "border-warning/35 bg-warning/10";
    case "blocked":
    case "danger":
    case "critical":
    case "failed":
    case "error":
      return "border-danger/35 bg-danger/10";
    case "primary":
    case "default":
      return "border-primary/30 bg-primary/10";
    default:
      return "border-border/70 bg-secondary/20";
  }
}

export function readinessToneToSeverityPanelClass(tone: SharedPanelTone): string {
  switch (normalizeToneKey(tone)) {
    case "ready":
    case "success":
    case "trusted":
      return "border-[var(--severity-ready-bd)] bg-[var(--severity-ready-bg)]";
    case "review":
    case "warning":
    case "pending":
    case "action":
    case "attention":
      return "border-[var(--severity-review-bd)] bg-[var(--severity-review-bg)]";
    case "blocked":
    case "danger":
    case "critical":
    case "failed":
    case "error":
      return "border-[var(--severity-blocked-bd)] bg-[var(--severity-blocked-bg)]";
    default:
      return "border-[var(--severity-info-bd)] bg-[var(--severity-info-bg)]";
  }
}

export function semanticToneToTextClass(tone: SharedPanelTone): string {
  switch (normalizeToneKey(tone)) {
    case "ready":
    case "success":
    case "trusted":
      return "text-success";
    case "review":
    case "warning":
    case "pending":
    case "action":
    case "attention":
      return "text-warning";
    case "blocked":
    case "danger":
    case "critical":
    case "failed":
    case "error":
      return "text-danger";
    case "primary":
    case "default":
      return "text-primary";
    default:
      return "text-muted-foreground";
  }
}

export function evidenceStatusToneToTextClass(tone: SharedPanelTone): string {
  return normalizeToneKey(tone) === "muted" ? "text-foreground" : semanticToneToTextClass(tone);
}

export function semanticToneToDotClass(tone: SharedPanelTone): string {
  switch (normalizeToneKey(tone)) {
    case "ready":
    case "success":
    case "trusted":
      return "bg-success";
    case "review":
    case "warning":
    case "pending":
    case "action":
    case "attention":
      return "bg-warning";
    case "blocked":
    case "danger":
    case "critical":
    case "failed":
    case "error":
      return "bg-danger";
    case "primary":
    case "default":
      return "bg-primary";
    default:
      return "bg-muted-foreground/50";
  }
}

function normalizeToneKey(tone: SharedPanelTone): string {
  return String(tone).trim().toLowerCase().replace(/[^a-z]/g, "");
}
