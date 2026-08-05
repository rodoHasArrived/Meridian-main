import type {
  AppShellDecisionBrief,
  AppShellEvidenceTimelineItem,
  AppShellLinkedContextItem,
  AppShellOperatorFocusItem,
  AppShellTrustStripItem,
  AppShellTrustStripState,
  AppShellWorkflowContinuityStatusTone,
  AppShellWorkflowContinuityViewModel
} from "@/app-shell.view-model";

export type DailyControlTowerBadgeVariant = "outline" | "success" | "warning" | "danger";

export interface DailyControlTowerProofPassportItem {
  id: string;
  label: string;
  value: string;
  detail: string;
}

export interface DailyControlTowerFocusRow {
  item: AppShellOperatorFocusItem;
  statusLabel: string;
  outputLabel: string;
  badgeVariant: DailyControlTowerBadgeVariant;
  proof: AppShellEvidenceTimelineItem | null;
  proofPassportItems: DailyControlTowerProofPassportItem[];
  proofPassportSummary: string;
}

export interface DailyControlTowerDriverItem {
  id: string;
  label: string;
  value: string;
  detail: string;
  badgeVariant: DailyControlTowerBadgeVariant;
}

export interface DailyControlTowerModel {
  decision: AppShellDecisionBrief;
  statusLabel: string;
  statusTone: AppShellWorkflowContinuityStatusTone;
  statusBadgeVariant: DailyControlTowerBadgeVariant;
  ownerLabel: string;
  outputLabel: string;
  nextActionLabel: string;
  nextActionHref: string;
  nextActionAriaLabel: string;
  evidenceLabel: string;
  driverItems: DailyControlTowerDriverItem[];
  queueRows: DailyControlTowerFocusRow[];
  trustItems: AppShellTrustStripItem[];
  linkedContextItems: AppShellLinkedContextItem[];
  evidenceTimelineItems: AppShellEvidenceTimelineItem[];
  emptyQueueText: string;
}

const proofPassportLabels = [
  "Source",
  "Freshness",
  "Reconciliation",
  "Approvals",
  "Report Usage",
  "Blockers",
  "Evidence Packet",
  "Audit Trail"
] as const;

export function buildDailyControlTowerModel(
  viewModel: AppShellWorkflowContinuityViewModel,
  trustStrip: AppShellTrustStripState
): DailyControlTowerModel {
  const focusItems = viewModel.operatorFocusCommandItems.length > 0
    ? viewModel.operatorFocusCommandItems
    : viewModel.operatorFocusItems;
  const queueRows = focusItems.map((item) => buildFocusRow(item, viewModel));
  const leadingRow = queueRows[0] ?? null;
  const decision = viewModel.decisionBrief;

  return {
    decision,
    statusLabel: decision.statusLabel,
    statusTone: decision.statusTone,
    statusBadgeVariant: badgeVariantForTone(decision.statusTone),
    ownerLabel: leadingRow?.item.workspaceLabel ?? "Current workstation",
    outputLabel: leadingRow?.outputLabel ?? outputLabelForRoute(decision.actionHref, "Workstation"),
    nextActionLabel: decision.actionLabel,
    nextActionHref: decision.actionHref,
    nextActionAriaLabel: decision.actionAriaLabel,
    evidenceLabel: decision.evidenceLabel ?? leadingRow?.proof?.label ?? "No evidence linked",
    driverItems: buildDriverItems({
      viewModel,
      queueRows,
      trustItems: trustStrip.items,
      linkedContextItems: viewModel.linkedContextItems,
      evidenceTimelineItems: viewModel.evidenceTimelineItems
    }),
    queueRows,
    trustItems: trustStrip.items,
    linkedContextItems: viewModel.linkedContextItems,
    evidenceTimelineItems: viewModel.evidenceTimelineItems,
    emptyQueueText: viewModel.operatorFocusEmptyText
  };
}

function buildDriverItems({
  viewModel,
  queueRows,
  trustItems,
  linkedContextItems,
  evidenceTimelineItems
}: {
  viewModel: AppShellWorkflowContinuityViewModel;
  queueRows: DailyControlTowerFocusRow[];
  trustItems: AppShellTrustStripItem[];
  linkedContextItems: AppShellLinkedContextItem[];
  evidenceTimelineItems: AppShellEvidenceTimelineItem[];
}): DailyControlTowerDriverItem[] {
  const blockedRows = queueRows.filter((row) => row.item.tone === "blocked").length;
  const reviewRows = queueRows.filter((row) => row.item.tone === "review").length;
  const trustAttention = trustItems.filter((item) => item.tone === "blocked" || item.tone === "review").length;
  const linkedAttention = linkedContextItems.filter((item) => item.tone === "blocked" || item.tone === "review" || item.tone === "pending").length;

  return [
    {
      id: "continuity",
      label: "Continuity source",
      value: viewModel.title,
      detail: `${viewModel.summary} Source route: ${viewModel.routeLabel}.`,
      badgeVariant: badgeVariantForTone(viewModel.decisionBrief.statusTone)
    },
    {
      id: "queue",
      label: "Finance queue",
      value: queueRows.length > 0 ? `${blockedRows} blocked / ${reviewRows} review` : "0 queued",
      detail: queueRows.length > 0
        ? `${queueRows.length} ranked finance focus item(s) drive the daily decision queue.`
        : "No finance focus items are currently queued.",
      badgeVariant: blockedRows > 0 ? "danger" : reviewRows > 0 ? "warning" : "success"
    },
    {
      id: "trust",
      label: "Trust posture",
      value: trustAttention > 0 ? `${trustAttention} need attention` : "Trusted",
      detail: `${trustItems.length} shell trust signal(s) are folded into the landing decision.`,
      badgeVariant: trustItems.some((item) => item.tone === "blocked") ? "danger" : trustAttention > 0 ? "warning" : "success"
    },
    {
      id: "context",
      label: "Linked context",
      value: linkedContextItems.length > 0 ? `${linkedAttention}/${linkedContextItems.length} checks` : "No subject",
      detail: linkedContextItems.length > 0
        ? "Cross-workspace context is linked before the operator leaves the landing queue."
        : "No symbol or operating subject is linked yet.",
      badgeVariant: linkedContextItems.some((item) => item.tone === "blocked") ? "danger" : linkedAttention > 0 ? "warning" : "outline"
    },
    {
      id: "proof",
      label: "Evidence events",
      value: evidenceTimelineItems.length > 0 ? `${evidenceTimelineItems.length} retained` : "No events",
      detail: evidenceTimelineItems.length > 0
        ? "Timestamped evidence events back the visible queue and evidence summaries."
        : "No timestamped evidence event is linked to this decision.",
      badgeVariant: evidenceTimelineItems.length > 0 ? "success" : "warning"
    }
  ];
}

export function dailyControlTowerProofPassportLabels(): string[] {
  return [...proofPassportLabels];
}

function buildFocusRow(
  item: AppShellOperatorFocusItem,
  viewModel: AppShellWorkflowContinuityViewModel
): DailyControlTowerFocusRow {
  const proof = findProofForItem(item, viewModel.evidenceTimelineItems);
  const linkedContext = findLinkedContextForItem(item, viewModel.linkedContextItems);
  const outputLabel = outputLabelForRoute(item.route, item.workspaceLabel);
  const statusLabel = statusLabelForTone(item.tone);
  const proofPassportItems = buildProofPassportItems({
    item,
    proof,
    linkedContext,
    viewModel,
    outputLabel
  });

  return {
    item,
    statusLabel,
    outputLabel,
    badgeVariant: badgeVariantForTone(item.tone),
    proof,
    proofPassportItems,
    proofPassportSummary: `${item.label}: ${statusLabel}, ${outputLabel}, ${proofPassportItems[1]?.value ?? "No freshness evidence"}.`
  };
}

function buildProofPassportItems({
  item,
  proof,
  linkedContext,
  viewModel,
  outputLabel
}: {
  item: AppShellOperatorFocusItem;
  proof: AppShellEvidenceTimelineItem | null;
  linkedContext: AppShellLinkedContextItem | null;
  viewModel: AppShellWorkflowContinuityViewModel;
  outputLabel: string;
}): DailyControlTowerProofPassportItem[] {
  const source = proof?.workspaceLabel ?? item.workspaceLabel;
  const freshness = proof?.timestampLabel ?? "No timestamped evidence";
  const reconciliation = linkedContext?.statusLabel ?? viewModel.linkedContextPostureLabel;
  const approvals = approvalsLabelForTone(item.tone);
  const evidencePacket = proof?.label ?? "No evidence packet linked";
  const auditTrail = proof?.timestampIso ?? "Audit timestamp unavailable";

  return [
    {
      id: "source",
      label: "Source",
      value: source,
      detail: `Primary source workspace for ${item.label}.`
    },
    {
      id: "freshness",
      label: "Freshness",
      value: freshness,
      detail: proof ? proof.detail : "No timestamped evidence event is linked to this item."
    },
    {
      id: "reconciliation",
      label: "Reconciliation",
      value: reconciliation,
      detail: linkedContext?.detail ?? viewModel.linkedContextSummary
    },
    {
      id: "approvals",
      label: "Approvals",
      value: approvals,
      detail: item.actionLabel
    },
    {
      id: "report-usage",
      label: "Report Usage",
      value: outputLabel,
      detail: `Downstream output affected by ${item.label}.`
    },
    {
      id: "blockers",
      label: "Blockers",
      value: item.detail,
      detail: item.ariaLabel
    },
    {
      id: "evidence-packet",
      label: "Evidence Packet",
      value: evidencePacket,
      detail: proof?.ariaLabel ?? "No correlated evidence packet is linked to this queue item."
    },
    {
      id: "audit-trail",
      label: "Audit Trail",
      value: auditTrail,
      detail: proof?.route ?? item.route
    }
  ];
}

function findProofForItem(
  item: AppShellOperatorFocusItem,
  evidenceItems: AppShellEvidenceTimelineItem[]
): AppShellEvidenceTimelineItem | null {
  // Evidence association is deliberately fail-closed. Route, workspace, tone,
  // and list position are presentation attributes, not durable correlation
  // keys, so they must never be used to attach a different event to this row.
  return evidenceItems.find((proof) => proof.id === item.id) ?? null;
}

function findLinkedContextForItem(
  item: AppShellOperatorFocusItem,
  linkedContextItems: AppShellLinkedContextItem[]
): AppShellLinkedContextItem | null {
  return linkedContextItems.find((context) => routeFingerprint(context.route) === routeFingerprint(item.route))
    ?? linkedContextItems.find((context) => context.workspaceLabel === item.workspaceLabel)
    ?? null;
}

function routeFingerprint(route: string): string {
  return route.split(/[?#]/, 1)[0] || "/";
}

function statusLabelForTone(tone: AppShellWorkflowContinuityStatusTone): string {
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

function badgeVariantForTone(tone: AppShellWorkflowContinuityStatusTone): DailyControlTowerBadgeVariant {
  switch (tone) {
    case "blocked":
      return "danger";
    case "review":
      return "warning";
    case "ready":
      return "success";
    case "pending":
      return "outline";
  }
}

function approvalsLabelForTone(tone: AppShellWorkflowContinuityStatusTone): string {
  switch (tone) {
    case "blocked":
      return "Blocked";
    case "review":
      return "Review required";
    case "pending":
      return "Pending";
    case "ready":
      return "Ready";
  }
}

function outputLabelForRoute(route: string, workspaceLabel: string): string {
  const pathname = routeFingerprint(route);
  if (pathname.startsWith("/reporting")) {
    return "Reporting package or evidence";
  }

  if (pathname.startsWith("/accounting")) {
    return "Accounting record or close evidence";
  }

  if (pathname.startsWith("/portfolio")) {
    return "Portfolio exposure or brokerage sync";
  }

  if (pathname.startsWith("/trading")) {
    return "Trading readiness or paper session";
  }

  if (pathname.startsWith("/data")) {
    return "Data health or provider review";
  }

  if (pathname.startsWith("/settings")) {
    return "Provider connections or diagnostics";
  }

  if (pathname.startsWith("/strategy")) {
    return "Strategy run or research evidence";
  }

  return `${workspaceLabel} output`;
}
