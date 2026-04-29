import type { MetricSnapshot } from "@/types";

export interface MetricCardViewModel {
  id: string;
  label: string;
  value: string;
  delta: string | null;
  toneClass: string;
  ariaLabel: string;
  deltaAriaLabel: string | null;
  labelId: string;
  valueId: string;
  deltaId: string | null;
}

const toneClass: Record<MetricSnapshot["tone"], string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

const toneLabel: Record<MetricSnapshot["tone"], string> = {
  default: "neutral",
  success: "positive",
  warning: "attention",
  danger: "critical"
};

export function buildMetricCardViewModel(metric: MetricSnapshot): MetricCardViewModel {
  const id = normalizeMetricId(metric.id || metric.label);
  const label = metric.label.trim();
  const value = metric.value.trim();
  const delta = metric.delta.trim() || null;
  const deltaAriaLabel = delta ? `Change ${normalizeDeltaForSpeech(delta)}` : null;
  const ariaLabel = [
    `${label} metric`,
    value,
    deltaAriaLabel,
    `Status ${toneLabel[metric.tone]}`
  ].filter(Boolean).join(". ");

  return {
    id,
    label,
    value,
    delta,
    toneClass: toneClass[metric.tone],
    ariaLabel,
    deltaAriaLabel,
    labelId: `${id}-label`,
    valueId: `${id}-value`,
    deltaId: delta ? `${id}-delta` : null
  };
}

function normalizeMetricId(value: string): string {
  const normalized = value
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");

  return normalized ? `metric-${normalized}` : "metric-card";
}

function normalizeDeltaForSpeech(value: string): string {
  return value
    .replace(/[▲△]/g, "up")
    .replace(/[▼▽]/g, "down")
    .replace(/−/g, "-")
    .replace(/\s+/g, " ")
    .trim();
}
