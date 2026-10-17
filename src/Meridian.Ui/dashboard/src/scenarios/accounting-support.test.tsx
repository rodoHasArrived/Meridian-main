import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { getReconciliationStatementRuns } from "@/lib/api";
import {
  getReconciliationOpenCases,
  getReconciliationQueueStatus,
  getReconciliationTaxonomy
} from "@/lib/api/reconciliation-readiness.api";
import { getStatementRunBreaks, getStatementRunValidation } from "@/lib/api/statement-run-detail.api";
import { resolveDevFixture } from "@/lib/dev-fixtures";
import {
  RECONCILIATION_API_ENDPOINTS,
  reconciliationStatementRunValidationEndpoint
} from "@/lib/workstation-endpoints";
import { accountingPayload } from "@/scenarios/accounting-payload";
import { ReconciliationReadinessPanel } from "@/screens/accounting-screen.reconciliation-readiness";
import { requireFirst } from "@/test/fixtures";
import { installScenario } from "@/test/scenarios";
import type { StatementRunSummary } from "@/types";

const baselineRuns = resolveDevFixture<StatementRunSummary[]>(RECONCILIATION_API_ENDPOINTS.statementRuns) ?? [];
const knownRunIds = [...new Set([
  ...accountingPayload.reconciliationQueue.map((run) => run.runId),
  ...baselineRuns.map((run) => run.runId)
])];

describe("accounting reconciliation supporting scenarios", () => {
  it("mounts the production readiness panel in strict mode without claiming account sign-off", async () => {
    installScenario("accounting.normal");
    render(<ReconciliationReadinessPanel />);
    expect(await screen.findByText("No accounts reported a reconciliation queue state.")).toBeVisible();
    expect(screen.getByText("No open reconciliation cases.")).toBeVisible();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("supports only retained sample run identifiers and reports missing validation as blocked", async () => {
    const scenario = installScenario("accounting.normal");
    expect(knownRunIds.length).toBeGreaterThan(0);
    for (const runId of knownRunIds) {
      const validation = await getStatementRunValidation(runId);
      expect(validation.runId).toBe(runId);
      expect(validation.isBlocked).toBe(true);
      expect(requireFirst(validation.issues, "sample validation issues")).toMatchObject({
        severity: 0,
        code: "SAMPLE_VALIDATION_UNAVAILABLE",
        message: expect.stringContaining("Sample scenario: no retained")
      });
      await expect(getStatementRunBreaks(runId)).resolves.toEqual([]);
    }
    expect(scenario.unexpectedRequests).toEqual([]);
  });

  it("keeps statement runs, readiness accounts, and casework empty in the empty scenario", async () => {
    installScenario("accounting.empty");
    await expect(getReconciliationStatementRuns()).resolves.toEqual([]);
    await expect(getReconciliationQueueStatus()).resolves.toEqual([]);
    await expect(getReconciliationOpenCases()).resolves.toEqual([]);
    await expect(getReconciliationTaxonomy()).resolves.toEqual({ version: 1, rootCauses: [], resolutionCodes: [] });
  });

  it.each([
    ["accounting.forbidden", 403],
    ["accounting.failed", 500]
  ] as const)("preserves %s on all supporting reads", async (id, status) => {
    installScenario(id);
    const runId = requireFirst(accountingPayload.reconciliationQueue, "accounting reconciliation queue").runId;
    await expect(getReconciliationStatementRuns()).rejects.toMatchObject({ status });
    await expect(getReconciliationQueueStatus()).rejects.toMatchObject({ status });
    await expect(getReconciliationOpenCases()).rejects.toMatchObject({ status });
    await expect(getReconciliationTaxonomy()).rejects.toMatchObject({ status });
    await expect(getStatementRunValidation(runId)).rejects.toMatchObject({ status });
    await expect(getStatementRunBreaks(runId)).rejects.toMatchObject({ status });
  });

  it("still rejects an arbitrary statement-run identifier in strict mode", async () => {
    const scenario = installScenario("accounting.normal");
    const path = reconciliationStatementRunValidationEndpoint("unregistered-run");
    await expect(getStatementRunValidation("unregistered-run")).rejects.toThrow(path);
    expect(scenario.unexpectedRequests).toHaveLength(1);
    expect(() => scenario.restore()).toThrow(path);
  });
});
