import { act, renderHook, waitFor } from "@testing-library/react";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it, vi } from "vitest";
import * as workstationApi from "@/lib/api";
import { createApiErrorFromResponseBody } from "@/lib/api-errors";
import {
  DATA_BACKFILL_DETAIL_PANEL_ID,
  DATA_BACKFILL_ROUTE_FOCUS_CARD_ID,
  DATA_EXPORT_DETAIL_PANEL_ID,
  buildBackfillSection,
  buildBackfillDialogState,
  buildBackfillProviderOptions,
  buildBackfillNarrative,
  buildBackfillRequest,
  buildBackfillLiveProgressState,
  buildBackfillResultCardState,
  buildBackfillTriggerState,
  buildDataUploadPanelState,
  buildDataUploadTemplateCsv,
  buildDataLoadingState,
  buildDataPresentationState,
  buildExportSection,
  formatProviderReasonLabel,
  buildProviderRow,
  buildProviderSection,
  buildPlaidInstitutionSearchState,
  buildProviderSetupDialogState,
  buildProviderSetupSuccessMetadata,
  buildProviderSetupSuccessActions,
  buildRouteFocusCardState,
  buildSelectedExportDetail,
  buildSelectedProviderDetail,
  clearProviderSetupCredentials,
  buildSelectedBackfillDetail,
  defaultDataUploadTemplateCatalog,
  resolveDataWorkstream,
  resolveSelectedDataUploadTemplate,
  resolveSelectedProvider,
  resolveSelectedBackfill,
  resolveSelectedExport,
  useDataViewModel,
  validateBackfillForm,
  validateProviderSetupForm,
  DATA_PROVIDER_DETAIL_PANEL_ID
} from "@/screens/data-screen.view-model";
import {
  buildCorporateActionChipState,
  buildCorporateActionLifecycleState,
  buildSecurityMasterWorkspaceState
} from "@/screens/data-screen.security-master";
import type { CorporateActionDescriptor } from "@/types";
import type {
  BackfillProgressResponse,
  BackfillPreviewResult,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  DataBackfillRecord,
  DataExportRecord,
  DataProviderRecord,
  DataWorkspaceResponse,
  ProviderConnectionRow,
  ProviderReadinessSummary,
  ProviderRoutingBinding,
  ProviderRoutingConnection,
  ProviderRoutingTrustSnapshot
} from "@/types";

const backfills: DataBackfillRecord[] = [
  {
    jobId: "BF-1042",
    scope: "US equities / 30d",
    provider: "Databento",
    status: "Running",
    progress: "62%",
    updatedAt: "2m ago"
  },
  {
    jobId: "BF-1044",
    scope: "Options chains / 7d",
    provider: "Databento",
    status: "Review",
    progress: "95%",
    updatedAt: "5m ago"
  }
];

const preview: BackfillPreviewResult = {
  provider: "polygon",
  providerDisplayName: "Polygon",
  symbols: [
    { symbol: "AAPL", estimatedBars: 600, hasMarketHoursData: true, notes: [] },
    { symbol: "MSFT", estimatedBars: 600, hasMarketHoursData: true, notes: [] }
  ],
  from: "2024-01-01",
  to: "2024-01-31",
  totalDays: 31,
  estimatedTradingDays: 21,
  estimatedDurationSeconds: 5,
  notes: []
};

const completedBackfill: BackfillTriggerResult = {
  success: true,
  provider: "polygon",
  symbols: ["AAPL", "MSFT"],
  from: "2024-01-01",
  to: "2024-01-31",
  barsWritten: 1200,
  startedUtc: "2024-01-31T10:00:00Z",
  completedUtc: "2024-01-31T10:00:05Z",
  error: null
};

const providers: DataProviderRecord[] = [
  {
    providerId: "polygon",
    displayName: "Polygon.io",
    provider: "Polygon",
    status: "Healthy",
    capability: "Streaming equities",
    latency: "18ms p50",
    note: "Realtime subscriptions are stable.",
    trustScore: "98%",
    signalSource: "Provider heartbeat",
    reasonCode: "TRUST_OK",
    recommendedAction: "Keep provider active.",
    gateImpact: "No gate impact"
  }
];

const alpacaProvider: DataProviderRecord = {
  providerId: "alpaca",
  displayName: "Alpaca",
  provider: "Alpaca",
  status: "Healthy",
  capability: "Historical bars",
  latency: "24ms p50",
  note: "Configured with paper API keys.",
  trustScore: "97%",
  signalSource: "Provider heartbeat",
  reasonCode: "TRUST_OK",
  recommendedAction: "Keep provider active.",
  gateImpact: "No gate impact"
};

const exports: DataExportRecord[] = [
  {
    exportId: "EX-2201",
    profile: "python-pandas",
    target: "strategy pack",
    status: "Ready",
    rows: "124k",
    updatedAt: "4m ago"
  }
];

const polygonConnection: ProviderConnectionRow = {
  providerId: "polygon",
  displayName: "Polygon.io",
  capability: "Data",
  credentialState: "Verified",
  credentialSource: "LocalEncryptedStore",
  verificationState: "Verified",
  health: "Warning",
  fallbackActive: true,
  lastVerifiedAt: "2026-05-20T18:20:00Z",
  lastSuccessfulAt: "2026-05-20T18:25:00Z",
  lastFailureAt: "2026-05-20T17:00:00Z",
  lastError: "Rate-limit pressure is elevated.",
  maskedKeyPreview: "pk_live_****7F3A",
  environment: "live",
  externalAccountId: "acct-provider-01",
  affectedWorkflows: ["Strategy", "Backfill"],
  recommendedAction: "Verify credentials before routing dependent workflows.",
  actionHref: "/settings#provider-polygon",
  credentialFields: [
    {
      name: "ApiKey",
      label: "API key",
      required: true,
      inputKind: "Password",
      placeholder: "POLYGON_API_KEY",
      helpText: "Stored in Meridian's encrypted local provider store and masked after save."
    }
  ],
  environmentOptions: []
};

const polygonRoutingConnection: ProviderRoutingConnection = {
  connectionId: "polygon",
  providerFamilyId: "polygon",
  displayName: "Polygon.io",
  connectionType: "Data",
  connectionMode: "Live",
  enabled: true,
  credentialReference: "local:polygon",
  institutionId: null,
  externalAccountId: null,
  scope: null,
  tags: ["market-data"],
  description: "Polygon market data",
  productionReady: true
};

const polygonRoutingBinding: ProviderRoutingBinding = {
  bindingId: "binding-polygon-bars",
  capability: "HistoricalBars",
  connectionId: "polygon",
  target: null,
  priority: 10,
  enabled: true,
  failoverConnectionIds: ["yahoo"],
  safetyModeOverride: null,
  notes: null
};

const polygonTrustSnapshot: ProviderRoutingTrustSnapshot = {
  connectionId: "polygon",
  providerFamilyId: "polygon",
  score: 82,
  isHealthy: false,
  healthStatus: "Warning",
  isProductionReady: true,
  isCertificationFresh: true,
  signals: ["fallback-active"],
  decision: null
};

const providerReadiness: ProviderReadinessSummary = {
  asOf: "2026-06-02T16:50:00Z",
  status: "Blocked",
  totalProviders: 2,
  readyProviders: 1,
  reviewProviders: 0,
  degradedProviders: 0,
  blockedProviders: 1,
  summary: "1 provider blocks dependent workflows.",
  recommendedAction: "Repair Plaid credentials before routing accounting evidence workflows.",
  providers: [
    {
      providerId: "plaid",
      displayName: "Plaid",
      capability: "Data",
      status: "Blocked",
      credentialState: "Missing",
      credentialSource: "None",
      verificationState: "NotVerified",
      connectionHealth: "Warning",
      isEnabled: true,
      isConnected: false,
      fallbackActive: false,
      degradationScore: null,
      lastVerifiedAt: null,
      lastSuccessfulAt: null,
      lastFailureAt: null,
      lastError: "Sandbox client credentials have not been configured.",
      maskedKeyPreview: null,
      environment: "sandbox",
      externalAccountId: null,
      affectedWorkflows: ["Accounting evidence", "Brokerage sync"],
      recommendedAction: "Connect Plaid sandbox credentials before bank-account evidence can be retained.",
      actionHref: "/data/providers",
      evidence: [
        {
          kind: "Credential",
          label: "Credential",
          status: "Blocked",
          detail: "Required Plaid client fields are missing."
        }
      ],
      recoveryActions: [
        {
          actionId: "provider.plaid.open-setup",
          label: "Open setup",
          target: "/data/providers",
          requiresMutation: false
        }
      ],
      credentialFields: [
        {
          name: "ClientId",
          label: "Client ID",
          required: true,
          inputKind: "Password",
          placeholder: "PLAID_CLIENT_ID",
          helpText: "Used server-side to create Plaid link tokens and retain bank evidence."
        },
        {
          name: "Secret",
          label: "Secret",
          required: true,
          inputKind: "Password",
          placeholder: "PLAID_SECRET",
          helpText: "Used server-side to create Plaid link tokens and retain bank evidence."
        }
      ],
      environmentOptions: [
        { value: "sandbox", label: "Sandbox", isDefault: true },
        { value: "development", label: "Development", isDefault: false },
        { value: "production", label: "Production", isDefault: false }
      ]
    },
    {
      providerId: "polygon",
      displayName: "Polygon.io",
      capability: "Data",
      status: "Ready",
      credentialState: "Verified",
      credentialSource: "LocalEncryptedStore",
      verificationState: "Verified",
      connectionHealth: "Healthy",
      isEnabled: true,
      isConnected: true,
      fallbackActive: false,
      degradationScore: 0.08,
      lastVerifiedAt: "2026-06-02T16:40:00Z",
      lastSuccessfulAt: "2026-06-02T16:45:00Z",
      lastFailureAt: null,
      lastError: null,
      maskedKeyPreview: "pk_live_****7F3A",
      environment: "paper",
      externalAccountId: null,
      affectedWorkflows: ["Import", "Validate", "Backfill"],
      recommendedAction: "No provider readiness action required.",
      actionHref: "/settings#provider-polygon",
      evidence: [
        {
          kind: "Connection",
          label: "Connection",
          status: "Ready",
          detail: "Connected with 12 active subscriptions and 18 ms average latency.",
          observedAt: "2026-06-02T16:45:00Z"
        }
      ],
      recoveryActions: [
        {
          actionId: "provider.polygon.verify",
          label: "Verify credentials",
          target: "/api/providers/polygon/verify",
          requiresMutation: true
        }
      ],
      credentialFields: [
        {
          name: "ApiKey",
          label: "API key",
          required: true,
          inputKind: "Password",
          placeholder: "POLYGON_API_KEY",
          helpText: "Stored in Meridian's encrypted local provider store and masked after save."
        }
      ],
      environmentOptions: []
    }
  ]
};

describe("data-screen view model", () => {
  it("keeps Data workspace API types canonical with Data Operations compatibility aliases", () => {
    // types.ts is a barrel re-export; the Data workspace declarations live in the workstation-3 module.
    const typesSource = readFileSync(resolve(process.cwd(), "src/types/workstation-3.ts"), "utf8");

    expect(typesSource).toContain("export interface DataProviderRecord");
    expect(typesSource).toContain("export interface DataWorkspaceResponse");
    expect(typesSource).toContain("providers: DataProviderRecord[];");
    expect(typesSource).toContain("backfills: DataBackfillRecord[];");
    expect(typesSource).toContain("exports: DataExportRecord[];");
    expect(typesSource).toContain("uploadTemplates?: DataUploadTemplateCatalog | null;");
    expect(typesSource).toContain("sourceKinds?: string[] | null;");
    expect(typesSource).toContain("mappingGuidance?: string[] | null;");
    expect(typesSource).toContain("export type DataOperationsWorkspaceResponse = DataWorkspaceResponse;");
    expect(typesSource).not.toContain("export interface DataOperationsWorkspaceResponse");
    expect(typesSource).not.toContain("export type DataWorkspaceResponse = DataOperationsWorkspaceResponse");
  });

  it("derives upload template panel state and CSV template content", () => {
    const state = buildDataUploadPanelState(
      defaultDataUploadTemplateCatalog,
      "trade-data",
      {
        uploadId: "UP-1",
        templateId: "trade-data",
        templateLabel: "Trade data",
        fileName: "trades.csv",
        fileSizeBytes: 128,
        contentType: "text/csv",
        uploadedBy: "ops-user",
        uploadedAtUtc: "2026-06-08T12:00:00Z",
        retainedPath: "workstation/data-uploads/UP-1/trades.csv",
        parsedRowCount: 1,
        previewRowCount: 1,
        headers: ["trade_id", "trade_date", "account_code", "symbol"],
        previewRows: [{ trade_id: "TRD-1", trade_date: "2026-06-01", account_code: "FUND-A", symbol: "AAPL" }],
        issues: [
          { severity: "Warning", field: "row", message: "Row has fewer values than headers.", rowNumber: 2 }
        ],
        status: "NeedsSchemaRepair",
        nextAction: "Repair the template headers or required fields, then upload the corrected source file again."
      },
      null,
      false,
      "trades.csv"
    );

    expect(state.templateOptions.map((option) => option.value)).toEqual([
      "trade-data",
      "transaction-data",
      "asset-information",
      "entity-configuration"
    ]);
    expect(state.statusLabel).toBe("Needs schema repair");
    expect(state.retainedPath).toBe("workstation/data-uploads/UP-1/trades.csv");
    expect(state.sourceKinds).toContain("SFTP file drop");
    expect(state.setupChecklist.join(" ")).toContain("pinned host-key fingerprint");
    expect(state.mappingSummary).toBe("4 of 7 required mapping fields matched in trades.csv.");
    expect(state.mappingGuidance[0]).toContain("trade_id");
    expect(state.issueRows[0]).toMatchObject({
      severity: "Warning",
      field: "row",
      rowLabel: "Row 2"
    });
    expect(state.previewRows[0].values).toContainEqual({ id: "symbol", label: "symbol", value: "AAPL" });

    const template = resolveSelectedDataUploadTemplate(defaultDataUploadTemplateCatalog, "trade-data");
    expect(template?.label).toBe("Trade data");
    expect(buildDataUploadTemplateCsv(template!)).toContain("trade_id,trade_date,account_code,symbol,side,quantity,price");
  });

  it("surfaces the onboarding workbook download when the catalog advertises it", () => {
    const catalog = {
      ...defaultDataUploadTemplateCatalog,
      workbookFileName: "meridian-onboarding-workbook.xlsx",
      workbookAcceptedFileExtensions: [".xlsx"],
      workbookMaxFileBytes: 15 * 1024 * 1024
    };

    const withWorkbook = buildDataUploadPanelState(catalog, "trade-data", null, null, false, null);
    expect(withWorkbook.workbookDownload).not.toBeNull();
    expect(withWorkbook.workbookDownload?.fileName).toBe("meridian-onboarding-workbook.xlsx");
    expect(withWorkbook.workbookDownload?.href).toContain("/uploads/templates/workbook");
    expect(withWorkbook.workbookDownload?.label).toContain(".xlsx");

    const withoutWorkbook = buildDataUploadPanelState(
      defaultDataUploadTemplateCatalog,
      "trade-data",
      null,
      null,
      false,
      null
    );
    expect(withoutWorkbook.workbookDownload).toBeNull();
  });

  it("derives route-aware loading state with operator recovery actions", () => {
    const overview = buildDataLoadingState("overview");
    expect(overview).toMatchObject({
      title: "Loading Data workspace",
      statusLabel: "Workspace data pending",
      role: "status",
      ariaLive: "polite",
      ariaBusy: true,
      regionLabel: "Data workspace loading state"
    });
    expect(overview.chips.map((chip) => chip.label)).toEqual(["Providers", "Data quality", "Exports"]);
    expect(overview.actions).toContainEqual({
      id: "settings",
      label: "Check provider setup",
      href: "/settings#alpaca-provider-setup",
      ariaLabel: "Open Alpaca paper provider setup while Data workspace loads",
      variant: "default"
    });

    const backfills = buildDataLoadingState("backfills");
    expect(backfills.title).toBe("Loading backfill queue");
    expect(backfills.description).toContain("historical repair jobs");
    expect(backfills.chips[2]).toEqual({ label: "Backfills", value: "Pending" });
  });

  it("derives route focus, selected backfill, and detail narrative", () => {
    expect(resolveDataWorkstream("/data/backfills")).toBe("backfills");
    expect(resolveDataWorkstream("/data/providers")).toBe("providers");
    expect(resolveDataWorkstream("/data/import")).toBe("import");
    expect(resolveDataWorkstream("/data/exports")).toBe("exports");
    expect(resolveDataWorkstream("/data/query")).toBe("query");
    expect(resolveDataWorkstream("/data")).toBe("overview");
    expect(resolveDataWorkstream("/data-operations/backfills")).toBe("backfills");
    expect(resolveDataWorkstream("/data-operations")).toBe("overview");

    expect(resolveSelectedBackfill(backfills, "BF-1044")?.jobId).toBe("BF-1044");
    expect(resolveSelectedBackfill(backfills, null)?.jobId).toBe("BF-1042");
    expect(buildBackfillNarrative(backfills[1])).toContain("waiting on operator review");

    const selectedDetail = buildSelectedBackfillDetail(backfills, "BF-1044");
    const backfillFocus = buildRouteFocusCardState({
      workstream: "backfills",
      selectedBackfillDetail: selectedDetail,
      backfillDetailEmptyState: null
    });
    expect(backfillFocus).toMatchObject({
      id: DATA_BACKFILL_ROUTE_FOCUS_CARD_ID,
      role: "region",
      ariaLabel: "Backfill route focus",
      eyebrow: "Backfill Detail",
      title: "Backfill queue focus",
      action: null
    });
    expect(backfillFocus.rows).toContainEqual({ id: "updated", label: "Updated", value: "5m ago" });

    const overviewFocus = buildRouteFocusCardState({
      workstream: "overview",
      selectedBackfillDetail: null,
      backfillDetailEmptyState: null
    });
    expect(overviewFocus.ariaLabel).toBe("Data workspace route focus");
    expect(overviewFocus.action).toEqual({
      label: "Open Security Master",
      href: "/accounting/security-master",
      ariaLabel: "Open Security Master in Accounting"
    });

    const importFocus = buildRouteFocusCardState({
      workstream: "import",
      selectedBackfillDetail: null,
      backfillDetailEmptyState: null
    });
    expect(importFocus).toMatchObject({
      ariaLabel: "Data import route focus",
      eyebrow: "File Intake",
      title: "Governed file import"
    });
  });

  it("uses canonical Strategy packet labels for Security Master export evidence", () => {
    const state = buildSecurityMasterWorkspaceState({
      query: "GS",
      selectedSecurityId: null,
      activeTab: "print",
      statusFilter: "active"
    });

    const exportEvidence = state.selectedSecurity?.exportEvidence ?? [];

    expect(exportEvidence).toContainEqual(expect.objectContaining({
      title: "Strategy pack",
      destination: "report-pack / strategy"
    }));
    expect(exportEvidence.map((item) => item.title)).not.toContain("Research pack");
    expect(exportEvidence.map((item) => item.destination)).not.toContain("report-pack / research");
  });

  it("projects corporate-action descriptors into chips with CAEV badges and cancelled treatment", () => {
    const dividend: CorporateActionDescriptor = {
      corpActId: "ca-1",
      canonicalName: "Dividend",
      caevCode: "DVCA",
      displayName: "Cash dividend",
      lifecycleState: "Ex",
      isCancelled: false,
      timeline: [
        { corpActId: "ca-1", lifecycleState: "Confirmed", exDate: "2026-05-29", payDate: "2026-06-28", isAmendment: false }
      ]
    };

    expect(buildCorporateActionChipState(dividend)).toEqual({
      label: "Cash dividend",
      caevCode: "DVCA",
      cancelled: false,
      ariaLabel: "Cash dividend, CAEV DVCA"
    });

    const cancelled = buildCorporateActionChipState({
      ...dividend,
      displayName: "Special dividend",
      lifecycleState: "Cancelled",
      isCancelled: true
    });
    expect(cancelled.cancelled).toBe(true);
    expect(cancelled.ariaLabel).toBe("Special dividend, CAEV DVCA, Cancelled");

    const internalExtension = buildCorporateActionChipState({ ...dividend, caevCode: null, displayName: "Futures expiry" });
    expect(internalExtension.caevCode).toBeNull();
    expect(internalExtension.ariaLabel).toBe("Futures expiry");
  });

  it("fills reached lifecycle stops and marks amendments on the four-stop timeline", () => {
    const amendedDividend: CorporateActionDescriptor = {
      corpActId: "ca-tip",
      canonicalName: "Dividend",
      caevCode: "DVCA",
      displayName: "Cash dividend",
      lifecycleState: "Ex",
      isCancelled: false,
      timeline: [
        { corpActId: "ca-original", lifecycleState: "Announced", exDate: "2026-05-29", payDate: "2026-06-28", isAmendment: false },
        { corpActId: "ca-tip", lifecycleState: "Confirmed", exDate: "2026-05-29", payDate: "2026-06-28", isAmendment: true }
      ]
    };

    const lifecycle = buildCorporateActionLifecycleState(amendedDividend);

    expect(lifecycle.stops.map((stop) => [stop.id, stop.reached, stop.current])).toEqual([
      ["announced", true, false],
      ["confirmed", true, false],
      ["ex", true, true],
      ["paid", false, false]
    ]);
    expect(lifecycle.stops.find((stop) => stop.id === "ex")?.date).toBe("2026-05-29");
    expect(lifecycle.stops.find((stop) => stop.id === "paid")?.date).toBe("2026-06-28");
    expect(lifecycle.amended).toBe(true);
    expect(lifecycle.entries.map((entry) => [entry.label, entry.amended])).toEqual([
      ["Original terms", false],
      ["Amended", true]
    ]);
  });

  it("keeps pre-cancellation progress filled but no current stop for cancelled actions", () => {
    const lifecycle = buildCorporateActionLifecycleState({
      corpActId: "ca-cancel",
      canonicalName: "SpecialDividend",
      caevCode: "DVCA",
      displayName: "Special dividend",
      lifecycleState: "Cancelled",
      isCancelled: true,
      timeline: [
        { corpActId: "ca-announce", lifecycleState: "Announced", exDate: "2026-04-10", payDate: "2026-05-01", isAmendment: false },
        { corpActId: "ca-cancel", lifecycleState: "Cancelled", exDate: "2026-04-10", payDate: "2026-05-01", isAmendment: true }
      ]
    });

    expect(lifecycle.cancelled).toBe(true);
    expect(lifecycle.stops.map((stop) => [stop.id, stop.reached])).toEqual([
      ["announced", true],
      ["confirmed", false],
      ["ex", false],
      ["paid", false]
    ]);
    expect(lifecycle.stops.every((stop) => !stop.current)).toBe(true);
  });

  it("builds expandable corporate-action rows from the workspace state", () => {
    const collapsed = buildSecurityMasterWorkspaceState({
      query: "GS",
      selectedSecurityId: "gs-common-us",
      activeTab: "corporate-actions",
      statusFilter: "active"
    });
    const rows = collapsed.selectedSecurity?.corporateActions ?? [];

    expect(rows.length).toBeGreaterThan(0);
    expect(rows.every((row) => !row.expanded)).toBe(true);
    expect(rows[0].chip).toEqual(expect.objectContaining({ label: "Cash dividend", caevCode: "DVCA" }));
    expect(rows[0].toggleLabel).toContain("Expand lifecycle timeline");

    const cancelledRow = rows.find((row) => row.status === "Cancelled");
    expect(cancelledRow?.chip.cancelled).toBe(true);
    expect(cancelledRow?.lifecycle.cancelled).toBe(true);

    const expanded = buildSecurityMasterWorkspaceState({
      query: "GS",
      selectedSecurityId: "gs-common-us",
      activeTab: "corporate-actions",
      statusFilter: "active",
      expandedCorporateActionIds: [rows[0].id]
    });
    const expandedRow = expanded.selectedSecurity?.corporateActions.find((row) => row.id === rows[0].id);
    expect(expandedRow?.expanded).toBe(true);
    expect(expandedRow?.toggleLabel).toContain("Collapse lifecycle timeline");
  });

  it("normalizes request data and validates required symbols and date range", () => {
    expect(buildBackfillRequest({
      provider: " polygon ",
      symbols: "aapl, msft SPY",
      from: "2024-01-01",
      to: "2024-01-31"
    })).toEqual({
      provider: "polygon",
      symbols: ["AAPL", "MSFT", "SPY"],
      from: "2024-01-01",
      to: "2024-01-31"
    });

    expect(validateBackfillForm({ provider: "polygon", symbols: "", from: "", to: "" }, providers))
      .toBe("Enter at least one symbol before previewing a backfill.");
    expect(validateBackfillForm({ provider: "polygon", symbols: "AAPL", from: "2024-02-31", to: "" }, providers))
      .toBe("Use YYYY-MM-DD for the From date.");
    expect(validateBackfillForm({ provider: "polygon", symbols: "AAPL", from: "2024-02-01", to: "2024-01-01" }, providers))
      .toBe("From date must be before or equal to To date.");
    expect(validateBackfillForm({ provider: "polygon", symbols: "AAPL", from: "", to: "" }, []))
      .toBe("Configure a provider before previewing a backfill.");
  });

  it("derives command enablement, feedback, and async labels", () => {
    const empty = buildBackfillTriggerState({
      form: { provider: "polygon", symbols: "", from: "", to: "" },
      busy: false,
      phase: "idle",
      error: null,
      preview: null,
      result: null,
      configuredProviders: providers
    });

    expect(empty.canPreview).toBe(false);
    expect(empty.feedbackText).toBeNull();

    const readyWithPreview = buildBackfillTriggerState({
      form: { provider: "polygon", symbols: "aapl msft", from: "", to: "" },
      busy: false,
      phase: "idle",
      error: null,
      preview,
      result: null,
      configuredProviders: providers
    });

    expect(readyWithPreview.canPreview).toBe(true);
    expect(readyWithPreview.canRun).toBe(true);
    expect(readyWithPreview.statusAnnouncement).toBe("Backfill preview ready for AAPL, MSFT.");

    const running = buildBackfillTriggerState({
      form: { provider: "polygon", symbols: "AAPL", from: "", to: "" },
      busy: true,
      phase: "running",
      error: null,
      preview,
      result: null,
      configuredProviders: providers
    });

    expect(running.runButtonLabel).toBe("Running...");
    expect(running.runButtonAriaLabel).toBe("Running backfill request");
    expect(running.dialogState.formStatusLabel).toBe("Running the previewed backfill request.");
    expect(running.dialogState.runAction).toMatchObject({
      label: "Running...",
      disabled: true,
      disabledReason: "Backfill is already running.",
      busy: true,
      busyLabel: "Running..."
    });
    expect(running.dialogState.runAction.busy).toBe(true);
    expect(running.dialogState.closeButtonDisabledReason).toContain("wait for the current request");
    expect(running.dialogState.symbolsField.disabled).toBe(true);
    expect(running.dialogState.symbolsField.disabledReason).toContain("wait for the current request");
    expect(running.statusAnnouncement).toBe("Running backfill request.");
  });

  it("derives backfill dialog field, focus, and action semantics", () => {
    const dialog = buildBackfillDialogState({
      form: { provider: "polygon", symbols: "", from: "", to: "" },
      busy: false,
      phase: "idle",
      validationError: "Enter at least one symbol before previewing a backfill.",
      preview: null,
      configuredProviders: providers
    });

    expect(dialog.titleId).toBe("backfill-dialog-title");
    expect(dialog.descriptionId).toBe("backfill-dialog-description");
    expect(dialog.formLabel).toBe("Backfill request form");
    expect(dialog.closeButtonLabel).toBe("Close backfill dialog");
    expect(dialog.closeButtonDisabledReason).toBeNull();
    expect(dialog.summaryItems).toEqual([
      { id: "provider", label: "Provider", value: "Polygon", tone: "default" },
      { id: "symbols", label: "Symbols", value: "None yet", tone: "warning" },
      { id: "range", label: "Range", value: "Full available history", tone: "default" }
    ]);
    expect(dialog.providerOptions.map((provider) => provider.value)).toEqual(["polygon"]);
    expect(dialog.selectedProviderDetail).toContain("Polygon is configured for Streaming equities");
    expect(dialog.symbolsField).toMatchObject({
      id: "backfill-symbols",
      ariaLabel: "Backfill symbols",
      placeholder: "Type symbols, e.g. AAPL, MSFT, SPY",
      describedBy: "backfill-symbols-help backfill-form-status backfill-form-feedback",
      autoFocus: true
    });
    expect(dialog.previewAction).toMatchObject({
      label: "Preview",
      ariaLabel: "Preview backfill unavailable: Enter at least one symbol before previewing a backfill.",
      disabled: true,
      disabledReason: "Enter at least one symbol before previewing a backfill.",
      busy: false
    });
    expect(dialog.runAction.ariaLabel).toBe("Run backfill unavailable until preview completes");
    expect(dialog.runAction.disabledReason).toBe("Enter at least one symbol before previewing a backfill.");
    expect(dialog.formStatusLabel).toBe("Enter at least one symbol before previewing a backfill.");
    expect(dialog.formStatusTone).toBe("warning");
  });

  it("derives provider options from configured providers only", () => {
    const options = buildBackfillProviderOptions("alpaca", [alpacaProvider]);
    expect(options[0]).toMatchObject({
      value: "alpaca",
      label: "Alpaca",
      badge: "Configured"
    });
    expect(options.map((option) => option.value)).not.toContain("yahoo");

    expect(buildBackfillProviderOptions("internal-feed", [alpacaProvider]).map((option) => option.value))
      .toEqual(["alpaca"]);
    expect(buildBackfillProviderOptions("alpaca", [])).toEqual([]);
  });

  it("ignores stale backfill preview responses after a newer preview settles", async () => {
    const previewRequests: Array<{
      request: BackfillTriggerRequest;
      resolve: (value: BackfillPreviewResult) => void;
    }> = [];
    const idleProgress: BackfillProgressResponse = {
      active: false,
      provider: null,
      symbols: [],
      message: null
    };
    const services = {
      preview: (request: BackfillTriggerRequest) => new Promise<BackfillPreviewResult>((resolve) => {
        previewRequests.push({ request, resolve });
      }),
      run: async (request: BackfillTriggerRequest) => ({ ...completedBackfill, symbols: request.symbols }),
      getProgress: async () => idleProgress
    };

    const workspace: DataWorkspaceResponse = {
      metrics: [],
      providers,
      backfills: [],
      exports: []
    };
    const { result } = renderHook(() => useDataViewModel(workspace, "/data/backfills", services));

    await waitFor(() => expect(result.current.form.provider).toBe("polygon"));

    act(() => {
      result.current.updateBackfillForm("symbols", "AAPL MSFT");
    });

    let firstPreview!: Promise<void>;
    let secondPreview!: Promise<void>;
    act(() => {
      firstPreview = result.current.previewBackfill();
      secondPreview = result.current.previewBackfill();
    });

    await waitFor(() => expect(previewRequests).toHaveLength(2));

    await act(async () => {
      previewRequests[1].resolve({
        ...preview,
        symbols: [{ symbol: "MSFT", estimatedBars: 25, hasMarketHoursData: true, notes: [] }]
      });
      await secondPreview;
    });

    expect(result.current.preview?.symbols.map((entry) => entry.symbol)).toEqual(["MSFT"]);
    expect(result.current.preview?.symbols[0]?.estimatedBars).toBe(25);

    await act(async () => {
      previewRequests[0].resolve({
        ...preview,
        symbols: [{ symbol: "AAPL", estimatedBars: 1000, hasMarketHoursData: true, notes: [] }]
      });
      await firstPreview;
    });

    expect(result.current.preview?.symbols.map((entry) => entry.symbol)).toEqual(["MSFT"]);
    expect(result.current.preview?.symbols[0]?.estimatedBars).toBe(25);
    expect(result.current.phase).toBe("idle");
    expect(result.current.busy).toBe(false);
  });

  it("surfaces structured preview errors with operator-visible detail lines", async () => {
    const idleProgress: BackfillProgressResponse = {
      active: false,
      provider: null,
      symbols: [],
      message: null
    };
    const services = {
      preview: async () => {
        throw createApiErrorFromResponseBody(
          "/api/backfill/preview",
          400,
          JSON.stringify({
            title: "Backfill validation failed",
            detail: "Provider rejected the requested date window.",
            errors: {
              symbols: ["At least one supported symbol is required."]
            }
          })
        );
      },
      run: async (request: BackfillTriggerRequest) => ({ ...completedBackfill, symbols: request.symbols }),
      getProgress: async () => idleProgress
    };
    const workspace: DataWorkspaceResponse = {
      metrics: [],
      providers,
      backfills: [],
      exports: []
    };
    const { result } = renderHook(() => useDataViewModel(workspace, "/data/backfills", services));

    await waitFor(() => expect(result.current.form.provider).toBe("polygon"));

    act(() => {
      result.current.updateBackfillForm("symbols", "AAPL");
    });

    await act(async () => {
      await result.current.previewBackfill();
    });

    expect(result.current.error).toEqual({
      summary: "Provider rejected the requested date window.",
      details: [
        "Meridian service returned 400. Open diagnostics for technical details.",
        "Backfill validation failed",
        "symbols: At least one supported symbol is required."
      ]
    });
  });

  it("derives backfill preview and completion result cards", () => {
    const previewCard = buildBackfillResultCardState(preview, "preview");

    expect(previewCard.title).toBe("Preview ready — polygon");
    expect(previewCard.statusLabel).toBe("Preview only");
    expect(previewCard.tone).toBe("warning");
    expect(previewCard.rows).toContainEqual({ id: "symbols", label: "Symbols", value: "AAPL, MSFT" });
    expect(previewCard.rows).toContainEqual({ id: "bars", label: "Bars", value: "1,200" });
    expect(previewCard.rows).toContainEqual({ id: "range", label: "Range", value: "2024-01-01 to 2024-01-31" });
    expect(previewCard.rows).toContainEqual({ id: "timing", label: "Timing", value: "5 sec est." });
    expect(previewCard.ariaLabel).toContain("Status Preview only");

    const completedCard = buildBackfillResultCardState(completedBackfill, "result");

    expect(completedCard.title).toBe("Backfill complete — polygon");
    expect(completedCard.statusLabel).toBe("Written");
    expect(completedCard.tone).toBe("success");
  });

  it("derives exact provider fallback progress without hiding dropped notifications", () => {
    const state = buildBackfillLiveProgressState({
      isActive: true,
      timestamp: "2026-07-13T17:05:00Z",
      providerProgress: {
        symbols: {
          AAPL: {
            symbol: "AAPL",
            rangeStart: "2026-07-01",
            rangeEnd: "2026-07-12",
            totalDays: 12,
            completedDays: 6,
            percentComplete: 50,
            isCompleted: false,
            isFailed: false,
            isSkipped: false,
            currentProvider: "stooq",
            currentStatus: "Downloading",
            providerAttempt: 2,
            retryRound: 1,
            operation: "daily-bars",
            attemptStartedAt: "2026-07-13T17:04:30Z",
            lastUpdatedAt: "2026-07-13T17:05:00Z",
            error: null
          }
        },
        recentProviderAttempts: [{
          symbol: "AAPL",
          provider: "polygon",
          rangeStart: "2026-07-01",
          rangeEnd: "2026-07-12",
          providerAttempt: 1,
          retryRound: 0,
          operation: "daily-bars",
          status: "Failed",
          barsDownloaded: 0,
          startedAt: "2026-07-13T17:04:00Z",
          observedAt: "2026-07-13T17:04:20Z",
          error: "HTTP 429"
        }],
        overallPercentComplete: 50,
        totalSymbols: 1,
        completedSymbols: 0,
        failedSymbols: 0,
        droppedProviderNotifications: 3,
        timestamp: "2026-07-13T17:05:00Z"
      }
    });

    expect(state).toMatchObject({
      active: true,
      title: "Live provider progress",
      overallPercent: 50,
      droppedNotificationWarning: expect.stringContaining("3 older provider notifications")
    });
    expect(state?.symbols[0]).toMatchObject({
      symbol: "AAPL",
      range: "2026-07-01 to 2026-07-12",
      provider: "stooq",
      attempt: "Attempt 2, retry 1",
      status: "Downloading",
      progress: "50%"
    });
    expect(state?.recentAttempts[0]).toMatchObject({
      label: "AAPL · polygon",
      tone: "danger",
      detail: expect.stringContaining("HTTP 429")
    });
  });

  it("polls provider progress while a backfill run is in flight and keeps the final snapshot", async () => {
    let resolveRun!: (value: BackfillTriggerResult) => void;
    let runSettled = false;
    const progressReads = vi.fn(async (): Promise<BackfillProgressResponse> => ({
      isActive: !runSettled,
      providerProgress: {
        symbols: {},
        recentProviderAttempts: [],
        overallPercentComplete: runSettled ? 100 : 25,
        totalSymbols: 1,
        completedSymbols: runSettled ? 1 : 0,
        failedSymbols: 0,
        droppedProviderNotifications: 0,
        timestamp: "2026-07-13T17:05:00Z"
      },
      timestamp: "2026-07-13T17:05:00Z"
    }));
    const services = {
      preview: async () => preview,
      run: () => new Promise<BackfillTriggerResult>((resolve) => {
        resolveRun = resolve;
      }),
      getProgress: progressReads
    };
    const workspace: DataWorkspaceResponse = { metrics: [], providers, backfills: [], exports: [] };
    const { result } = renderHook(() => useDataViewModel(workspace, "/data/backfills", services));

    await waitFor(() => expect(result.current.form.provider).toBe("polygon"));
    act(() => result.current.updateBackfillForm("symbols", "AAPL"));
    await act(async () => result.current.previewBackfill());

    let runPromise!: Promise<void>;
    act(() => {
      runPromise = result.current.runBackfill();
    });

    await waitFor(() => {
      expect(progressReads).toHaveBeenCalled();
      expect(result.current.liveProgressState?.overallPercent).toBe(25);
    });

    await act(async () => {
      runSettled = true;
      resolveRun(completedBackfill);
      await runPromise;
    });

    expect(result.current.liveProgressState).toMatchObject({
      active: false,
      overallPercent: 100,
      title: "Final provider progress"
    });
    expect(progressReads.mock.calls.length).toBeGreaterThanOrEqual(2);
  });

  it("derives failed backfill result cards with danger tone and error evidence", () => {
    const failedCard = buildBackfillResultCardState({
      ...completedBackfill,
      success: false,
      error: "Provider rejected the requested range.",
      from: null,
      to: null,
      startedUtc: "not-a-date",
      completedUtc: "not-a-date"
    }, "result");

    expect(failedCard.title).toBe("Backfill failed — polygon");
    expect(failedCard.statusLabel).toBe("Failed");
    expect(failedCard.tone).toBe("danger");
    expect(failedCard.errorText).toBe("Provider rejected the requested range.");
    expect(failedCard.rows).toContainEqual({ id: "range", label: "Range", value: "Full available history" });
    expect(failedCard.rows).toContainEqual({ id: "timing", label: "Timing", value: "Timing unavailable" });
    expect(failedCard.ariaLabel).toContain("Error Provider rejected the requested range.");
  });

  it("derives provider, backfill, and export section rows with empty guidance", () => {
    const providerSection = buildProviderSection(providers);
    expect(providerSection.hasRows).toBe(true);
    expect(providerSection.tableLabel).toBe("Provider health");
    expect(providerSection.description).toContain("Provider trust");
    expect(providerSection.detailPanelId).toBe(DATA_PROVIDER_DETAIL_PANEL_ID);
    expect(providerSection.selectedRowId).toBe("provider-row-polygon");
    expect(providerSection.selectedDetail?.title).toBe("Polygon.io");
    expect(providerSection.selectedDetail?.id).toBe(DATA_PROVIDER_DETAIL_PANEL_ID);
    expect(providerSection.rows[0].statusTone).toBe("success");
    expect(providerSection.rows[0].rowClassName).toBe("bg-success/5");
    expect(providerSection.rows[0].rowId).toBe("provider-row-polygon");
    expect(providerSection.rows[0].selected).toBe(true);
    expect(providerSection.rows[0].expanded).toBe(true);
    expect(providerSection.rows[0].detailPanelId).toBe(DATA_PROVIDER_DETAIL_PANEL_ID);
    expect(providerSection.rows[0].ariaLabel).toContain("Selected provider Polygon.io");
    expect(providerSection.rows[0].selectAriaLabel).toBe("Inspect provider Polygon.io");
    expect(providerSection.rows[0].trustFields).toContainEqual({
      id: "trust-score",
      label: "Trust score",
      value: "98%"
    });
    expect(providerSection.rows[0].recommendedActionText).toBe("Keep provider active.");
    expect(providerSection.rows[0].detailDescription).toContain("provider detail panel is expanded");
    const emptyProviderSection = buildProviderSection([]);
    expect(emptyProviderSection.emptyState.title).toBe("No providers configured");
    expect(emptyProviderSection.detailEmptyState?.title).toBe("No provider selected");

    const backfillSection = buildBackfillSection(backfills, "BF-1044", "backfills");
    expect(backfillSection.tableLabel).toBe("Backfill queue");
    expect(backfillSection.description).toBe("Queued and recently completed historical repair jobs");
    expect(backfillSection.rows[1].selected).toBe(true);
    expect(backfillSection.rows[1].expanded).toBe(true);
    expect(backfillSection.rows[0].expanded).toBe(false);
    expect(backfillSection.rows[0].rowClassName).toBe("bg-paper/5");
    expect(backfillSection.rows[1].rowClassName).toBe("bg-warning/5");
    expect(backfillSection.rows[1].rowId).toBe("backfill-row-bf-1044");
    expect(backfillSection.rows[1].detailPanelId).toBe(DATA_BACKFILL_DETAIL_PANEL_ID);
    expect(backfillSection.rows[0].detailPanelId).toBe(DATA_BACKFILL_DETAIL_PANEL_ID);
    expect(backfillSection.rows[1].ariaLabel).toContain("Selected backfill BF-1044");
    expect(backfillSection.rows[1].selectAriaLabel).toBe("Inspect backfill BF-1044");
    expect(backfillSection.rows[0].detailDescription).toContain("updates the shared backfill detail panel");
    expect(buildBackfillSection([], null, "backfills").emptyState.description).toContain("Trigger backfill");

    const exportSection = buildExportSection(exports);
    expect(exportSection.tableLabel).toBe("Recent exports");
    expect(exportSection.description).toBe("Latest package and reporting outputs tied to Data workspace evidence");
    expect(exportSection.selectedRowId).toBe("export-row-ex-2201");
    expect(exportSection.rows[0].summaryText).toBe("strategy pack · 124k · 4m ago");
    expect(exportSection.rows[0].statusVariant).toBe("success");
    expect(exportSection.rows[0].rowClassName).toBe("bg-success/5");
    expect(exportSection.rows[0].selected).toBe(true);
    expect(exportSection.rows[0].expanded).toBe(true);
    expect(exportSection.rows[0].detailPanelId).toBe(DATA_EXPORT_DETAIL_PANEL_ID);
    expect(exportSection.rows[0].selectAriaLabel).toBe("Inspect export EX-2201");
    expect(exportSection.rows[0].detailFields).toContainEqual({
      id: "export-id",
      label: "Export ID",
      value: "EX-2201"
    });
    expect(exportSection.rows[0].actionText).toContain("Attach export");
    expect(exportSection.rows[0].ariaLabel).toContain("Next action Attach export");
    expect(exportSection.selectedDetail?.id).toBe(DATA_EXPORT_DETAIL_PANEL_ID);
    expect(exportSection.selectedDetail?.actionText).toContain("Attach export");
    expect(buildExportSection([]).emptyState.title).toBe("No exports available");
    expect(buildExportSection([]).detailEmptyState?.title).toBe("No export selected");
  });

  it("merges provider connections, routing bindings, and trust evidence into provider management rows", () => {
    const providerSection = buildProviderSection(
      providers,
      "provider-row-polygon",
      {
        providerConnections: [polygonConnection],
        providerRoutingConnections: [polygonRoutingConnection],
        providerRoutingBindings: [polygonRoutingBinding],
        providerRoutingTrustSnapshots: [polygonTrustSnapshot]
      },
      "credentials"
    );

    expect(providerSection.title).toBe("Provider Management");
    expect(providerSection.providerOptions).toEqual([
      expect.objectContaining({
        value: "provider-row-polygon",
        label: "Polygon.io — Warning — Data"
      })
    ]);
    expect(providerSection.rows).toHaveLength(1);
    expect(providerSection.rows[0]).toMatchObject({
      provider: "Polygon.io",
      status: "Warning",
      credentialText: "Verified",
      verificationText: "Verified",
      affectedWorkflowsText: "Strategy, Backfill",
      actionLabel: "View Details"
    });
    expect(providerSection.rows[0].trustFields).toContainEqual({
      id: "trust-score",
      label: "Trust score",
      value: "82% · Warning"
    });
    expect(providerSection.summaryCards).toContainEqual(expect.objectContaining({
      id: "credentials",
      label: "Credential State",
      value: "Verified"
    }));
    expect(providerSection.selectedDetail?.activeTab).toBe("credentials");
    expect(providerSection.selectedDetail?.credentialFields).toContainEqual({
      id: "masked-key",
      label: "Masked key preview",
      value: "pk_live_****7F3A"
    });
    expect(providerSection.selectedDetail?.credentialFields).toContainEqual({
      id: "credential-field-ApiKey",
      label: "API key",
      value: "Required field"
    });
    expect(providerSection.selectedDetail?.credentialFields).not.toContainEqual(expect.objectContaining({
      value: expect.stringContaining("local:polygon")
    }));
    expect(providerSection.selectedDetail?.diagnostics).toContainEqual(expect.objectContaining({
      id: "backfill-test",
      status: "pending",
      statusLabel: "Placeholder"
    }));
  });

  it("uses shared provider readiness as the command-center row source", () => {
    const providerSection = buildProviderSection(
      providers,
      "provider-row-plaid",
      {
        providerReadiness,
        providerConnections: [polygonConnection]
      },
      "diagnostics"
    );

    expect(providerSection.statusLabel).toBe("1 blocked");
    expect(providerSection.statusTone).toBe("danger");
    expect(providerSection.readinessSummary).toContain("Repair Plaid credentials");
    expect(providerSection.rows.map((row) => row.provider)).toEqual(["Plaid", "Polygon.io"]);
    expect(providerSection.rows[0]).toMatchObject({
      provider: "Plaid",
      status: "Blocked",
      credentialText: "Missing",
      actionLabel: "Open setup",
      recommendedActionText: "Connect Plaid sandbox credentials before bank-account evidence can be retained."
    });
    expect(providerSection.selectedDetail?.diagnostics).toContainEqual(expect.objectContaining({
      label: "Credential",
      status: "fail",
      detail: "Required Plaid client fields are missing."
    }));

    const credentialProviderSection = buildProviderSection(
      providers,
      "provider-row-plaid",
      {
        providerReadiness,
        providerConnections: [polygonConnection]
      },
      "credentials"
    );
    expect(credentialProviderSection.selectedDetail?.credentialFields).toContainEqual({
      id: "credential-field-ClientId",
      label: "Client ID",
      value: "Required field"
    });
    expect(credentialProviderSection.selectedDetail?.credentialFields).toContainEqual({
      id: "credential-field-Secret",
      label: "Secret",
      value: "Required field"
    });
    expect(credentialProviderSection.selectedDetail?.credentialFields).toContainEqual({
      id: "allowed-environments",
      label: "Allowed environments",
      value: "Sandbox, Development, Production"
    });
  });

  it("merges workspace and routing providers while normalizing trust and readiness coverage", () => {
    const alpacaRoutingConnection: ProviderRoutingConnection = {
      ...polygonRoutingConnection,
      connectionId: "alpaca",
      providerFamilyId: "alpaca",
      displayName: "Alpaca"
    };
    const alpacaTrustSnapshot: ProviderRoutingTrustSnapshot = {
      ...polygonTrustSnapshot,
      connectionId: "alpaca",
      providerFamilyId: "alpaca",
      score: 0.92,
      isHealthy: true,
      healthStatus: "Healthy"
    };

    const providerSection = buildProviderSection(
      [...providers, alpacaProvider],
      "provider-row-alpaca",
      {
        providerReadiness,
        providerConnections: [polygonConnection],
        providerRoutingConnections: [polygonRoutingConnection, alpacaRoutingConnection],
        providerRoutingTrustSnapshots: [polygonTrustSnapshot, alpacaTrustSnapshot]
      }
    );

    expect(providerSection.rows.filter((row) => row.provider === "Alpaca")).toHaveLength(1);
    expect(providerSection.rows.find((row) => row.provider === "Alpaca")?.trustFields).toContainEqual({
      id: "trust-score",
      label: "Trust score",
      value: "92% · Healthy"
    });
    expect(providerSection.readinessSummary).toContain("Displayed posture: 2 ready / 0 review / 0 degraded / 1 blocked.");
    expect(providerSection.readinessSummary).toContain("Shared readiness covers 2 of 3 displayed providers.");
  });

  it("surfaces verification command state without mutating provider rows", () => {
    const providerSection = buildProviderSection(
      providers,
      "provider-row-polygon",
      { providerConnections: [polygonConnection] },
      "diagnostics",
      {
        label: "Run Verification",
        ariaLabel: "Run provider credential verification for polygon",
        busy: false,
        disabled: false,
        disabledReason: null,
        statusLabel: "Verification passed.",
        statusTone: "success",
        details: ["External account: acct-provider-01"]
      }
    );

    expect(providerSection.selectedDetail?.activeTab).toBe("diagnostics");
    expect(providerSection.selectedDetail?.verifyAction).toMatchObject({
      label: "Run Verification",
      statusLabel: "Verification passed.",
      statusTone: "success"
    });
    expect(providerSection.selectedDetail?.verifyAction.details).toEqual(["External account: acct-provider-01"]);
    expect(providerSection.rows[0].credentialText).toBe("Verified");
  });

  it("uses direct workstation provider-center diagnostics when the payload includes them", () => {
    const providerSection = buildProviderSection(
      [
        {
          ...providers[0],
          connectionSummary: polygonConnection,
          diagnostics: [
            {
              id: "provider-health",
              label: "Provider health",
              status: "warning",
              statusLabel: "Warning",
              detail: "Latency drift is elevated for Polygon."
            }
          ]
        }
      ],
      "provider-row-polygon",
      {},
      "diagnostics"
    );

    expect(providerSection.selectedDetail?.diagnostics).toEqual([
      {
        id: "provider-health",
        label: "Provider health",
        status: "warning",
        statusLabel: "Warning",
        detail: "Latency drift is elevated for Polygon."
      }
    ]);
    expect(providerSection.selectedDetail?.diagnosticsEmptyState).toBeNull();
  });

  it("shows a diagnostics empty state when shared provider evidence has not loaded yet", () => {
    const providerSection = buildProviderSection(
      providers,
      "provider-row-polygon",
      {},
      "diagnostics"
    );

    expect(providerSection.selectedDetail?.diagnosticsEmptyState).toEqual({
      title: "Diagnostics not loaded yet",
      description: "Load credential or routing evidence before Meridian can run provider-specific verification, quote probes, or backfill checks."
    });
    expect(providerSection.selectedDetail?.verifyAction.disabled).toBe(true);
    expect(providerSection.selectedDetail?.verifyAction.disabledReason).toBe("This provider row does not have a credential connection record yet.");
  });

  it("selects export detail rows by export id or table row id", () => {
    const exportRecords: DataExportRecord[] = [
      ...exports,
      {
        exportId: "EX-2202",
        profile: "report-pack",
        target: "board packet",
        status: "Attention",
        rows: "42k",
        updatedAt: "9m ago"
      }
    ];

    expect(resolveSelectedExport(exportRecords, "EX-2202")?.profile).toBe("report-pack");
    expect(resolveSelectedExport(exportRecords, "export-row-ex-2202")?.profile).toBe("report-pack");

    const exportSection = buildExportSection(exportRecords, "export-row-ex-2202");
    expect(exportSection.selectedRowId).toBe("export-row-ex-2202");
    expect(exportSection.rows[0].selected).toBe(false);
    expect(exportSection.rows[1].selected).toBe(true);
    expect(exportSection.rows[1].expanded).toBe(true);
    expect(exportSection.rows[1].ariaLabel).toContain("Selected export EX-2202");
    expect(exportSection.rows[1].detailDescription).toContain("detail panel is expanded");

    const detail = buildSelectedExportDetail(exportRecords, "export-row-ex-2202");
    expect(detail?.ariaLabel).toContain("Export detail for EX-2202");
    expect(detail?.statusVariant).toBe("warning");
    expect(detail?.fields).toContainEqual({ id: "rows", label: "Rows", value: "42k" });
    expect(detail?.actionText).toBe("Review export profile and target before report-pack use.");
  });

  it("selects provider detail rows by provider name or table row id", () => {
    const providerRecords: DataProviderRecord[] = [
      ...providers,
      {
        provider: "Databento",
        status: "Degraded",
        capability: "Historical futures",
        latency: "2.4s p95",
        note: "Backfill pressure is elevated.",
        trustScore: "72%",
        signalSource: "Replay audit",
        reasonCode: "LATENCY_ELEVATED",
        recommendedAction: "Route fresh requests to Polygon until replay clears.",
        gateImpact: "Blocks promotion gates"
      }
    ];

    expect(resolveSelectedProvider(providerRecords, "Databento")?.provider).toBe("Databento");
    expect(resolveSelectedProvider(providerRecords, "provider-row-databento")?.provider).toBe("Databento");

    const providerSection = buildProviderSection(providerRecords, "provider-row-databento");
    expect(providerSection.selectedRowId).toBe("provider-row-databento");
    expect(providerSection.rows[0].selected).toBe(false);
    expect(providerSection.rows[1].selected).toBe(true);
    expect(providerSection.rows[1].expanded).toBe(true);
    expect(providerSection.rows[1].statusTone).toBe("danger");
    expect(providerSection.rows[1].rowClassName).toBe("bg-danger/5");
    expect(providerSection.rows[1].ariaLabel).toContain("Selected provider Databento");

    const detail = buildSelectedProviderDetail(providerRecords, "provider-row-databento");
    expect(detail?.ariaLabel).toContain("Provider detail for Databento");
    expect(detail?.fields).toContainEqual({ id: "trust-score", label: "Trust score", value: "72%" });
    expect(detail?.actionText).toContain("Route fresh requests");
  });

  it("derives provider setup fields from the selected provider kind", () => {
    const alpacaDialog = buildProviderSetupDialogState("idle", {
      kind: "alpaca",
      displayName: "Alpaca",
      apiKey: "",
      apiSecret: "",
      endpoint: "",
      capabilities: ["streaming", "brokerage"]
    });

    expect(alpacaDialog.providerKindField.options.map((option) => option.value)).toContain("alpaca");
    expect(alpacaDialog.providerKindField.description).toContain("paper trading");
    expect(alpacaDialog.displayNameField).toMatchObject({
      id: "provider-setup-name",
      field: "displayName",
      value: "Alpaca"
    });
    expect(alpacaDialog.credentialFields.map((field) => field.field)).toEqual(["apiKey", "apiSecret"]);
    expect(alpacaDialog.credentialFields.map((field) => field.autoComplete)).toEqual(["new-password", "new-password"]);
    expect(alpacaDialog.capabilityOptions.find((option) => option.id === "brokerage")?.selected).toBe(true);
    expect(alpacaDialog.submitAction.disabledReason).toBe("An API key is required for Alpaca.");
    expect(alpacaDialog.selectedProviderSummary.rows).toContainEqual({
      id: "credentials",
      label: "Required",
      value: "API key + secret"
    });
    expect(alpacaDialog.workflowSteps.map((step) => step.label)).toEqual([
      "Connect Source",
      "Acquire Data",
      "Validate Data",
      "Normalize Data",
      "Store Data",
      "Publish Data"
    ]);
    expect(alpacaDialog.workflowSteps[0]).toMatchObject({
      id: "connect-source",
      status: "current",
      statusLabel: "Current"
    });
    expect(alpacaDialog.cancelAction).toEqual({
      label: "Cancel",
      ariaLabel: "Cancel provider setup",
      disabled: false,
      disabledReason: null
    });
    expect(alpacaDialog.successPanel).toEqual({
      title: "Next validation",
      ariaLabel: "Provider setup next validation"
    });
    expect(alpacaDialog.successActions.map((action) => action.id)).toEqual(["live-quotes", "readiness"]);

    const yahooDialog = buildProviderSetupDialogState("idle", {
      kind: "yahoo",
      displayName: "Yahoo Finance",
      apiKey: "",
      apiSecret: "",
      endpoint: "",
      capabilities: ["backfill"]
    });

    expect(yahooDialog.credentialFields).toHaveLength(0);
    expect(yahooDialog.submitAction.disabled).toBe(false);
    expect(yahooDialog.selectedProviderSummary.noCredentialMessage).toBe("Yahoo Finance can be configured without pasting a secret.");
    expect(yahooDialog.selectedProviderSummary.rows).toContainEqual({
      id: "next-step",
      label: "After save",
      value: "Preview a historical backfill"
    });
    expect(yahooDialog.successActions).toEqual([
      {
        id: "backfill",
        label: "Preview a backfill",
        href: "/data/operations",
        ariaLabel: "Preview a historical backfill after configuring Yahoo Finance",
        variant: "default"
      }
    ]);

    const plaidDialog = buildProviderSetupDialogState("idle", {
      kind: "plaid",
      displayName: "Plaid",
      apiKey: "",
      apiSecret: "",
      endpoint: "",
      capabilities: ["banking", "identity", "investments"],
      environment: "sandbox"
    });

    expect(plaidDialog.providerKindField.options.map((option) => option.value)).toContain("plaid");
    expect(plaidDialog.providerKindField.description).toContain("reconciliation workflows");
    expect(plaidDialog.credentialFields.map((field) => field.field)).toEqual(["apiKey", "apiSecret"]);
    expect(plaidDialog.credentialFields.map((field) => field.label)).toEqual(["Client ID", "Secret"]);
    expect(plaidDialog.credentialFields.map((field) => field.ariaLabel)).toEqual(["Plaid client ID", "Plaid secret"]);
    expect(plaidDialog.capabilityOptions.find((option) => option.id === "banking")?.selected).toBe(true);
    expect(plaidDialog.capabilityOptions.find((option) => option.id === "identity")?.selected).toBe(true);
    expect(plaidDialog.capabilityOptions.find((option) => option.id === "investments")?.selected).toBe(true);
    expect(plaidDialog.submitAction.disabledReason).toBe("A Client ID is required for Plaid.");
    expect(plaidDialog.institutionSearch?.disabledReason).toBeNull();
    expect(plaidDialog.institutionSearch?.searchAction.disabledReason).toBe("Type at least two characters to search for a financial institution.");
    expect(plaidDialog.selectedProviderSummary.rows).toContainEqual({
      id: "credentials",
      label: "Required",
      value: "Client ID + secret"
    });
    expect(plaidDialog.selectedProviderSummary.rows).toContainEqual({
      id: "next-step",
      label: "After save",
      value: "Link account evidence"
    });
    expect(plaidDialog.successActions).toEqual([
      {
        id: "plaid-link",
        label: "Link account evidence",
        href: "/data/providers",
        ariaLabel: "Link account evidence after configuring Plaid",
        variant: "default"
      }
    ]);

    const plaidInstitutionSearch = buildPlaidInstitutionSearchState({
      form: {
        kind: "plaid",
        displayName: "Plaid",
        apiKey: "client-id",
        apiSecret: "secret",
        endpoint: "",
        capabilities: ["banking", "identity"],
        environment: "sandbox"
      },
      query: "Chase",
      phase: "success",
      results: [
        {
          institutionId: "ins_3",
          name: "Chase",
          products: ["Transactions", "Auth", "Identity"],
          countryCodes: ["US"],
          url: "https://www.chase.com",
          primaryColor: "#117ACA",
          logo: null
        }
      ],
      selectedInstitutionId: "ins_3",
      error: null
    });

    expect(plaidInstitutionSearch).toMatchObject({
      label: "Financial institution",
      statusLabel: '1 supported institution found for "Chase".',
      selectedInstitutionLabel: "Chase"
    });
    expect(plaidInstitutionSearch?.results).toContainEqual({
      institutionId: "ins_3",
      name: "Chase",
      detail: "ins_3 | US | Transactions, Auth, Identity",
      selected: true
    });

    const submittingDialog = buildProviderSetupDialogState("submitting", {
      kind: "alpaca",
      displayName: "Alpaca",
      apiKey: "key-123",
      apiSecret: "secret-456",
      endpoint: "",
      capabilities: ["streaming", "brokerage"]
    });

    expect(submittingDialog.providerKindField.disabled).toBe(true);
    expect(submittingDialog.displayNameField.disabled).toBe(true);
    expect(submittingDialog.credentialFields.every((field) => field.disabled)).toBe(true);
    expect(submittingDialog.capabilityOptions.every((option) => option.disabled)).toBe(true);
    expect(submittingDialog.providerKindField.disabledReason).toBe("Provider setup is in progress; wait before editing.");
    expect(submittingDialog.cancelAction).toEqual({
      label: "Cancel",
      ariaLabel: "Cancel provider setup unavailable: Provider setup is in progress; wait before closing.",
      disabled: true,
      disabledReason: "Provider setup is in progress; wait before closing."
    });

    const successDialog = buildProviderSetupDialogState("success", {
      kind: "yahoo",
      displayName: "Yahoo Finance",
      apiKey: "",
      apiSecret: "",
      endpoint: "",
      capabilities: ["backfill"]
    });
    expect(successDialog.workflowSteps[0]).toMatchObject({
      id: "connect-source",
      status: "complete",
      statusLabel: "Connected"
    });
    expect(successDialog.workflowSteps[1]).toMatchObject({
      id: "acquire-data",
      status: "current",
      statusLabel: "Next"
    });
  });

  it("derives provider setup success actions from configured capabilities", () => {
    expect(buildProviderSetupSuccessActions({
      kind: "polygon",
      displayName: "Polygon.io",
      apiKey: "",
      apiSecret: "",
      endpoint: "",
      capabilities: ["streaming", "backfill", "reference"]
    })).toEqual([
      {
        id: "live-quotes",
        label: "Validate live quotes",
        href: "/data/quotes?symbol=AAPL",
        ariaLabel: "Validate live quotes after configuring Polygon.io",
        variant: "default"
      },
      {
        id: "backfill",
        label: "Preview a backfill",
        href: "/data/operations",
        ariaLabel: "Preview a historical backfill after configuring Polygon.io",
        variant: "outline"
      },
      {
        id: "security-master",
        label: "Review Security Master",
        href: "/accounting/security-master",
        ariaLabel: "Review Security Master coverage after configuring Polygon.io",
        variant: "outline"
      }
    ]);

    expect(buildProviderSetupSuccessActions({
      kind: "plaid",
      displayName: "Plaid Treasury",
      apiKey: "",
      apiSecret: "",
      endpoint: "",
      capabilities: ["banking", "identity", "investments", "transfers"]
    })).toEqual([
      {
        id: "plaid-link",
        label: "Link account evidence",
        href: "/data/providers",
        ariaLabel: "Link account evidence after configuring Plaid Treasury",
        variant: "default"
      },
      {
        id: "plaid-transfers",
        label: "Review transfers",
        href: "/accounting/reconciliation",
        ariaLabel: "Review transfer evidence after configuring Plaid Treasury",
        variant: "outline"
      }
    ]);

    expect(buildProviderSetupSuccessActions({
      kind: "custom",
      displayName: "",
      apiKey: "",
      apiSecret: "",
      endpoint: "https://providers.example.test",
      capabilities: []
    })).toEqual([
      {
        id: "security-master",
        label: "Review Security Master",
        href: "/accounting/security-master",
        ariaLabel: "Review Security Master coverage after configuring Custom data connection",
        variant: "default"
      }
    ]);
  });

  it("derives safe provider setup routing and credential metadata from setup results", () => {
    const metadata = buildProviderSetupSuccessMetadata({
      success: true,
      providerId: "provider-alpaca-paper",
      providerName: "Alpaca paper",
      message: "Alpaca paper was configured.",
      error: null,
      connectionId: "provider-alpaca-paper",
      bindingIds: ["provider-alpaca-paper-RealtimeMarketData", "provider-alpaca-paper-HistoricalBars"],
      credentialState: "Configured",
      credentialSource: "ExternalVaultReference",
      credentialReference: "vault:alpaca/paper",
      environment: "paper",
      warnings: ["Credential verification still needs to run."]
    });

    expect(metadata.rows).toEqual([
      { id: "connection-id", label: "Connection", value: "provider-alpaca-paper" },
      {
        id: "binding-ids",
        label: "Bindings",
        value: "provider-alpaca-paper-RealtimeMarketData, provider-alpaca-paper-HistoricalBars"
      },
      { id: "credential-state", label: "Credential", value: "Configured" },
      { id: "credential-source", label: "Source", value: "External vault reference" },
      { id: "environment", label: "Environment", value: "PAPER" }
    ]);
    expect(metadata.warnings).toEqual(["Credential verification still needs to run."]);
    expect(metadata.rows.map((row) => row.value).join(" ")).not.toContain("vault:alpaca/paper");
  });

  it("ignores stale provider setup responses after a newer submission settles", async () => {
    const setupRequests: Array<{
      resolve: (value: Awaited<ReturnType<typeof workstationApi.setupProvider>>) => void;
    }> = [];
    const setupProvider = vi.spyOn(workstationApi, "setupProvider").mockImplementation(() => (
      new Promise((resolve) => {
        setupRequests.push({ resolve });
      })
    ));

    try {
      const { result } = renderHook(() => useDataViewModel(null, "/data"));

      act(() => {
        result.current.updateProviderForm("kind", "alpaca");
        result.current.updateProviderForm("apiKey", "key-123");
        result.current.updateProviderForm("apiSecret", "secret-456");
      });

      let firstSubmit!: Promise<void>;
      let secondSubmit!: Promise<void>;
      act(() => {
        firstSubmit = result.current.submitProviderSetup();
        secondSubmit = result.current.submitProviderSetup();
      });

      await waitFor(() => expect(setupRequests).toHaveLength(2));

      await act(async () => {
        setupRequests[1].resolve({
          success: true,
          providerId: "provider-alpaca-paper",
          providerName: "Alpaca paper",
          message: "Provider configured.",
          error: null
        });
        await secondSubmit;
      });

      expect(result.current.providerPhase).toBe("success");
      expect(result.current.providerSetupResult?.providerName).toBe("Alpaca paper");

      await act(async () => {
        setupRequests[0].resolve({
          success: false,
          providerId: "provider-alpaca-old",
          providerName: "Old Alpaca",
          message: "Old provider response.",
          error: "Old response failed."
        });
        await firstSubmit;
      });

      expect(result.current.providerPhase).toBe("success");
      expect(result.current.providerSetupResult?.providerName).toBe("Alpaca paper");
      expect(result.current.providerSetupError).toBeNull();
    } finally {
      setupProvider.mockRestore();
    }
  });

  it("surfaces structured provider setup errors without exposing secrets", async () => {
    const setupProvider = vi.spyOn(workstationApi, "setupProvider").mockRejectedValue(
      createApiErrorFromResponseBody(
        "/api/providers/configure",
        403,
        JSON.stringify({
          title: "Credential verification failed",
          detail: "Paper trading credentials were rejected by the provider.",
          errors: {
            apiKey: ["Verify the configured key against the selected environment."]
          }
        })
      )
    );

    try {
      const { result } = renderHook(() => useDataViewModel(null, "/data"));

      act(() => {
        result.current.updateProviderForm("kind", "alpaca");
        result.current.updateProviderForm("apiKey", "key-123");
        result.current.updateProviderForm("apiSecret", "secret-456");
      });

      await act(async () => {
        await result.current.submitProviderSetup();
      });

      expect(result.current.providerPhase).toBe("error");
      expect(result.current.providerSetupError).toEqual({
        summary: "Permission denied for this Meridian role.",
        details: [
          "Meridian service returned 403. Open diagnostics for technical details.",
          "Credential verification failed",
          "Paper trading credentials were rejected by the provider.",
          "apiKey: Verify the configured key against the selected environment."
        ]
      });
      expect(result.current.providerSetupError?.details.join(" ")).not.toContain("secret-456");
      expect(result.current.providerSetupError?.details.join(" ")).not.toContain("key-123");
    } finally {
      setupProvider.mockRestore();
    }
  });

  it("clears provider setup secrets while preserving non-secret form context", () => {
    expect(clearProviderSetupCredentials({
      kind: "alpaca",
      displayName: "Alpaca paper",
      apiKey: "key-123",
      apiSecret: "secret-456",
      endpoint: "https://paper-api.alpaca.markets",
      capabilities: ["streaming", "brokerage"]
    })).toEqual({
      kind: "alpaca",
      displayName: "Alpaca paper",
      apiKey: "",
      apiSecret: "",
      endpoint: "https://paper-api.alpaca.markets",
      capabilities: ["streaming", "brokerage"]
    });
  });

  it("validates endpoint provider URLs before submit", () => {
    expect(validateProviderSetupForm({
      kind: "interactivebrokers",
      displayName: "IBKR",
      apiKey: "",
      apiSecret: "",
      endpoint: "not-a-url",
      capabilities: ["streaming"]
    })).toBe("Enter a valid http or https service URL for Interactive Brokers.");

    expect(validateProviderSetupForm({
      kind: "interactivebrokers",
      displayName: "IBKR",
      apiKey: "",
      apiSecret: "",
      endpoint: "https://localhost:7497",
      capabilities: ["streaming"]
    })).toBeNull();
  });


  it("maps export row status into semantic tones and next actions", () => {
    const exportSection = buildExportSection([
      {
        exportId: "EX-2202",
        profile: "report-pack",
        target: "board packet",
        status: "Running",
        rows: "42k",
        updatedAt: "1m ago"
      },
      {
        exportId: "EX-2203",
        profile: "excel",
        target: "finance review",
        status: "Attention",
        rows: "7k",
        updatedAt: "9m ago"
      }
    ]);

    expect(exportSection.rows[0].statusVariant).toBe("paper");
    expect(exportSection.rows[0].rowClassName).toBe("bg-paper/5");
    expect(exportSection.rows[0].actionText).toBe("Wait for the package writer to finish before handoff.");
    expect(exportSection.rows[1].statusVariant).toBe("warning");
    expect(exportSection.rows[1].rowClassName).toBe("bg-warning/5");
    expect(exportSection.rows[1].actionText).toBe("Review export profile and target before report-pack use.");
  });

  it("derives degraded provider trust evidence with explicit fallback copy", () => {
    const row = buildProviderRow({
      provider: "Databento",
      status: "Degraded",
      capability: "Backfill bars",
      latency: "",
      note: "Checkpoint delay exceeded the review threshold.",
      signalSource: "Provider calibration",
      reasonCode: "CHECKPOINT_DELAY",
      recommendedAction: "Review checkpoint freshness before accepting DK evidence.",
      gateImpact: "Blocks provider trust gate"
    });

    expect(row.statusTone).toBe("danger");
    expect(row.rowClassName).toBe("bg-danger/5");
    expect(row.trustFields).toContainEqual({
      id: "latency",
      label: "Latency",
      value: "Latency not reported"
    });
    expect(row.trustFields).toContainEqual({
      id: "trust-score",
      label: "Trust score",
      value: "Trust score not reported"
    });
    expect(row.gateImpactText).toBe("Blocks provider trust gate");
    expect(row.reasonLabelText).toBe("Checkpoint delay");
    expect(row.reasonCodeText).toBe("CHECKPOINT_DELAY");
    expect(row.ariaLabel).toContain("Recommended action Review checkpoint freshness");
  });

  it("presents provider reason enums as operator labels while preserving non-code copy", () => {
    expect(formatProviderReasonLabel("READINESS_BLOCKED")).toBe("Readiness blocked");
    expect(formatProviderReasonLabel("TRUST_OK")).toBe("Trust OK");
    expect(formatProviderReasonLabel("LATENCY_ELEVATED")).toBe("Latency elevated");
    expect(formatProviderReasonLabel("Reason code not reported")).toBe("Reason code not reported");
  });

  it("derives selected backfill detail panel state with stable linkage ids", () => {
    const detail = buildSelectedBackfillDetail(backfills, "BF-1044");

    expect(detail?.id).toBe(DATA_BACKFILL_DETAIL_PANEL_ID);
    expect(detail?.title).toBe("Options chains / 7d");
    expect(detail?.description).toContain("waiting on operator review");
    expect(detail?.ariaLabel).toContain("Backfill detail for BF-1044");
    expect(detail?.statusLabel).toBe("Review");
    expect(detail?.statusVariant).toBe("warning");
    expect(detail?.rows).toContainEqual({ id: "updated", label: "Updated", value: "5m ago" });
    expect(buildSelectedBackfillDetail([], null)).toBeNull();
  });

  it("uses canonical Data workspace copy for queued backfill narratives", () => {
    const queuedBackfill: DataBackfillRecord = {
      ...backfills[0],
      status: "Queued"
    };

    expect(buildBackfillNarrative(queuedBackfill)).toContain("queued behind active Data workspace work");
    expect(buildBackfillNarrative(queuedBackfill)).not.toContain("data operations");
  });



  it("derives a Data presentation state for empty workspace arrays", () => {
    const emptyData: DataWorkspaceResponse = {
      metrics: [],
      providers: [],
      backfills: [],
      exports: []
    };

    const presentation = buildDataPresentationState(emptyData, null, "backfills");

    expect(presentation.providerSection.hasRows).toBe(false);
    expect(presentation.backfillSection.hasRows).toBe(false);
    expect(presentation.exportSection.hasRows).toBe(false);
    expect(presentation.exportSection.selectedDetail).toBeNull();
    expect(presentation.exportSection.detailEmptyState?.title).toBe("No export selected");
    expect(presentation.selectedBackfillDetail).toBeNull();
    expect(presentation.backfillDetailEmptyState?.title).toBe("No backfill activity yet");
    expect(presentation.routeFocusCard).toMatchObject({
      role: "status",
      ariaLabel: "Backfill route focus empty state",
      title: "No backfill activity yet",
      rows: []
    });
  });
});
