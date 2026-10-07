import type { AccountingWorkspaceResponse } from "../types";
import { RECONCILIATION_API_ENDPOINTS, WORKSTATION_API_ENDPOINTS } from "../lib/workstation-endpoints";
import { accountingPayload, type AccountingScenarioPayload } from "./accounting-payload";
import { createAccountingSupportRoutes } from "./accounting-support";
import type { ApiScenario, ScenarioResponse, ScenarioProblem } from "./types";

export { accountingPayload } from "./accounting-payload";

const emptyAccountingPayload: AccountingScenarioPayload = {
  ...accountingPayload,
  metrics: [],
  reconciliationQueue: [],
  breakQueue: [],
  closePlans: [],
  cashFlow: {
    totalCash: 0,
    totalLedgerCash: 0,
    netVariance: 0,
    totalFinancing: 0,
    runsWithCashSignals: 0,
    runsWithCashVariance: 0,
    tone: "default",
    summary: "No cash-flow signals are available for this accounting scope."
  }
};

function accountingScenario(
  state: string,
  response: ScenarioResponse<AccountingWorkspaceResponse | ScenarioProblem>,
  waitForTexts: string[]
): ApiScenario {
  return {
    id: `accounting.${state}`,
    previewPath: "/accounting/reconciliation",
    responses: [
      { method: "GET", path: WORKSTATION_API_ENDPOINTS.accounting, response },
      // Accounting reads this queue independently. Keep the state consistent
      // when that read replaces the workspace's embedded queue.
      { method: "GET", path: RECONCILIATION_API_ENDPOINTS.breakQueue, response: {
        ...response,
        body: "breakQueue" in response.body ? response.body.breakQueue : response.body
      } },
      ...createAccountingSupportRoutes(response, { empty: state === "empty" })
    ],
    waitForTexts
  };
}

// Readiness follows the payload, so changing a strategy name needs only one edit.
const populatedTexts = accountingPayload.reconciliationQueue.map((row) => row.strategyName);

export const accountingScenarios = {
  "accounting.normal": accountingScenario("normal", { status: 200, body: accountingPayload }, populatedTexts),
  "accounting.empty": accountingScenario("empty", { status: 200, body: emptyAccountingPayload }, [
    "No reconciliation runs are available for this accounting scope."
  ]),
  "accounting.delayed": accountingScenario("delayed", { status: 200, body: accountingPayload, delayMs: 1500 }, populatedTexts),
  "accounting.forbidden": accountingScenario("forbidden", {
    status: 403,
    body: { status: 403, title: "Forbidden", detail: "Accounting scenario access is forbidden." }
  }, ["Some workspace data is unavailable"]),
  "accounting.failed": accountingScenario("failed", {
    status: 500,
    body: { status: 500, title: "Accounting unavailable", detail: "Accounting scenario service failed." }
  }, ["Some workspace data is unavailable"])
} satisfies Record<string, ApiScenario>;
