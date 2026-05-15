import type { RiskRuleStatus } from "@/types";

export type RiskRuleTone = "success" | "warning" | "danger";

export interface RiskRuleRowViewModel {
  ruleName: string;
  state: RiskRuleStatus["state"];
  summary: string;
  threshold: string;
  currentValue: string;
  tone: RiskRuleTone;
  violationCount: number;
}

export interface RuleViolationTimelineItem {
  id: string;
  ruleName: string;
  message: string;
}

export interface RiskControlPanelViewModel {
  overallState: RiskRuleStatus["state"];
  overallSummary: string;
  rows: RiskRuleRowViewModel[];
  violationTimeline: RuleViolationTimelineItem[];
}

export function buildRiskControlPanelViewModel(statuses: RiskRuleStatus[]): RiskControlPanelViewModel {
  if (statuses.length === 0) {
    return {
      overallState: "Observe",
      overallSummary: "Risk runtime status is unavailable.",
      rows: [],
      violationTimeline: []
    };
  }

  const constrained = statuses.find((status) => status.state === "Constrained");
  const observed = statuses.find((status) => status.state === "Observe");
  const selected = constrained ?? observed ?? statuses[0];

  const rows = statuses.map((status) => ({
    ruleName: status.ruleName,
    state: status.state,
    summary: status.summary,
    threshold: status.threshold,
    currentValue: status.currentValue,
    tone: mapRuleTone(status.state),
    violationCount: status.recentViolations.length
  }));

  const violationTimeline = statuses.flatMap((status) =>
    status.recentViolations.map((message, index) => ({
      id: `${status.ruleName}-${index}`,
      ruleName: status.ruleName,
      message
    })));

  return {
    overallState: selected.state,
    overallSummary: selected.summary,
    rows,
    violationTimeline
  };
}

function mapRuleTone(state: RiskRuleStatus["state"]): RiskRuleTone {
  if (state === "Constrained") {
    return "danger";
  }

  if (state === "Observe") {
    return "warning";
  }

  return "success";
}
