import { describe, expect, it } from "vitest";
import {
  appendRouteQuery,
  evidenceWorkbenchPath,
  legacyWorkspaceRedirect,
  normalizeLocalWorkstationRoute,
  normalizeWorkspacePath,
  resolveWorkstationRouteBreadcrumbLabel,
  settingsProviderAdvancedRoute,
  settingsProviderConnectionRoute,
  settingsProviderSetupRoute,
  WORKSPACES,
  WORKSTATION_PAGE_TAG_ROUTES,
  WORKSTATION_ROUTE_CATALOG,
  workflowTargetPath,
  workstationRoute,
  workstationRouteWithHash,
  workstationRouteWithQuery,
  workspaceForKey,
  workspacePath
} from "@/lib/workspace";

describe("workspace metadata", () => {
  it("defines the canonical workstation navigation order", () => {
    expect(WORKSPACES.map((workspace) => workspace.label)).toEqual([
      "Trading",
      "Portfolio",
      "Accounting",
      "Reporting",
      "Strategy",
      "Data",
      "Settings"
    ]);
  });

  it("routes every canonical workspace by key", () => {
    expect(WORKSPACES.map((workspace) => workspacePath(workspace.key))).toEqual([
      "/trading",
      "/portfolio",
      "/accounting",
      "/reporting",
      "/strategy",
      "/data",
      "/settings"
    ]);
  });

  it("exposes typed workstation route catalog helpers", () => {
    expect(workstationRoute("tradingReadiness")).toBe("/trading/readiness");
    expect(workstationRoute("strategyFormulaWorkbenchLegacy")).toBe("/strategy/formula-workbench");
    expect(workstationRoute("strategyLab")).toBe("/strategy/lab");
    expect(workstationRoute("portfolioAssetDetail")).toBe("/portfolio/asset-detail");
    expect(workstationRoute("portfolioCashLadder")).toBe("/portfolio/cash-ladder");
    expect(workstationRoute("portfolioFamilyOffice")).toBe("/portfolio/family-office");
    expect(workstationRoute("accountingOperationsContinuity")).toBe("/accounting/operations-continuity");
    expect(workstationRoute("accountingEntitySetup")).toBe("/accounting/entity-setup");
    expect(workstationRoute("accountingExceptions")).toBe("/accounting/exceptions");
    expect(workstationRoute("accountingExternalGlReconciliation")).toBe("/accounting/reconciliation/external-gl");
    expect(workstationRoute("reportingReportBuilder")).toBe("/reporting/report-builder");
    expect(workstationRoute("reportingScheduled")).toBe("/reporting/scheduled");
    expect(workstationRoute("reportingRunStatus")).toBe("/reporting/run-status");
    expect(workstationRoute("reportingOperationsRecord")).toBe("/reporting/operations-record");
    expect(workstationRoute("reportingExports")).toBe("/reporting/exports");
    expect(workstationRoute("reportingGovernance")).toBe("/reporting/governance");
    expect(workstationRoute("dataExports")).toBe("/data/exports");
    expect(workstationRoute("dataImport")).toBe("/data/import");
    expect(workstationRoute("dataQuery")).toBe("/data/query");
    expect(workstationRoute("settingsAccess")).toBe("/settings/access");
    expect(workstationRoute("settingsAccountingSystems")).toBe("/settings/accounting-systems");
    expect(workstationRoute("settingsProviders")).toBe("/settings/providers");
    expect(workstationRoute("settingsDiagnostics")).toBe("/settings/diagnostics");
    expect(workstationRoute("settingsDiagnosticsAdvanced")).toBe("/settings/diagnostics/advanced");
    expect(workstationRoute("settingsFeatureCoverage")).toBe("/settings/feature-coverage");
    expect(workstationRoute("settingsAlpacaProviderSetup")).toBe("/settings#alpaca-provider-setup");
    expect(workstationRouteWithQuery("dataQuotes", { symbol: "BRK/B", provider: "Alpaca", empty: null })).toBe(
      "/data/quotes?symbol=BRK%2FB&provider=Alpaca"
    );
    expect(appendRouteQuery(WORKSTATION_ROUTE_CATALOG.reportingEvidence, { subjectKind: "run", subjectId: "run 1" }))
      .toBe("/reporting/evidence?subjectKind=run&subjectId=run%201");
    expect(workstationRouteWithHash("settings", "#backend-capability-coverage")).toBe(
      "/settings#backend-capability-coverage"
    );
    expect(settingsProviderConnectionRoute("alpaca-paper")).toBe("/settings#provider-alpaca-paper-connection");
    expect(settingsProviderSetupRoute("Alpaca Paper")).toBe("/settings/providers/alpaca%20paper/setup");
    expect(settingsProviderAdvancedRoute("Polygon")).toBe("/settings/providers/polygon/advanced");
  });

  it("normalizes legacy workspace URLs to canonical roots", () => {
    expect(normalizeWorkspacePath("/")).toBe("trading");
    expect(normalizeWorkspacePath("/overview")).toBe("trading");
    expect(normalizeWorkspacePath("/research/run-library")).toBe("strategy");
    expect(normalizeWorkspacePath("/data-operations/backfills")).toBe("data");
    expect(normalizeWorkspacePath("/governance/security-master")).toBe("accounting");
    expect(normalizeWorkspacePath("/data/security-master")).toBe("accounting");
  });

  it("preserves legacy suffix, query, and hash when building redirects", () => {
    expect(legacyWorkspaceRedirect("/data-operations/backfills", "?provider=alpaca", "#queue")).toBe(
      "/data/operations?provider=alpaca#queue"
    );
    expect(legacyWorkspaceRedirect("/data/backfills", "?provider=alpaca", "#queue")).toBe(
      "/data/operations?provider=alpaca#queue"
    );
    expect(legacyWorkspaceRedirect("/data/security-master/identity", "?query=GS", "#conflicts")).toBe(
      "/accounting/security-master/identity?query=GS#conflicts"
    );
    expect(legacyWorkspaceRedirect("/governance/reconciliation")).toBe("/accounting/reconciliation");
    expect(legacyWorkspaceRedirect("/trading")).toBeNull();
  });

  it("redirects consolidated screen routes into their host screens with scope preserved", () => {
    expect(legacyWorkspaceRedirect("/accounting/trial-balance")).toBe(
      "/accounting/ledger?view=trial-balance"
    );
    expect(legacyWorkspaceRedirect("/accounting/trial-balance", "?runId=run-42", "#section")).toBe(
      "/accounting/ledger?runId=run-42&view=trial-balance#section"
    );
    expect(legacyWorkspaceRedirect("/strategy/formula-workbench")).toBe(
      "/strategy/quant-lab?view=formulas"
    );
    expect(legacyWorkspaceRedirect("/strategy/formula-workbench", "?draft=1")).toBe(
      "/strategy/quant-lab?draft=1&view=formulas"
    );
    expect(normalizeLocalWorkstationRoute("/accounting/trial-balance?runId=run-42")).toBe(
      "/accounting/ledger?runId=run-42&view=trial-balance"
    );
  });

  it("redirects retired evidence mounts to the canonical reporting evidence workbench", () => {
    expect(legacyWorkspaceRedirect("/accounting/evidence")).toBe("/reporting/evidence");
    expect(legacyWorkspaceRedirect("/accounting/evidence", "?subjectKind=run&subjectId=run-1", "#packet")).toBe(
      "/reporting/evidence?subjectKind=run&subjectId=run-1#packet"
    );
    expect(legacyWorkspaceRedirect("/accounting/evidence/detail", "?evidenceId=bank-statement")).toBe(
      "/reporting/evidence?subjectKind=evidence&subjectId=bank-statement"
    );
    expect(legacyWorkspaceRedirect("/accounting/evidence/detail")).toBe("/reporting/evidence");
    expect(legacyWorkspaceRedirect("/data/evidence")).toBe("/reporting/evidence");
    expect(legacyWorkspaceRedirect("/data/evidence", "?subjectKind=import-run&subjectId=imp-9")).toBe(
      "/reporting/evidence?subjectKind=import-run&subjectId=imp-9"
    );
  });

  it("returns workspace summaries for canonical keys", () => {
    expect(workspaceForKey("reporting")).toMatchObject({
      label: "Reporting",
      status: "Review"
    });
  });

  it("resolves route breadcrumb labels from the centralized workstation route registry", () => {
    expect(resolveWorkstationRouteBreadcrumbLabel("/reporting/operations-record", workspaceForKey("reporting"))).toBe(
      "Operations Record"
    );
    expect(resolveWorkstationRouteBreadcrumbLabel("/accounting/journal-entries/detail", workspaceForKey("accounting")))
      .toBe("Journal Entries / Detail");
    expect(resolveWorkstationRouteBreadcrumbLabel("/accounting/approvals/inbox", workspaceForKey("accounting")))
      .toBe("Approvals / Inbox");
    expect(resolveWorkstationRouteBreadcrumbLabel("/accounting/exceptions", workspaceForKey("accounting")))
      .toBe("Exceptions");
    expect(resolveWorkstationRouteBreadcrumbLabel("/accounting/configure", workspaceForKey("accounting")))
      .toBe("Configure");
    expect(resolveWorkstationRouteBreadcrumbLabel("/reporting/report-packs", workspaceForKey("reporting")))
      .toBe("Report Packs");
    expect(resolveWorkstationRouteBreadcrumbLabel("/workstation/data/quotes", workspaceForKey("data"))).toBe("Quotes");
    expect(resolveWorkstationRouteBreadcrumbLabel("/portfolio/custom-beta-route", workspaceForKey("portfolio"))).toBe(
      "Custom Beta Route"
    );
    expect(resolveWorkstationRouteBreadcrumbLabel("/settings/accounting-systems", workspaceForKey("settings"))).toBe(
      "Accounting Systems"
    );
  });

  it("builds encoded Evidence Workbench subject routes", () => {
    expect(evidenceWorkbenchPath("strategy-run", "run 1/A")).toBe(
      "/reporting/evidence?subjectKind=strategy-run&subjectId=run%201%2FA"
    );
    expect(evidenceWorkbenchPath("report pack", "current")).toBe(
      "/reporting/evidence?subjectKind=report%20pack&subjectId=current"
    );
  });

  it("maps backend workflow targets to browser workstation routes", () => {
    expect(workflowTargetPath("Backtest", "strategy")).toBe("/strategy");
    expect(workflowTargetPath("EvidenceWorkbench", "strategy")).toBe("/reporting/evidence");
    expect(workflowTargetPath("EvidenceWorkbench:accounting-record/accounting-record-2026-05", "accounting"))
      .toBe("/reporting/evidence?subjectKind=accounting-record&subjectId=accounting-record-2026-05");
    expect(workflowTargetPath(" EvidenceWorkbench:strategy-run/run 1/A ", "strategy"))
      .toBe("/reporting/evidence?subjectKind=strategy-run&subjectId=run%201%2FA");
    expect(workflowTargetPath("TradingShell", "trading")).toBe("/trading");
    expect(workflowTargetPath("ResearchShell", null)).toBe("/strategy");
    expect(workflowTargetPath("DataOperationsShell", null)).toBe("/data");
    expect(workflowTargetPath("GovernanceShell", null)).toBe("/accounting");
    expect(workflowTargetPath("TradingReadiness", "trading")).toBe("/trading/readiness");
    expect(workflowTargetPath("TradingReadinessConsole", "trading")).toBe("/trading/readiness");
    expect(workflowTargetPath("RunRisk", "trading")).toBe("/trading/readiness");
    expect(workflowTargetPath("OperationsContinuity", "accounting")).toBe("/accounting/operations-continuity");
    expect(workflowTargetPath("OperationsClose", "accounting")).toBe("/accounting/operations-continuity");
    expect(workflowTargetPath("FundExceptionWorkbench", "accounting")).toBe("/accounting/exceptions");
    expect(workflowTargetPath("FundReconciliation", "accounting")).toBe("/accounting/reconciliation");
    expect(workflowTargetPath("FundTrialBalance", "accounting")).toBe("/accounting/ledger");
    expect(workflowTargetPath("FundAuditTrail", "accounting")).toBe("/accounting");
    expect(workflowTargetPath("FundReportPack", "reporting")).toBe("/reporting/report-packs");
    expect(workflowTargetPath("ReportLineProvenanceExplorer", "reporting")).toBe("/reporting/evidence");
    expect(workflowTargetPath("ProviderTrust", "data")).toBe("/data/providers");
    expect(workflowTargetPath("SecurityMaster", "data")).toBe("/accounting/security-master");
    expect(workflowTargetPath("UnknownTag", "research")).toBe("/strategy");
    expect(workflowTargetPath("UnknownTag", "data-operations")).toBe("/data");
    expect(workflowTargetPath("UnknownTag", "data")).toBe("/data");
    expect(workflowTargetPath(null, null)).toBe("/trading");
  });

  it("keeps the browser workflow target catalog explicit for shared backend page tags", () => {
    expect(Object.keys(WORKSTATION_PAGE_TAG_ROUTES).sort()).toEqual([
      "AccountPortfolio",
      "AccountingShell",
      "Backfill",
      "Backtest",
      "BrokerageSync",
      "CapitalAccountWorkbench",
      "DataOperationsShell",
      "DataShell",
      "EvidenceWorkbench",
      "FundAccountingConfigure",
      "FundAuditTrail",
      "FundExceptionWorkbench",
      "FundJournalEntryWorkbench",
      "FundReconciliation",
      "FundReportPack",
      "FundStructureSetup",
      "FundTrialBalance",
      "GovernanceShell",
      "OperationsClose",
      "OperationsContinuity",
      "PortfolioFamilyOffice",
      "PortfolioShell",
      "ProviderHealth",
      "ProviderTrust",
      "ReportLineProvenanceExplorer",
      "ReportPackApproval",
      "ReportingShell",
      "ResearchShell",
      "RunRisk",
      "SecurityMaster",
      "SettingsShell",
      "StrategyRuns",
      "StrategyShell",
      "TradingReadiness",
      "TradingReadinessConsole",
      "TradingShell"
    ]);
  });

  it("normalizes only local workstation routes for cross-screen workflow links", () => {
    expect(normalizeLocalWorkstationRoute("/workstation/governance/reconciliation?runId=run-1#cash")).toBe(
      "/accounting/reconciliation?runId=run-1#cash"
    );
    expect(normalizeLocalWorkstationRoute("/workstation/data/security-master/identity?query=GS#conflicts")).toBe(
      "/accounting/security-master/identity?query=GS#conflicts"
    );
    expect(normalizeLocalWorkstationRoute("/data/providers")).toBe("/data/providers");
    expect(normalizeLocalWorkstationRoute("/api/workstation/operator/inbox")).toBeNull();
    expect(normalizeLocalWorkstationRoute("//example.test/data")).toBeNull();
    expect(normalizeLocalWorkstationRoute("https://example.test/data")).toBeNull();
    expect(normalizeLocalWorkstationRoute("/unknown/path")).toBeNull();
  });
});
