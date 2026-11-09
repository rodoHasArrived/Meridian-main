type AccountingToolingToneClass = "default" | "success" | "warning" | "danger";
export function accountingToolingBadgeVariant(tone: AccountingToolingToneClass): "default" | "outline" | "success" | "warning" | "danger" {
  if (tone === "success" || tone === "warning" || tone === "danger") {
    return tone;
  }

  return "outline";
}

export function accountingToolingBorderClass(tone: AccountingToolingToneClass): string {
  if (tone === "success") {
    return "border-success/30 bg-success/10";
  }

  if (tone === "warning") {
    return "border-warning/35 bg-warning/10";
  }

  if (tone === "danger") {
    return "border-danger/35 bg-danger/10";
  }

  return "border-border/70 bg-secondary/20";
}

export function cashFlowTextClass(tone: "default" | "success" | "warning" | "danger") {
  if (tone === "success") return "text-success";
  if (tone === "warning") return "text-warning";
  if (tone === "danger") return "text-danger";
  return "";
}
