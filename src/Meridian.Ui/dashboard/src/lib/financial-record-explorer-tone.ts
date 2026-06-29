import type { FinancialRecordExplorerTone } from "@/types";

export function financialRecordToneToBadge(tone?: FinancialRecordExplorerTone): "default" | "outline" | "success" | "warning" | "danger" {
  switch (tone) {
    case "Success":
      return "success";
    case "Warning":
      return "warning";
    case "Danger":
      return "danger";
    case "Info":
      return "default";
    default:
      return "outline";
  }
}

export function financialRecordToneTextClass(tone?: FinancialRecordExplorerTone): string {
  switch (tone) {
    case "Success":
      return "text-success";
    case "Warning":
      return "text-warning";
    case "Danger":
      return "text-danger";
    default:
      return "text-foreground";
  }
}
