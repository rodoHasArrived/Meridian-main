import type { PromotionOutcomeLevel } from "@/screens/trading-screen.view-model";
import type { TradingWorkspaceResponse } from "@/types";

/**
 * Presentation tone maps for the trading screen. Pure lookup tables with no behaviour, kept
 * out of the screen so its body stays the composition of panels rather than a palette.
 */
export const riskTone: Record<TradingWorkspaceResponse["risk"]["state"], string> = {
  Healthy: "text-success",
  Observe: "text-warning",
  Constrained: "text-danger"
};

export const wiringTone: Record<TradingWorkspaceResponse["brokerage"]["connection"], string> = {
  Connected: "text-success",
  Degraded: "text-warning",
  Disconnected: "text-danger"
};

export const promotionOutcomeTone: Record<PromotionOutcomeLevel, string> = {
  success: "text-success",
  warning: "text-warning",
  danger: "text-danger"
};

export const promotionEvaluationPanelTone = {
  success: "border-success/30 bg-success/10 text-success",
  warning: "border-warning/30 bg-warning/10 text-warning",
  danger: "border-danger/30 bg-danger/10 text-danger"
} as const;

export const promotionEvaluationTextTone = {
  success: "text-success",
  warning: "text-warning"
} as const;

