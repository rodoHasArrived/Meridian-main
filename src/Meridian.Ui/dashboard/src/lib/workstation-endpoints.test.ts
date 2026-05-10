import { describe, expect, it } from "vitest";
import {
  EXECUTION_API_ENDPOINTS,
  PROMOTION_API_ENDPOINTS,
  PORTFOLIO_API_ENDPOINTS,
  REPLAY_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINTS,
  WORKSTATION_API_ENDPOINT_TEMPLATES,
  executionAuditEndpoint,
  executionManualOverrideClearEndpoint,
  executionOrderCancelEndpoint,
  executionPositionCloseEndpoint,
  executionSessionCloseEndpoint,
  executionSessionEndpoint,
  executionSessionReplayEndpoint,
  portfolioHouseholdEndpoint,
  promotionEvaluateEndpoint,
  replayFilesEndpoint,
  replaySessionActionEndpoint,
  workstationEvidenceExportManifestEndpoint,
  workstationEvidenceGraphEndpoint,
  workstationEvidencePacketEndpoint,
  workstationEvidenceValidateEndpoint,
  workstationOperatorInboxEndpoint,
  workstationRunContinuityEndpoint,
  workstationRunHistoryEndpoint,
  workstationRunLedgerEndpoint,
  workstationRunLedgerJournalEndpoint,
  workstationRunReconciliationEndpoint,
  workstationRunReconciliationHistoryEndpoint,
  workstationRunReviewPacketEndpoint,
  workstationRunSweepsEndpoint,
  workstationRunTimelineEndpoint,
  workstationWorkflowSummaryEndpoint,
  workstationWorkflowPresetEndpoint,
  workstationWorkflowPresetPinEndpoint,
  workstationWorkflowPresetUsedEndpoint
} from "@/lib/workstation-endpoints";

describe("workstation API endpoint catalog", () => {
  it("keeps canonical workspace bootstrap endpoints in one shared catalog", () => {
    expect(WORKSTATION_API_ENDPOINTS).toMatchObject({
      session: "/api/workstation/session",
      strategy: "/api/workstation/strategy",
      trading: "/api/workstation/trading",
      tradingReadiness: "/api/workstation/trading/readiness",
      portfolio: "/api/workstation/portfolio",
      data: "/api/workstation/data",
      accounting: "/api/workstation/accounting",
      reporting: "/api/workstation/reporting",
      workflowSummary: "/api/workstation/workflow-summary",
      runHistory: "/api/workstation/runs/history",
      runTimeline: "/api/workstation/runs/timeline",
      runSweeps: "/api/workstation/runs/sweeps",
      evidenceSubjects: "/api/workstation/evidence/subjects",
      evidenceTemplates: "/api/workstation/evidence/templates"
    });
  });

  it("builds account-scoped operator inbox endpoints without changing the base contract", () => {
    expect(workstationOperatorInboxEndpoint()).toBe("/api/workstation/operator/inbox");
    expect(workstationOperatorInboxEndpoint("fund account/1")).toBe(
      "/api/workstation/operator/inbox?fundAccountId=fund%20account%2F1"
    );
  });

  it("builds workflow preset endpoints from the shared preset root", () => {
    expect(workstationWorkflowSummaryEndpoint()).toBe("/api/workstation/workflow-summary");
    expect(workstationWorkflowSummaryEndpoint({
      hasOperatingContext: true,
      operatingContext: "portfolio/review",
      fundProfileId: "fund / 1",
      fundDisplayName: "Core Fund"
    })).toBe(
      "/api/workstation/workflow-summary?hasOperatingContext=true&operatingContext=portfolio%2Freview&fundProfileId=fund+%2F+1&fundDisplayName=Core+Fund"
    );
    expect(workstationWorkflowPresetEndpoint()).toBe("/api/workstation/workflows/presets");
    expect(workstationWorkflowPresetEndpoint("preset / 1")).toBe("/api/workstation/workflows/presets/preset%20%2F%201");
    expect(workstationWorkflowPresetPinEndpoint("preset / 1")).toBe("/api/workstation/workflows/presets/preset%20%2F%201/pin");
    expect(workstationWorkflowPresetUsedEndpoint("preset / 1")).toBe("/api/workstation/workflows/presets/preset%20%2F%201/used");
  });

  it("builds run evidence endpoints and matching Settings templates", () => {
    expect(WORKSTATION_API_ENDPOINT_TEMPLATES).toMatchObject({
      runLedger: "/api/workstation/runs/{runId}/ledger",
      runContinuity: "/api/workstation/runs/{runId}/continuity",
      runReviewPacket: "/api/workstation/runs/{runId}/review-packet",
      runReconciliation: "/api/workstation/runs/{runId}/reconciliation"
    });
    expect(workstationRunLedgerEndpoint("run / 1")).toBe("/api/workstation/runs/run%20%2F%201/ledger");
    expect(workstationRunLedgerJournalEndpoint("run / 1")).toBe("/api/workstation/runs/run%20%2F%201/ledger/journal");
    expect(workstationRunContinuityEndpoint("run / 1")).toBe("/api/workstation/runs/run%20%2F%201/continuity");
    expect(workstationRunReviewPacketEndpoint("run / 1", "fund / 1")).toBe(
      "/api/workstation/runs/run%20%2F%201/review-packet?fundAccountId=fund+%2F+1"
    );
    expect(workstationRunReconciliationEndpoint("run / 1")).toBe("/api/workstation/runs/run%20%2F%201/reconciliation");
    expect(workstationRunReconciliationHistoryEndpoint("run / 1")).toBe(
      "/api/workstation/runs/run%20%2F%201/reconciliation/history"
    );
    expect(workstationRunHistoryEndpoint({ mode: "paper", status: "Ready", limit: 10 })).toBe(
      "/api/workstation/runs/history?mode=paper&status=Ready&limit=10"
    );
    expect(workstationRunTimelineEndpoint({ strategyId: "strategy / 1", limit: 5 })).toBe(
      "/api/workstation/runs/timeline?strategyId=strategy+%2F+1&limit=5"
    );
    expect(workstationRunSweepsEndpoint(20)).toBe("/api/workstation/runs/sweeps?limit=20");
  });

  it("builds evidence workbench subject endpoints from the shared evidence root", () => {
    expect(workstationEvidencePacketEndpoint("strategy/run", "run / 1")).toBe(
      "/api/workstation/evidence/subjects/strategy%2Frun/run%20%2F%201/packet"
    );
    expect(workstationEvidenceGraphEndpoint("strategy/run", "run / 1")).toBe(
      "/api/workstation/evidence/subjects/strategy%2Frun/run%20%2F%201/graph"
    );
    expect(workstationEvidenceValidateEndpoint("strategy/run", "run / 1")).toBe(
      "/api/workstation/evidence/subjects/strategy%2Frun/run%20%2F%201/validate"
    );
    expect(workstationEvidenceExportManifestEndpoint("strategy/run", "run / 1")).toBe(
      "/api/workstation/evidence/subjects/strategy%2Frun/run%20%2F%201/export-manifest"
    );
  });

  it("keeps execution, replay, and promotion endpoint builders encoded", () => {
    expect(EXECUTION_API_ENDPOINTS.sessions).toBe("/api/execution/sessions");
    expect(REPLAY_API_ENDPOINTS.start).toBe("/api/replay/start");
    expect(PROMOTION_API_ENDPOINTS.approve).toBe("/api/promotion/approve");
    expect(promotionEvaluateEndpoint("run / 1")).toBe("/api/promotion/evaluate/run%20%2F%201");
    expect(executionOrderCancelEndpoint("ord / 1")).toBe("/api/execution/orders/ord%20%2F%201/cancel");
    expect(executionPositionCloseEndpoint("BRK/B")).toBe("/api/execution/positions/BRK%2FB/close");
    expect(executionSessionEndpoint("sess / 1")).toBe("/api/execution/sessions/sess%20%2F%201");
    expect(executionSessionCloseEndpoint("sess / 1")).toBe("/api/execution/sessions/sess%20%2F%201/close");
    expect(executionSessionReplayEndpoint("sess / 1")).toBe("/api/execution/sessions/sess%20%2F%201/replay");
    expect(executionAuditEndpoint(12)).toBe("/api/execution/audit?take=12");
    expect(executionManualOverrideClearEndpoint("override / 1")).toBe(
      "/api/execution/controls/manual-overrides/override%20%2F%201/clear"
    );
    expect(replayFilesEndpoint("ES / M6")).toBe("/api/replay/files?symbol=ES+%2F+M6");
    expect(replaySessionActionEndpoint("rep / 1", "seek")).toBe("/api/replay/rep%20%2F%201/seek");
    expect(PORTFOLIO_API_ENDPOINTS.household).toBe("/api/portfolio/household");
    expect(portfolioHouseholdEndpoint("alpaca paper")).toBe("/api/portfolio/household?provider=alpaca+paper");
  });
});
