import { resolveDevFixture } from "../lib/dev-fixtures";
import {
  RECONCILIATION_API_ENDPOINTS,
  reconciliationStatementRunBreaksEndpoint,
  reconciliationStatementRunValidationEndpoint
} from "../lib/workstation-endpoints";
import type { AccountingWorkspaceResponse, StatementRunSummary } from "../types";
import type {
  ReconciliationCaseSummary,
  ReconciliationQueueAccountStatus,
  ReconciliationTaxonomySnapshot
} from "../types/reconciliation-readiness.types";
import type { StatementRunBreak, StatementRunValidation } from "../types/statement-run-detail.types";
import { accountingPayload } from "./accounting-payload";
import type { ScenarioProblem, ScenarioResponse, ScenarioRoute } from "./types";

/** Exact supporting reads mounted by the accounting reconciliation preview. */
export function createAccountingSupportRoutes(
  response: ScenarioResponse<AccountingWorkspaceResponse | ScenarioProblem>,
  { empty = false }: { empty?: boolean } = {}
): ScenarioRoute[] {
  const baselineRuns = resolveDevFixture<StatementRunSummary[]>(RECONCILIATION_API_ENDPOINTS.statementRuns) ?? [];
  const workspace = "reconciliationQueue" in response.body ? response.body : undefined;
  const problem = "reconciliationQueue" in response.body ? undefined : response.body;
  const statementRuns: StatementRunSummary[] = empty ? [] : baselineRuns;
  const queueStatus: ReconciliationQueueAccountStatus[] = [];
  const openCases: ReconciliationCaseSummary[] = [];
  const taxonomy: ReconciliationTaxonomySnapshot = { version: 1, rootCauses: [], resolutionCodes: [] };

  function route<T>(path: string, body: T): ScenarioRoute<T | ScenarioProblem> {
    return {
      method: "GET",
      path,
      response: { ...response, body: problem ?? body }
    };
  }

  const routes: ScenarioRoute[] = [
    route(RECONCILIATION_API_ENDPOINTS.queueStatus, queueStatus),
    route(RECONCILIATION_API_ENDPOINTS.openCases, openCases),
    route(RECONCILIATION_API_ENDPOINTS.breakQueueTaxonomy, taxonomy),
    route(RECONCILIATION_API_ENDPOINTS.statementRuns, statementRuns)
  ];

  // Derive known identifiers from the shared payload and retained fixture list.
  // An arbitrary run ID remains an unexpected request in strict consumers.
  const knownRunIds = new Set([
    ...accountingPayload.reconciliationQueue.map((run) => run.runId),
    ...(workspace?.reconciliationQueue ?? []).map((run) => run.runId),
    ...baselineRuns.map((run) => run.runId)
  ]);
  for (const runId of knownRunIds) {
    const validation: StatementRunValidation = {
      runId,
      isBlocked: true,
      issues: [{
        issueId: `${runId}:sample-validation-unavailable`,
        severity: 0,
        code: "SAMPLE_VALIDATION_UNAVAILABLE",
        message: "Sample scenario: no retained statement-run validation is available.",
        recommendedAction: "Load retained backend validation before authorizing reconciliation."
      }]
    };
    const breaks: StatementRunBreak[] = [];
    routes.push(
      route(reconciliationStatementRunValidationEndpoint(runId), validation),
      route(reconciliationStatementRunBreaksEndpoint(runId), breaks)
    );
  }
  return routes;
}
