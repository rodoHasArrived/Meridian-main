export type SemanticTone = "default" | "success" | "warning" | "danger" | "muted";

export const semanticPanelToneClass: Record<SemanticTone, string> = {
  default: "border-border/70 bg-secondary/30",
  success: "border-success/30 bg-success/10",
  warning: "border-warning/35 bg-warning/10",
  danger: "border-danger/35 bg-danger/10",
  muted: "border-border/70 bg-secondary/25"
};

export const semanticTextToneClass: Record<SemanticTone, string> = {
  default: "text-foreground",
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger",
  muted: "text-muted-foreground"
};

export const semanticBorderToneClass: Record<Exclude<SemanticTone, "muted">, string> = {
  default: "border-border/70",
  success: "border-success/30",
  warning: "border-warning/30",
  danger: "border-danger/30"
};
