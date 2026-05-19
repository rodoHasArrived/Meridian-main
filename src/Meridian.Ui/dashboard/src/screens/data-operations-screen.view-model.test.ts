import { act, renderHook, waitFor } from "@testing-library/react";
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
  buildBackfillResultCardState,
  buildBackfillTriggerState,
  buildDataOperationsLoadingState,
  buildDataOperationsPresentationState,
  buildExportSection,
  buildProviderRow,
  buildProviderSection,
  buildProviderSetupDialogState,
  buildProviderSetupSuccessMetadata,
  buildProviderSetupSuccessActions,
  buildRouteFocusCardState,
  buildSelectedExportDetail,
  buildSelectedProviderDetail,
  clearProviderSetupCredentials,
  buildSelectedBackfillDetail,
  resolveDataOperationsWorkstream,
  resolveSelectedProvider,
  resolveSelectedBackfill,
  resolveSelectedExport,
  useDataOperationsViewModel,
  validateBackfillForm,
  validateProviderSetupForm,
  DATA_PROVIDER_DETAIL_PANEL_ID
} from "@/screens/data-operations-screen.view-model";
import type {
  BackfillProgressResponse,
  BackfillTriggerRequest,
  BackfillTriggerResult,
  DataOperationsBackfillRecord,
  DataOperationsExportRecord,
  DataOperationsProviderRecord,
  DataOperationsWorkspaceResponse
} from "@/types";

const backfills: DataOperationsBackfillRecord[] = [
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

const preview: BackfillTriggerResult = {
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

const providers: DataOperationsProviderRecord[] = [
  {
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

const alpacaProvider: DataOperationsProviderRecord = {
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

const exports: DataOperationsExportRecord[] = [
  {
    exportId: "EX-2201",
    profile: "python-pandas",
    target: "research pack",
    status: "Ready",
    rows: "124k",
    updatedAt: "4m ago"
  }
];

describe("data-operations-screen view model", () => {
  it("derives route-aware loading state with operator recovery actions", () => {
    const overview = buildDataOperationsLoadingState("overview");
    expect(overview).toMatchObject({
      title: "Loading Data workspace",
      statusLabel: "Bootstrap pending",
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

    const backfills = buildDataOperationsLoadingState("backfills");
    expect(backfills.title).toBe("Loading backfill queue");
    expect(backfills.description).toContain("historical repair jobs");
    expect(backfills.chips[2]).toEqual({ label: "Backfills", value: "Pending" });
  });

  it("derives route focus, selected backfill, and detail narrative", () => {
    expect(resolveDataOperationsWorkstream("/data/backfills")).toBe("backfills");
    expect(resolveDataOperationsWorkstream("/data")).toBe("overview");
    expect(resolveDataOperationsWorkstream("/data-operations/backfills")).toBe("backfills");
    expect(resolveDataOperationsWorkstream("/data-operations")).toBe("overview");

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
      resolve: (value: BackfillTriggerResult) => void;
    }> = [];
    const idleProgress: BackfillProgressResponse = {
      active: false,
      provider: null,
      symbols: [],
      message: null
    };
    const services = {
      preview: (request: BackfillTriggerRequest) => new Promise<BackfillTriggerResult>((resolve) => {
        previewRequests.push({ request, resolve });
      }),
      run: async (request: BackfillTriggerRequest) => ({ ...preview, symbols: request.symbols }),
      getProgress: async () => idleProgress
    };

    const workspace: DataOperationsWorkspaceResponse = {
      metrics: [],
      providers,
      backfills: [],
      exports: []
    };
    const { result } = renderHook(() => useDataOperationsViewModel(workspace, "/data/backfills", services));

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
      previewRequests[1].resolve({ ...preview, symbols: ["MSFT"], barsWritten: 25 });
      await secondPreview;
    });

    expect(result.current.preview?.symbols).toEqual(["MSFT"]);
    expect(result.current.preview?.barsWritten).toBe(25);

    await act(async () => {
      previewRequests[0].resolve({ ...preview, symbols: ["AAPL"], barsWritten: 1000 });
      await firstPreview;
    });

    expect(result.current.preview?.symbols).toEqual(["MSFT"]);
    expect(result.current.preview?.barsWritten).toBe(25);
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
      run: async (request: BackfillTriggerRequest) => ({ ...preview, symbols: request.symbols }),
      getProgress: async () => idleProgress
    };
    const workspace: DataOperationsWorkspaceResponse = {
      metrics: [],
      providers,
      backfills: [],
      exports: []
    };
    const { result } = renderHook(() => useDataOperationsViewModel(workspace, "/data/backfills", services));

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
        "Endpoint returned 400 for /api/backfill/preview.",
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
    expect(previewCard.rows).toContainEqual({ id: "timing", label: "Timing", value: "Jan 31, 2024 10:00 UTC · 5s elapsed" });
    expect(previewCard.ariaLabel).toContain("Status Preview only");

    const completedCard = buildBackfillResultCardState(preview, "result");

    expect(completedCard.title).toBe("Backfill complete — polygon");
    expect(completedCard.statusLabel).toBe("Written");
    expect(completedCard.tone).toBe("success");
  });

  it("derives failed backfill result cards with danger tone and error evidence", () => {
    const failedCard = buildBackfillResultCardState({
      ...preview,
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
    expect(providerSection.selectedDetail?.title).toBe("Polygon");
    expect(providerSection.selectedDetail?.id).toBe(DATA_PROVIDER_DETAIL_PANEL_ID);
    expect(providerSection.rows[0].statusTone).toBe("success");
    expect(providerSection.rows[0].rowClassName).toBe("bg-success/5");
    expect(providerSection.rows[0].rowId).toBe("provider-row-polygon");
    expect(providerSection.rows[0].selected).toBe(true);
    expect(providerSection.rows[0].expanded).toBe(true);
    expect(providerSection.rows[0].detailPanelId).toBe(DATA_PROVIDER_DETAIL_PANEL_ID);
    expect(providerSection.rows[0].ariaLabel).toContain("Selected provider Polygon");
    expect(providerSection.rows[0].selectAriaLabel).toBe("Inspect provider Polygon");
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
    expect(exportSection.description).toBe("Latest package and reporting outputs tied to data operations evidence");
    expect(exportSection.selectedRowId).toBe("export-row-ex-2201");
    expect(exportSection.rows[0].summaryText).toBe("research pack · 124k · 4m ago");
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

  it("selects export detail rows by export id or table row id", () => {
    const exportRecords: DataOperationsExportRecord[] = [
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
    const providerRecords: DataOperationsProviderRecord[] = [
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
        href: "/data/backfills",
        ariaLabel: "Preview a historical backfill after configuring Yahoo Finance",
        variant: "default"
      }
    ]);

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
        href: "/data/backfills",
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
        ariaLabel: "Review Security Master coverage after configuring Custom endpoint",
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
      const { result } = renderHook(() => useDataOperationsViewModel(null, "/data"));

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
      const { result } = renderHook(() => useDataOperationsViewModel(null, "/data"));

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
        summary: "Paper trading credentials were rejected by the provider.",
        details: [
          "Endpoint returned 403 for /api/providers/configure.",
          "Credential verification failed",
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
    })).toBe("Enter a valid http or https endpoint URL for Interactive Brokers.");

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
    expect(row.ariaLabel).toContain("Recommended action Review checkpoint freshness");
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



  it("derives a data operations presentation state for empty workspace arrays", () => {
    const emptyData: DataOperationsWorkspaceResponse = {
      metrics: [],
      providers: [],
      backfills: [],
      exports: []
    };

    const presentation = buildDataOperationsPresentationState(emptyData, null, "backfills");

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
