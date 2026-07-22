import type { BackfillResultCardState } from "@/screens/data-screen.view-model";

/** Tonal border/background/text classes shared by the backfill result card and quality scorecards. */
export const resultToneClass: Record<BackfillResultCardState["tone"], string> = {
  warning: "border-warning/35 bg-warning/10 text-warning",
  success: "border-success/35 bg-success/10 text-success",
  danger: "border-danger/35 bg-danger/10 text-danger"
};

/** Maps a data-quality tone onto the corresponding Badge variant. */
export const qualityToneBadgeVariant = {
  success: "success",
  warning: "warning",
  danger: "danger"
} as const;
