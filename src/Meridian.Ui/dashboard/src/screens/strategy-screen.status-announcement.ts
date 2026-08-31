import type { PromotionRecord, RunComparisonRow, RunDiff } from "@/types";
import type { StrategyCommand } from "@/screens/strategy-screen.view-model";

export function buildStrategyStatusAnnouncement({
  activeCommand,
  actionError,
  comparison,
  runDiff,
  promotionHistory,
  comparisonLoaded = false,
  runDiffLoaded = false,
  promotionHistoryLoaded = false
}: {
  activeCommand: StrategyCommand | null;
  actionError: string | null;
  comparison: RunComparisonRow[];
  runDiff: RunDiff | null;
  promotionHistory: PromotionRecord[];
  comparisonLoaded?: boolean;
  runDiffLoaded?: boolean;
  promotionHistoryLoaded?: boolean;
}): string {
  if (activeCommand === "compare") {
    return "Comparing selected strategy runs.";
  }

  if (activeCommand === "diff") {
    return "Diffing selected strategy runs.";
  }

  if (activeCommand === "history") {
    return "Loading promotion history.";
  }

  if (actionError) {
    return `Strategy command failed: ${actionError}`;
  }

  if (runDiff) {
    return `Run diff ready for ${runDiff.baseStrategyName} and ${runDiff.targetStrategyName}.`;
  }

  if (runDiffLoaded) {
    return "No run diff returned for the selected pair.";
  }

  if (comparison.length > 0) {
    return `${comparison.length} comparison ${comparison.length === 1 ? "row" : "rows"} loaded.`;
  }

  if (comparisonLoaded) {
    return "No comparison rows returned for the selected pair.";
  }

  if (promotionHistory.length > 0) {
    return `${promotionHistory.length} promotion history ${promotionHistory.length === 1 ? "record" : "records"} loaded.`;
  }

  if (promotionHistoryLoaded) {
    return "No promotion history records returned.";
  }

  return "";
}
