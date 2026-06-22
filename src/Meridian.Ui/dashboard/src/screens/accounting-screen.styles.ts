import { cn } from "@/lib/utils";

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

export function cashFlowBadgeClass(tone: "default" | "success" | "warning" | "danger") {
  if (tone === "success") return "border-success/35 bg-success/10 text-success";
  if (tone === "warning") return "border-warning/35 bg-warning/10 text-warning";
  if (tone === "danger") return "border-danger/35 bg-danger/10 text-danger";
  return "border-border/70 bg-secondary text-muted-foreground";
}

export function reportingBadgeClass(tone: "primary" | "success" | "warning" | "muted") {
  return cn(
    "rounded-sm border px-2 py-0.5 font-mono text-[10px] uppercase tracking-[0.12em]",
    tone === "primary" ? "border-primary/35 bg-primary/10 text-primary" : "",
    tone === "success" ? "border-success/35 bg-success/10 text-success" : "",
    tone === "warning" ? "border-warning/35 bg-warning/10 text-warning" : "",
    tone === "muted" ? "border-border/70 bg-secondary text-muted-foreground" : ""
  );
}
