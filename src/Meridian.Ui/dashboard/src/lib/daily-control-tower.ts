import type {
  AppShellDecisionBrief,
  AppShellEvidenceTimelineItem,
  AppShellLinkedContextItem,
  AppShellOperatorFocusItem,
  AppShellWorkflowContinuityStatusTone,
  AppShellWorkflowContinuityViewModel
} from "@/lib/app-shell-workflow-continuity";

export type DailyControlTowerBadgeVariant = "outline" | "success" | "warning" | "danger";

export interface DailyControlTowerFocusRow {
  item: AppShellOperatorFocusItem;
  statusLabel: string;
  outputLabel: string;
  badgeVariant: DailyControlTowerBadgeVariant;
  proof: AppShellEvidenceTimelineItem | null;
  proofPassportStatusLabel: string;
  proofPassportSummary: string;
  proofPassportItems: DailyControlTowerProofPassportItem[];
}

export type DailyControlTowerDecisionFactId = "status" | "owner" | "output" | "next-action" | "proof";

export type DailyControlTowerProofPassportItemId =
  | "source"
  | "freshness"
  | "reconciliation"
  | "approvals"
  | "report-usage"
  | "blockers"
  | "evidence-packet"
  | "audit-trail";

export interface DailyControlTowerProofPassportItem {
  id: DailyControlTowerProofPassportItemId;
  label: string;
  value: string;
  detail: string;
  href: string | null;
  ariaLabel: string | null;
  badgeVariant: DailyControlTowerBadgeVariant;
}

export interface DailyControlTowerDecisionFact {
  id: DailyControlTowerDecisionFactId;
  label: string;
  value: string;
  detail: string;
  href: string | null;
  ariaLabel: string | null;
  badgeVariant: DailyControlTowerBadgeVariant | null;
}

export interface DailyControlTowerModel {
  decision: AppShellDecisionBrief;
  decisionBadgeVariant: DailyControlTowerBadgeVariant;
  decisionFacts: DailyControlTowerDecisionFact[];
  focusSummary: string;
  focusEmptyText: string;
  focusOverflowLabel: string | null;
  focusRows: DailyControlTowerFocusRow[];
  linkedContextLabel: string;
  linkedContextSummary: string;
  linkedContextPostureLabel: string;
  linkedContextBadgeVariant: DailyControlTowerBadgeVariant;
  linkedContextEmptyText: string;
  linkedContextItems: AppShellLinkedContextItem[];
  evidenceTimelineLabel: string;
  evidenceTimelineSummary: string;
  evidenceTimelineEmptyText: string;
  evidenceTimelineOverflowLabel: string | null;
  evidenceTimelineItems: AppShellEvidenceTimelineItem[];
}

export function buildDailyControlTowerModel(viewModel: AppShellWorkflowContinuityViewModel): DailyControlTowerModel {
  const focusRows = viewModel.operatorFocusItems.map((item) => {
    const statusLabel = dailyControlTowerStatusLabel(item.tone);
    const outputLabel = dailyControlTowerOutputLabel(item);
    const badgeVariant = dailyControlTowerBadgeVariant(item.tone);
    const proof = findDailyControlTowerProof(item, viewModel.evidenceTimelineItems);
    const proofPassportItems = buildDailyControlTowerProofPassportItems({
      item,
      statusLabel,
      outputLabel,
      badgeVariant,
      proof
    });
    return {
      item,
      statusLabel,
      outputLabel,
      badgeVariant,
      proof,
      proofPassportStatusLabel: proof ? `${statusLabel} proof` : statusLabel,
      proofPassportSummary: `Proof Passport for ${outputLabel}: ${proofPassportItems.map((passportItem) => `${passportItem.label} ${passportItem.value}`).join("; ")}.`,
      proofPassportItems
    };
  });
  const decisionBadgeVariant = dailyControlTowerBadgeVariant(viewModel.decisionBrief.statusTone);

  return {
    decision: viewModel.decisionBrief,
    decisionBadgeVariant,
    decisionFacts: buildDailyControlTowerDecisionFacts(
      viewModel.decisionBrief,
      decisionBadgeVariant,
      focusRows[0] ?? null,
      viewModel.evidenceTimelineItems[0] ?? null
    ),
    focusSummary: viewModel.operatorFocusSummary,
    focusEmptyText: viewModel.operatorFocusEmptyText,
    focusOverflowLabel: viewModel.operatorFocusOverflowLabel,
    focusRows,
    linkedContextLabel: viewModel.linkedContextLabel,
    linkedContextSummary: viewModel.linkedContextSummary,
    linkedContextPostureLabel: viewModel.linkedContextPostureLabel,
    linkedContextBadgeVariant: dailyControlTowerBadgeVariant(viewModel.linkedContextPostureTone),
    linkedContextEmptyText: viewModel.linkedContextEmptyText,
    linkedContextItems: viewModel.linkedContextItems.slice(0, 4),
    evidenceTimelineLabel: viewModel.evidenceTimelineLabel,
    evidenceTimelineSummary: viewModel.evidenceTimelineSummary,
    evidenceTimelineEmptyText: viewModel.evidenceTimelineEmptyText,
    evidenceTimelineOverflowLabel: viewModel.evidenceTimelineOverflowLabel,
    evidenceTimelineItems: viewModel.evidenceTimelineItems.slice(0, 4)
  };
}

function buildDailyControlTowerDecisionFacts(
  decision: AppShellDecisionBrief,
  decisionBadgeVariant: DailyControlTowerBadgeVariant,
  primaryRow: DailyControlTowerFocusRow | null,
  fallbackProof: AppShellEvidenceTimelineItem | null
): DailyControlTowerDecisionFact[] {
  const proof = primaryRow?.proof ?? fallbackProof;
  const proofValue = proof
    ? `${proof.label} / ${proof.timestampLabel}`
    : decision.evidenceLabel ?? "Proof loads from the target route";
  const proofDetail = proof?.detail
    ?? (decision.evidenceLabel ? "Decision proof is summarized by the shell workflow evidence." : "Open the next action route for supporting evidence.");
  const proofHref = proof?.route ?? (decision.evidenceLabel ? decision.actionHref : null);
  const proofAriaLabel = proof?.ariaLabel ?? (decision.evidenceLabel ? decision.actionAriaLabel : null);

  return [
    {
      id: "status",
      label: "Blocked state",
      value: decision.statusLabel,
      detail: decision.reason,
      href: null,
      ariaLabel: null,
      badgeVariant: decisionBadgeVariant
    },
    {
      id: "owner",
      label: "Owner",
      value: primaryRow?.item.workspaceLabel ?? "No owner loaded",
      detail: primaryRow?.item.detail ?? "No higher-priority owner is loaded in the shell workflow model.",
      href: primaryRow?.item.route ?? null,
      ariaLabel: primaryRow?.item.ariaLabel ?? null,
      badgeVariant: null
    },
    {
      id: "output",
      label: "Affected output",
      value: primaryRow?.outputLabel ?? decision.title,
      detail: primaryRow ? primaryRow.item.detail : decision.summary,
      href: primaryRow?.item.route ?? decision.actionHref,
      ariaLabel: primaryRow?.item.ariaLabel ?? decision.actionAriaLabel,
      badgeVariant: null
    },
    {
      id: "next-action",
      label: "Next action",
      value: decision.actionLabel,
      detail: decision.summary,
      href: decision.actionHref,
      ariaLabel: decision.actionAriaLabel,
      badgeVariant: null
    },
    {
      id: "proof",
      label: "Supporting proof",
      value: proofValue,
      detail: proofDetail,
      href: proofHref,
      ariaLabel: proofAriaLabel,
      badgeVariant: proof ? dailyControlTowerBadgeVariant(proof.tone) : null
    }
  ];
}

export function findDailyControlTowerProof(
  item: AppShellOperatorFocusItem,
  evidenceItems: AppShellEvidenceTimelineItem[]
): AppShellEvidenceTimelineItem | null {
  return evidenceItems.find((candidate) => candidate.id === item.id)
    ?? evidenceItems.find((candidate) => candidate.workspaceLabel === item.workspaceLabel)
    ?? evidenceItems[0]
    ?? null;
}

export function dailyControlTowerStatusLabel(tone: AppShellWorkflowContinuityStatusTone): string {
  switch (tone) {
    case "blocked":
      return "Blocked";
    case "review":
      return "Review";
    case "pending":
      return "Pending";
    case "ready":
      return "Ready";
  }
}

export function dailyControlTowerOutputLabel(item: AppShellOperatorFocusItem): string {
  return `${item.workspaceLabel}: ${item.label}`;
}

export function dailyControlTowerBadgeVariant(tone: AppShellWorkflowContinuityStatusTone): DailyControlTowerBadgeVariant {
  switch (tone) {
    case "blocked":
      return "danger";
    case "review":
      return "warning";
    case "ready":
      return "success";
    case "pending":
    default:
      return "outline";
  }
}

function buildDailyControlTowerProofPassportItems({
  item,
  statusLabel,
  outputLabel,
  badgeVariant,
  proof
}: {
  item: AppShellOperatorFocusItem;
  statusLabel: string;
  outputLabel: string;
  badgeVariant: DailyControlTowerBadgeVariant;
  proof: AppShellEvidenceTimelineItem | null;
}): DailyControlTowerProofPassportItem[] {
  const proofLabel = proof ? `${proof.label} / ${proof.timestampLabel}` : "Route-owned proof";
  const proofDetail = proof?.detail ?? "Open the target route for supporting evidence.";
  const proofHref = proof?.route ?? item.route;
  const proofAriaLabel = proof?.ariaLabel ?? item.ariaLabel;
  const routeOwnedDetail = "Owned by the target workspace route and its shared read model.";

  return [
    {
      id: "source",
      label: "Source",
      value: item.workspaceLabel,
      detail: item.detail,
      href: item.route,
      ariaLabel: item.ariaLabel,
      badgeVariant
    },
    {
      id: "freshness",
      label: "Freshness",
      value: proof?.timestampLabel ?? "Open route",
      detail: proof?.detail ?? "Freshness is supplied by the target route evidence.",
      href: proofHref,
      ariaLabel: proofAriaLabel,
      badgeVariant: proof ? dailyControlTowerBadgeVariant(proof.tone) : "outline"
    },
    {
      id: "reconciliation",
      label: "Reconciliation",
      value: controlTowerFacetValue(item, proof, ["reconciliation", "reconcile", "break", "ledger", "matched"], statusLabel),
      detail: controlTowerFacetDetail(item, proof, ["reconciliation", "reconcile", "break", "ledger", "matched"], routeOwnedDetail),
      href: item.route,
      ariaLabel: item.ariaLabel,
      badgeVariant: controlTowerFacetVariant(item, proof, ["reconciliation", "reconcile", "break", "ledger", "matched"], badgeVariant)
    },
    {
      id: "approvals",
      label: "Approvals",
      value: controlTowerFacetValue(item, proof, ["approval", "approve", "sign-off", "certify", "review"], statusLabel),
      detail: controlTowerFacetDetail(item, proof, ["approval", "approve", "sign-off", "certify", "review"], routeOwnedDetail),
      href: item.route,
      ariaLabel: item.ariaLabel,
      badgeVariant: controlTowerFacetVariant(item, proof, ["approval", "approve", "sign-off", "certify", "review"], badgeVariant)
    },
    {
      id: "report-usage",
      label: "Report Usage",
      value: controlTowerFacetValue(item, proof, ["report", "delivery", "statement", "pack"], outputLabel),
      detail: controlTowerFacetDetail(item, proof, ["report", "delivery", "statement", "pack"], routeOwnedDetail),
      href: item.route,
      ariaLabel: item.ariaLabel,
      badgeVariant: controlTowerFacetVariant(item, proof, ["report", "delivery", "statement", "pack"], badgeVariant)
    },
    {
      id: "blockers",
      label: "Blockers",
      value: item.tone === "blocked" || item.tone === "review" ? statusLabel : "None disclosed",
      detail: item.detail,
      href: item.route,
      ariaLabel: item.ariaLabel,
      badgeVariant
    },
    {
      id: "evidence-packet",
      label: "Evidence Packet",
      value: proofLabel,
      detail: proofDetail,
      href: proofHref,
      ariaLabel: proofAriaLabel,
      badgeVariant: proof ? dailyControlTowerBadgeVariant(proof.tone) : badgeVariant
    },
    {
      id: "audit-trail",
      label: "Audit Trail",
      value: proof?.timestampLabel ?? "Open route",
      detail: proof?.detail ?? "Audit trail is available from the target route evidence.",
      href: proofHref,
      ariaLabel: proofAriaLabel,
      badgeVariant: proof ? dailyControlTowerBadgeVariant(proof.tone) : "outline"
    }
  ];
}

function controlTowerFacetValue(
  item: AppShellOperatorFocusItem,
  proof: AppShellEvidenceTimelineItem | null,
  keywords: string[],
  matchedValue: string
): string {
  return controlTowerFacetMatches(item, proof, keywords) ? matchedValue : "Route-owned";
}

function controlTowerFacetDetail(
  item: AppShellOperatorFocusItem,
  proof: AppShellEvidenceTimelineItem | null,
  keywords: string[],
  fallback: string
): string {
  if (!controlTowerFacetMatches(item, proof, keywords)) {
    return fallback;
  }

  return proof?.detail ?? item.detail;
}

function controlTowerFacetVariant(
  item: AppShellOperatorFocusItem,
  proof: AppShellEvidenceTimelineItem | null,
  keywords: string[],
  matchedVariant: DailyControlTowerBadgeVariant
): DailyControlTowerBadgeVariant {
  return controlTowerFacetMatches(item, proof, keywords)
    ? proof ? dailyControlTowerBadgeVariant(proof.tone) : matchedVariant
    : "outline";
}

function controlTowerFacetMatches(
  item: AppShellOperatorFocusItem,
  proof: AppShellEvidenceTimelineItem | null,
  keywords: string[]
): boolean {
  const text = [
    item.id,
    item.label,
    item.detail,
    item.route,
    item.workspaceLabel,
    item.actionLabel,
    proof?.id,
    proof?.label,
    proof?.detail,
    proof?.route,
    proof?.workspaceLabel
  ].join(" ").toLowerCase();

  return keywords.some((keyword) => text.includes(keyword));
}
