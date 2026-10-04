/**
 * Presentation logic for the reconciliation queue readiness panel.
 *
 * Three read models the workstation never called sit behind this panel. Two of
 * them answer questions the existing break queue cannot: `queue-status` reports
 * per-account sign-off readiness with the server's own next-best-action and
 * blocker, and `cases` reports open casework with its SLA state. The third,
 * `break-queue/taxonomy`, is the catalog those casework codes are drawn from —
 * without it a root-cause code renders as the raw token the operator has to
 * recognize from memory.
 */

import type {
  ReconciliationCaseSummary,
  ReconciliationQueueAccountStatus,
  ReconciliationTaxonomySnapshot,
  ReconciliationTaxonomyValue
} from "@/types/reconciliation-readiness.types";

export type ReadinessTone = "default" | "success" | "warning" | "danger";

export interface QueueAccountRowViewModel {
  accountId: string;
  accountCode: string;
  queueState: string;
  unresolvedBreakCount: number;
  readinessLabel: string;
  readinessTone: ReadinessTone;
  nextBestAction: string;
  blockerReason: string | null;
  evidenceCountLabel: string;
  ariaLabel: string;
}

export interface QueueReadinessSummaryViewModel {
  accountsLabel: string;
  readyLabel: string;
  blockedLabel: string;
  unresolvedLabel: string;
  /** Null when nothing is blocked, so the panel does not manufacture an alarm. */
  blockedNotice: string | null;
}

export interface OpenCaseRowViewModel {
  caseId: string;
  status: string;
  reason: string;
  priority: string;
  assignee: string;
  slaLabel: string;
  slaTone: ReadinessTone;
  ageLabel: string;
  rootCauseLabel: string;
  resolutionLabel: string;
  confidenceLabel: string;
  ariaLabel: string;
}

export interface TaxonomyViewModel {
  loaded: boolean;
  versionLabel: string;
  rootCauseCount: number;
  resolutionCount: number;
  /** Codes a case cites that the catalog does not define — a real drift signal. */
  unknownCodes: string[];
  unknownNotice: string | null;
}

/** SLA states the server reports; anything else is shown verbatim, not bucketed. */
const BREACHED_SLA_STATES = new Set(["breached", "overdue"]);
const WARNING_SLA_STATES = new Set(["warning", "atrisk", "at-risk", "duesoon"]);

export function buildQueueAccountRow(status: ReconciliationQueueAccountStatus): QueueAccountRowViewModel {
  const blockerReason = status.blockerReason?.trim() || null;
  const readinessLabel = status.signOffReady ? "Ready" : "Not ready";
  // Sign-off readiness is the server's flag. A blocker alongside a ready flag is
  // contradictory input, so it is shown rather than resolved in the browser.
  const readinessTone: ReadinessTone = status.signOffReady
    ? (blockerReason ? "warning" : "success")
    : (status.unresolvedBreakCount > 0 ? "danger" : "warning");

  return {
    accountId: status.accountId,
    accountCode: status.accountCode?.trim() || status.accountId,
    queueState: status.queueState?.trim() || "Unreported",
    unresolvedBreakCount: status.unresolvedBreakCount,
    readinessLabel,
    readinessTone,
    nextBestAction: status.nextBestAction?.trim() || "No next action reported.",
    blockerReason,
    evidenceCountLabel: describeEvidenceCount(status.evidenceLinks),
    ariaLabel: `Account ${status.accountCode?.trim() || status.accountId}: ${readinessLabel}, `
      + `${status.unresolvedBreakCount} unresolved break${status.unresolvedBreakCount === 1 ? "" : "s"}. `
      + (blockerReason ? `Blocked: ${blockerReason}.` : `Next: ${status.nextBestAction?.trim() || "not reported"}.`)
  };
}

export function buildQueueReadinessSummary(
  statuses: ReconciliationQueueAccountStatus[] | null
): QueueReadinessSummaryViewModel {
  if (!statuses) {
    return {
      accountsLabel: "—",
      readyLabel: "—",
      blockedLabel: "—",
      unresolvedLabel: "—",
      blockedNotice: null
    };
  }

  const ready = statuses.filter((status) => status.signOffReady).length;
  const blocked = statuses.filter((status) => (status.blockerReason?.trim() ?? "") !== "").length;
  const unresolved = statuses.reduce((total, status) => total + status.unresolvedBreakCount, 0);

  return {
    accountsLabel: String(statuses.length),
    readyLabel: `${ready} of ${statuses.length}`,
    blockedLabel: String(blocked),
    unresolvedLabel: String(unresolved),
    blockedNotice: blocked > 0
      ? `${blocked} account${blocked === 1 ? " reports a blocker" : "s report a blocker"} that must clear before sign-off.`
      : null
  };
}

export function buildOpenCaseRow(
  summary: ReconciliationCaseSummary,
  taxonomy: ReconciliationTaxonomySnapshot | null
): OpenCaseRowViewModel {
  const slaState = summary.slaState?.trim() || "Unreported";
  const rootCauseLabel = describeTaxonomyCode(summary.rootCauseCode, taxonomy?.rootCauses);
  const resolutionLabel = describeTaxonomyCode(summary.resolutionCode, taxonomy?.resolutionCodes);

  return {
    caseId: summary.caseId,
    status: summary.status?.trim() || "Unreported",
    reason: summary.reason?.trim() || "No reason recorded.",
    priority: summary.priority?.trim() || "Normal",
    assignee: summary.assignee?.trim() || "Unassigned",
    slaLabel: describeSla(summary),
    slaTone: slaTone(slaState),
    ageLabel: describeAge(summary),
    rootCauseLabel,
    resolutionLabel,
    confidenceLabel: `${Math.round(summary.confidence * 100)}%`,
    ariaLabel: `Case ${summary.caseId}, ${summary.status?.trim() || "unreported"}, SLA ${slaState}, `
      + `root cause ${rootCauseLabel}, assigned to ${summary.assignee?.trim() || "nobody"}.`
  };
}

export function buildTaxonomyViewModel(
  taxonomy: ReconciliationTaxonomySnapshot | null,
  cases: ReconciliationCaseSummary[] | null
): TaxonomyViewModel {
  if (!taxonomy) {
    return {
      loaded: false,
      versionLabel: "Not loaded",
      rootCauseCount: 0,
      resolutionCount: 0,
      unknownCodes: [],
      unknownNotice: null
    };
  }

  // Checked per slot, matching how each row resolves its own label: a resolution
  // code that only exists in the root-cause catalog is drift, and merging the two
  // catalogs here would let the row say "not in taxonomy" while this stays silent.
  const knownRootCauses = new Set(taxonomy.rootCauses.map((value) => value.code));
  const knownResolutions = new Set(taxonomy.resolutionCodes.map((value) => value.code));
  const cited = new Set<string>();
  for (const summary of cases ?? []) {
    for (const [code, known] of [
      [summary.rootCauseCode, knownRootCauses],
      [summary.resolutionCode, knownResolutions]
    ] as const) {
      const trimmed = code?.trim();
      if (trimmed && !known.has(trimmed)) {
        cited.add(trimmed);
      }
    }
  }

  const unknownCodes = [...cited].sort();
  return {
    loaded: true,
    versionLabel: `v${taxonomy.version}`,
    rootCauseCount: taxonomy.rootCauses.length,
    resolutionCount: taxonomy.resolutionCodes.length,
    unknownCodes,
    unknownNotice: unknownCodes.length > 0
      ? `${unknownCodes.length} code${unknownCodes.length === 1 ? "" : "s"} cited by open cases `
        + `${unknownCodes.length === 1 ? "is" : "are"} not in taxonomy ${`v${taxonomy.version}`}: ${unknownCodes.join(", ")}.`
      : null
  };
}

/**
 * Resolves a casework code to its catalog display name. An unset code and a code
 * the catalog does not define are different states and read differently: the
 * first is work not yet done, the second is drift between case and catalog.
 */
function describeTaxonomyCode(
  code: string | null | undefined,
  catalog: ReconciliationTaxonomyValue[] | undefined
): string {
  const trimmed = code?.trim();
  if (!trimmed) {
    return "Unset";
  }

  if (!catalog) {
    return trimmed;
  }

  const match = catalog.find((value) => value.code === trimmed);
  if (!match) {
    return `${trimmed} (not in taxonomy)`;
  }

  return match.isActive ? match.displayName : `${match.displayName} (retired)`;
}

function describeSla(summary: ReconciliationCaseSummary): string {
  const state = summary.slaState?.trim() || "Unreported";
  if (summary.slaBreachedAtUtc) {
    return `${state} — breached ${summary.slaBreachedAtUtc}`;
  }

  return summary.slaDueAtUtc ? `${state} — due ${summary.slaDueAtUtc}` : state;
}

function slaTone(state: string): ReadinessTone {
  const normalized = state.toLowerCase().replace(/\s+/g, "");
  if (BREACHED_SLA_STATES.has(normalized)) {
    return "danger";
  }

  if (WARNING_SLA_STATES.has(normalized)) {
    return "warning";
  }

  return normalized === "ontrack" ? "success" : "default";
}

function describeAge(summary: ReconciliationCaseSummary): string {
  const band = summary.ageBand?.trim();
  const hours = `${summary.businessAgeHours.toFixed(1)}h business`;
  return band ? `${band} (${hours})` : hours;
}

function describeEvidenceCount(links: string[] | null | undefined): string {
  const count = links?.length ?? 0;
  if (count === 0) {
    return "No evidence linked";
  }

  return `${count} evidence link${count === 1 ? "" : "s"}`;
}
