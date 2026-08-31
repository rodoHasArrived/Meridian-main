import type {
  ReportingDetailField,
  ReportingDetailFieldTone,
  ReportingExportStatusClassName,
  ReportingExportStatusTone,
  ReportingFieldClassName
} from "./reporting-screen.view-model";

/**
 * Pure presenter primitives for the Reporting desk: detail fields, their tone classes, and the
 * byte formatting the export status shares with them. Extracted from the view model so the
 * behavior there can change without the file growing (build/config/file-size-baseline.json caps
 * it). The types stay owned by the view model and are imported type-only, so this pair carries no
 * runtime import cycle.
 */
export function buildReportingDetailField(
  label: string,
  value: string,
  tone: ReportingDetailFieldTone
): ReportingDetailField {
  return {
    label,
    value,
    tone,
    className: fieldToneClass(tone)
  };
}

function fieldToneClass(tone: ReportingDetailFieldTone): ReportingFieldClassName {
  if (tone === "success") return "text-success";
  if (tone === "warning") return "text-warning";
  if (tone === "muted") return "text-muted-foreground";
  return "text-foreground";
}

export function exportStatusToneClass(tone: ReportingExportStatusTone): ReportingExportStatusClassName {
  if (tone === "success") return "border-success/30 bg-success/10 text-success";
  if (tone === "danger") return "border-danger/35 bg-danger/10 text-danger";
  return "border-border/70 bg-secondary/25 text-muted-foreground";
}

export function formatBytes(value: number): string {
  if (!Number.isFinite(value) || value <= 0) {
    return "0 B";
  }

  const units = ["B", "KB", "MB", "GB"];
  let amount = value;
  let unitIndex = 0;

  while (amount >= 1024 && unitIndex < units.length - 1) {
    amount /= 1024;
    unitIndex += 1;
  }

  return `${amount.toLocaleString(undefined, { maximumFractionDigits: amount >= 10 ? 1 : 2 })} ${units[unitIndex]}`;
}
