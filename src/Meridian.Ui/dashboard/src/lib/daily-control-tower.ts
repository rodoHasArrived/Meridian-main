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
import { summarizeStatusSignals } from "@/lib/status-signals";
import { normalizeWorkspacePath, WORKSPACES } from "@/lib/workspace";
import type { WorkspaceKey } from "@/types";

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
  /** The row for the decision the headline names; its evidence is shown until a row is chosen. */
  leadingRow: DailyControlTowerFocusRow | null;
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

/**
 * The decision queue the Daily Control Tower lists: every ranked focus candidate, or the visible
 * focus items when no ranked list exists. The one selection both the Control Tower and the
 * sidebar attention counts read, so the two can never disagree about what needs an operator.
 */
export function selectDailyControlTowerQueueItems(viewModel: AppShellWorkflowContinuityViewModel): AppShellOperatorFocusItem[] {
  return viewModel.operatorFocusCommandItems.length > 0
    ? viewModel.operatorFocusCommandItems
    : viewModel.operatorFocusItems;
}

/**
 * The queued item every status surface leads with: the one the view model ranked worst, or the
 * first of the worst tone when the view model names none. The masthead attention pill and the
 * Control Tower's headline both read this, so they name the same decision on every route.
 */
export function selectLeadingDecisionItem(viewModel: AppShellWorkflowContinuityViewModel): AppShellOperatorFocusItem | null {
  const queue = selectDailyControlTowerQueueItems(viewModel);
  const worst = summarizeStatusSignals(queue).worst ?? queue[0] ?? null;
  if (!worst) {
    return null;
  }
  const named = queue.find((item) => item.id === viewModel.leadingOperatorFocusItemId);
  return named && named.tone === worst.tone ? named : worst;
}

export interface WorkspaceAttentionCounts {
  /** Every queued item: the "Today" entry's count. */
  total: number;
  byWorkspace: Partial<Record<WorkspaceKey, number>>;
}

/** Per-workspace counts of the Control Tower queue, for the sidebar. */
export function buildWorkspaceAttentionCounts(viewModel: AppShellWorkflowContinuityViewModel): WorkspaceAttentionCounts {
  const byWorkspace: Partial<Record<WorkspaceKey, number>> = {};
  const queue = selectDailyControlTowerQueueItems(viewModel);
  for (const item of queue) {
    const key = workspaceKeyForQueueItem(item);
    if (key) {
      byWorkspace[key] = (byWorkspace[key] ?? 0) + 1;
    }
  }
  return { total: queue.length, byWorkspace };
}

function workspaceKeyForQueueItem(item: AppShellOperatorFocusItem): WorkspaceKey | null {
  const pathname = item.route.split(/[?#]/, 1)[0].replace(/^\/workstation(?=\/)/, "");
  const segment = pathname.split("/").filter(Boolean)[0];
  if (segment) {
    const key = normalizeWorkspacePath(pathname);
    // normalizeWorkspacePath falls back to Trading for an unknown root; only trust a real match.
    if (key !== "trading" || segment === "trading") {
      return key;
    }
  }
  const label = item.workspaceLabel.trim().toLowerCase();
  return WORKSPACES.find((workspace) => workspace.label.toLowerCase() === label)?.key ?? null;
}

export function buildDailyControlTowerModel(
  viewModel: AppShellWorkflowContinuityViewModel,
  trustStrip: AppShellTrustStripState
): DailyControlTowerModel {
  const focusItems = selectDailyControlTowerQueueItems(viewModel);
  const queueRows = focusItems.map((item) => buildFocusRow(item, viewModel));
  const leadingItemId = selectLeadingDecisionItem(viewModel)?.id ?? null;
  const leadingRow = queueRows.find((row) => row.item.id === leadingItemId) ?? queueRows[0] ?? null;
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
    evidenceLabel: buildDecisionEvidenceLabel(decision.evidenceLabel, leadingRow, viewModel.evidenceTimelineItems[0] ?? null),
    driverItems: buildDriverItems({
      viewModel,
      queueRows,
      trustItems: trustStrip.items,
      linkedContextItems: viewModel.linkedContextItems,
      evidenceTimelineItems: viewModel.evidenceTimelineItems
    }),
    queueRows,
    leadingRow,
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

/**
 * The hero's evidence line for the leading decision. Evidence association is fail-closed (see
 * findProofForItem): the workstation's latest evidence event is this decision's evidence only
 * when it is the event correlated with the leading row. Without a correlated event the line says
 * so and names the latest event as workstation context, which is the same answer the Freshness
 * card gives for the row instead of a timestamp that reads as proof for it.
 */
function buildDecisionEvidenceLabel(
  decisionEvidenceLabel: string | null,
  leadingRow: DailyControlTowerFocusRow | null,
  latestEvidence: AppShellEvidenceTimelineItem | null
): string {
  if (!leadingRow) {
    return decisionEvidenceLabel ?? "No evidence linked";
  }

  if (leadingRow.proof) {
    return `Linked evidence: ${leadingRow.proof.workspaceLabel} ${leadingRow.proof.timestampLabel}`;
  }

  return latestEvidence
    ? `No evidence linked to this item. Latest workstation evidence: ${latestEvidence.workspaceLabel} ${latestEvidence.timestampLabel}`
    : "No evidence linked to this item";
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
