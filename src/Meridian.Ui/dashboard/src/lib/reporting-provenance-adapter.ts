/**
 * Adapts the workspace's record graph into the reporting lineage model.
 *
 * `reportLineProvenanceExplorer` already carries real provenance: a node per record
 * relationship (position transaction, instrument passport, reconciliation, journal,
 * report line, evidence, audit event) and the edges between them. What it does not
 * carry is a *stage ordering*, so a graph missing its reconciliation renders the same
 * way as one that has it.
 *
 * This adapter maps those node types onto the governed trace stages, so a reader gets
 * source-to-publication order and an explicit statement of which stages are absent.
 *
 * ## Placeholder nodes
 *
 * The read service emits a node for every relationship slot, present or not. When the
 * relationship is missing it substitutes the slot name as the label, leaves the href
 * empty, and tones the node `Warning`. Those placeholders are *not* evidence that a
 * stage happened, so this adapter reports them as gaps rather than as steps - which is
 * the same distinction the trace model exists to make. A node is treated as a
 * placeholder only when both signals agree: an empty href and a label identical to its
 * node type.
 */
import {
  buildReportingTrace,
  normalizeReportingTraceStage,
  type ReportingTraceModel,
  type ReportingTraceStage,
  type ReportingTraceStepInput
} from "@/lib/reporting-trace";
import type {
  FinancialRecordExplorerGraphNodeDto,
  FinancialRecordExplorerRecordGraphDto
} from "@/types";

/**
 * Relationship slots the read service emits, mapped onto lineage stages.
 *
 * A journal posting is the calculation step for an accounting figure: it is where
 * the debits and credits that produce the reported amount are struck. Evidence and
 * audit events are preservation, which is the publication end of the lineage.
 */
const NODE_TYPE_STAGES: Record<string, ReportingTraceStage> = {
  "position-transaction": "Source",
  "instrument-passport": "Source",
  "asset-operations-readiness": "Normalization",
  "terms-obligations": "Normalization",
  journal: "Calculation",
  reconciliation: "Reconciliation",
  "report-line": "ReportBlock",
  evidence: "Publication",
  "audit-event": "Publication"
};

const RELATIONSHIP_NODE_PREFIX = "rel:";

export interface RecordGraphTraceOptions {
  /** Stages to report as gaps beyond the structurally required ones. */
  requiredStages?: readonly string[];
}

/**
 * Build the lineage for one record in the graph.
 *
 * Only the nodes belonging to `recordId` are considered: the graph holds a chain per
 * row, and mixing them would attribute one record's reconciliation to another.
 */
export function buildTraceFromRecordGraph(
  graph: FinancialRecordExplorerRecordGraphDto | null | undefined,
  recordId: string,
  options: RecordGraphTraceOptions = {}
): ReportingTraceModel {
  const nodes = graph?.nodes ?? [];
  const scoped = nodes.filter((node) => belongsToRecord(node, recordId));

  const steps: ReportingTraceStepInput[] = [];
  for (const node of scoped) {
    if (isPlaceholder(node)) {
      continue;
    }

    const stage = resolveStage(node, recordId);
    if (stage === null) {
      // Retained as an unresolved step by the trace builder rather than dropped.
      steps.push({ stage: node.nodeType, label: node.label, href: nonEmpty(node.href) });
      continue;
    }

    steps.push({
      stage,
      label: node.label,
      system: node.nodeType === stage ? null : titleCase(node.nodeType),
      href: nonEmpty(node.href)
    });
  }

  return buildReportingTrace({ steps, requiredStages: options.requiredStages });
}

/** The record ids the graph carries a chain for, in graph order. */
export function listTraceableRecordIds(
  graph: FinancialRecordExplorerRecordGraphDto | null | undefined
): readonly string[] {
  return (graph?.nodes ?? [])
    .filter((node) => !node.nodeId.startsWith(RELATIONSHIP_NODE_PREFIX))
    .map((node) => node.nodeId);
}

function belongsToRecord(node: FinancialRecordExplorerGraphNodeDto, recordId: string): boolean {
  if (node.nodeId === recordId) {
    return true;
  }
  return node.nodeId.startsWith(`${RELATIONSHIP_NODE_PREFIX}${recordId}:`);
}

/**
 * A node is a placeholder when the read service had no relationship to describe.
 *
 * Both signals must agree. An empty href alone is not enough - a real relationship
 * may simply not be navigable - and a label matching the node type alone is not
 * enough either, since a slot could legitimately be named after itself.
 */
function isPlaceholder(node: FinancialRecordExplorerGraphNodeDto): boolean {
  return nonEmpty(node.href) === null && node.label.trim() === node.nodeType.trim();
}

function resolveStage(
  node: FinancialRecordExplorerGraphNodeDto,
  recordId: string
): ReportingTraceStage | null {
  const mapped = NODE_TYPE_STAGES[node.nodeType.trim().toLowerCase()];
  if (mapped) {
    return mapped;
  }

  // The row node is the originating record itself; its node type is the record type,
  // which varies by domain and is not a relationship slot.
  if (node.nodeId === recordId) {
    return "Source";
  }

  return normalizeReportingTraceStage(node.nodeType);
}

function titleCase(value: string): string {
  return value
    .split(/[-_\s]+/)
    .filter((part) => part.length > 0)
    .map((part) => `${part.charAt(0).toUpperCase()}${part.slice(1)}`)
    .join(" ");
}

function nonEmpty(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? "";
  return trimmed.length > 0 ? trimmed : null;
}
