import { describe, expect, it } from "vitest";
import { resolveDevFixture } from "@/lib/dev-fixtures";
import { accountingPayload } from "@/scenarios/accounting-payload";
import type {
  CorporateActionInboxResponse,
  LedgerBook,
  OperatorScopePreference,
  OperatorSurfaceVisit,
  ProviderCapabilityMatrixResponse,
  QualityDashboardResponse,
  SecurityMasterQualityReport
} from "@/types";
import type {
  ReconciliationCaseSummary,
  ReconciliationQueueAccountStatus,
  ReconciliationTaxonomySnapshot
} from "@/types/reconciliation-readiness.types";
import {
  STATEMENT_BREAK_TYPE_LABELS,
  STATEMENT_VALIDATION_SEVERITY_LABELS,
  type StatementRunBreak,
  type StatementRunValidation
} from "@/types/statement-run-detail.types";

const statementRuns = "/api/workstation/reconciliation/statement-runs";

describe("workstation surface dev fixtures", () => {
  it("serves reconciliation readiness that agrees with the sample workspace's open break", () => {
    const sampleBreak = accountingPayload.breakQueue.find((item) => item.breakId === "run-42:cash");
    expect(sampleBreak).toBeDefined();

    const queue = resolveDevFixture<ReconciliationQueueAccountStatus[]>("/api/workstation/reconciliation/queue-status");
    const cases = resolveDevFixture<ReconciliationCaseSummary[]>("/api/workstation/reconciliation/cases");
    const breaks = resolveDevFixture<StatementRunBreak[]>(`${statementRuns}/run-42/breaks`);

    expect(queue?.reduce((count, account) => count + account.unresolvedBreakCount, 0)).toBe(1);
    expect(queue?.every((account) => account.signOffReady === (account.unresolvedBreakCount === 0))).toBe(true);
    expect(cases).toHaveLength(1);
    expect(cases?.[0]).toMatchObject({ status: "Open", createdAtUtc: sampleBreak?.detectedAt, signedOffBy: null });
    expect(breaks).toHaveLength(1);
    expect(breaks?.[0]).toMatchObject({
      breakId: sampleBreak?.breakId,
      runId: sampleBreak?.runId,
      delta: sampleBreak?.variance,
      status: sampleBreak?.status,
      category: sampleBreak?.category,
      toleranceBreached: true
    });
    expect(Math.abs(breaks?.[0].delta ?? 0)).toBeGreaterThan(breaks?.[0].tolerance ?? Infinity);
    expect(breaks?.[0].importId).toBe(cases?.[0].importId);
  });

  it("uses wire ordinals the statement-run contract recognises", () => {
    const validation = resolveDevFixture<StatementRunValidation>(`${statementRuns}/run-42/validation`);
    const breaks = resolveDevFixture<StatementRunBreak[]>(`${statementRuns}/run-42/breaks`);

    expect(validation?.runId).toBe("run-42");
    expect(validation?.issues.length).toBeGreaterThan(0);
    for (const issue of validation?.issues ?? []) {
      expect(STATEMENT_VALIDATION_SEVERITY_LABELS[issue.severity ?? -1]).toBeDefined();
    }
    // isBlocked is the server's decision; the fixture must not contradict its own issue list.
    expect(validation?.isBlocked).toBe((validation?.issues ?? []).some((issue) => (issue.severity ?? 0) >= 2));
    expect(STATEMENT_BREAK_TYPE_LABELS[breaks?.[0].breakType ?? -1]).toBe("Cash balance mismatch");
  });

  it("leaves statement runs the sample desk does not know unsupported", () => {
    expect(resolveDevFixture(`${statementRuns}/run-unknown/breaks`)).toBeUndefined();
    expect(resolveDevFixture(`${statementRuns}/run-unknown/validation`)).toBeUndefined();
  });

  it("serves an active taxonomy with unique codes", () => {
    const taxonomy = resolveDevFixture<ReconciliationTaxonomySnapshot>("/api/workstation/reconciliation/break-queue/taxonomy");
    for (const values of [taxonomy?.rootCauses ?? [], taxonomy?.resolutionCodes ?? []]) {
      expect(values.length).toBeGreaterThan(0);
      expect(new Set(values.map((value) => value.code)).size).toBe(values.length);
      expect(values.every((value) => value.isActive)).toBe(true);
    }
  });

  it("answers operator preference reads as a first session and echoes the visited surface", () => {
    expect(resolveDevFixture<OperatorScopePreference>("/api/workstation/operator/scope-preference"))
      .toEqual({ defaultScope: null, updatedAt: null });
    expect(resolveDevFixture<OperatorSurfaceVisit>("/api/workstation/operator/visits/reconciliation-breaks"))
      .toEqual({ surface: "reconciliation-breaks", previousVisitAt: null, visitedAt: null });
  });

  it("serves the ledger books the accounting configuration fixture declares, ignoring the query", () => {
    const books = resolveDevFixture<LedgerBook[]>("/api/ledger/books?fundProfileId=default-fund");
    expect(books?.map((book) => book.ledgerBookId)).toEqual(["ledger-book-default"]);
    expect(books?.[0].fundProfileId).toBe("default-fund");
  });

  it("serves a capability matrix whose cells are consistent with each provider's declarations", () => {
    const matrix = resolveDevFixture<ProviderCapabilityMatrixResponse>("/api/providers/capability-matrix");
    expect(matrix?.providers.length).toBeGreaterThan(1);
    for (const provider of matrix?.providers ?? []) {
      expect(provider.cells.map((cell) => cell.instrumentType)).toEqual(matrix?.instrumentTypes);
      for (const cell of provider.cells) {
        expect(cell.supported).toBe(provider.declaredInstrumentTypes.includes(cell.instrumentType));
        const anySurface = cell.stream || cell.backfill || cell.corporateActions || cell.symbolSearch || cell.optionsChain;
        expect(anySurface).toBe(cell.supported);
        if (cell.stream) expect(provider.surfaces.streaming).toBe(true);
        if (cell.backfill) expect(provider.surfaces.historical).toBe(true);
        if (cell.optionsChain) expect(provider.surfaces.optionsChain).toBe(true);
        if (cell.corporateActions) expect(provider.surfaces.corporateActions).toBe(true);
        if (cell.symbolSearch) expect(provider.surfaces.symbolSearch).toBe(true);
      }
    }
  });

  it("serves empty-but-valid answers where the sample desk has nothing to show", () => {
    expect(resolveDevFixture("/api/risk/escalations")).toEqual([]);
    expect(resolveDevFixture("/api/ledger/journal-automation/daily-mark-to-market-schedules")).toEqual([]);
    expect(resolveDevFixture<QualityDashboardResponse>("/api/quality/dashboard"))
      .toMatchObject({ composite: null, recentGaps: [], recentAnomalies: [] });
    const inbox = resolveDevFixture<CorporateActionInboxResponse>("/api/security-master/corporate-actions/inbox");
    expect(inbox?.stagedCount).toBe(inbox?.staged.length);
    expect(inbox).toMatchObject({ staged: [], errors: [], cases: [] });
  });

  it("serves a security master quality report whose count matches its findings", () => {
    const report = resolveDevFixture<SecurityMasterQualityReport>("/api/security-master/quality-report/latest");
    expect(report?.violationCount).toBe(report?.violations.length);
    expect(report?.securitiesScanned).toBeGreaterThanOrEqual(report?.violationCount ?? 0);
  });

  it("hands each caller its own copy", () => {
    const first = resolveDevFixture<ReconciliationCaseSummary[]>("/api/workstation/reconciliation/cases");
    expect(first).toHaveLength(1);
    if (first) first[0].status = "Mutated";
    expect(resolveDevFixture<ReconciliationCaseSummary[]>("/api/workstation/reconciliation/cases")?.[0].status).toBe("Open");
  });
});
