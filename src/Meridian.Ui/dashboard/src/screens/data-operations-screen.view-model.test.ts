import { describe, expect, it } from "vitest";
import {
  buildBackfillSection,
  buildBackfillDialogState,
  buildBackfillNarrative,
  buildBackfillRequest,
  buildBackfillResultCardState,
  buildBackfillTriggerState,
  buildDataOperationsPresentationState,
  buildExportSection,
  buildProviderRow,
  buildProviderSection,
  buildProviderSetupDialogState,
  buildSecurityMasterWorkspaceState,
  buildSelectedBackfillDetail,
  resolveDataOperationsWorkstream,
  resolveSelectedBackfill,
  validateBackfillForm
} from "@/screens/data-operations-screen.view-model";
import type {
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
  it("derives route focus, selected backfill, and detail narrative", () => {
    expect(resolveDataOperationsWorkstream("/data/backfills")).toBe("backfills");
    expect(resolveDataOperationsWorkstream("/data")).toBe("overview");
    expect(resolveDataOperationsWorkstream("/data-operations/backfills")).toBe("backfills");
    expect(resolveDataOperationsWorkstream("/data-operations")).toBe("overview");

    expect(resolveSelectedBackfill(backfills, "BF-1044")?.jobId).toBe("BF-1044");
    expect(resolveSelectedBackfill(backfills, null)?.jobId).toBe("BF-1042");
    expect(buildBackfillNarrative(backfills[1])).toContain("waiting on operator review");
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

    expect(validateBackfillForm({ provider: "polygon", symbols: "", from: "", to: "" }))
      .toBe("Enter at least one symbol before previewing a backfill.");
    expect(validateBackfillForm({ provider: "polygon", symbols: "AAPL", from: "2024-02-01", to: "2024-01-01" }))
      .toBe("From date must be before or equal to To date.");
  });

  it("derives command enablement, feedback, and async labels", () => {
    const empty = buildBackfillTriggerState({
      form: { provider: "polygon", symbols: "", from: "", to: "" },
      busy: false,
      phase: "idle",
      error: null,
      preview: null,
      result: null
    });

    expect(empty.canPreview).toBe(false);
    expect(empty.feedbackText).toBeNull();

    const readyWithPreview = buildBackfillTriggerState({
      form: { provider: "polygon", symbols: "aapl msft", from: "", to: "" },
      busy: false,
      phase: "idle",
      error: null,
      preview,
      result: null
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
      result: null
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
    expect(running.statusAnnouncement).toBe("Running backfill request.");
  });

  it("derives backfill dialog field, focus, and action semantics", () => {
    const dialog = buildBackfillDialogState({
      form: { provider: "polygon", symbols: "", from: "", to: "" },
      busy: false,
      phase: "idle",
      validationError: "Enter at least one symbol before previewing a backfill.",
      preview: null
    });

    expect(dialog.titleId).toBe("backfill-dialog-title");
    expect(dialog.descriptionId).toBe("backfill-dialog-description");
    expect(dialog.formLabel).toBe("Backfill request form");
    expect(dialog.closeButtonLabel).toBe("Close backfill dialog");
    expect(dialog.closeButtonDisabledReason).toBeNull();
    expect(dialog.summaryItems).toEqual([
      { id: "provider", label: "Provider", value: "polygon", tone: "default" },
      { id: "symbols", label: "Symbols", value: "None yet", tone: "warning" },
      { id: "range", label: "Range", value: "Full available history", tone: "default" }
    ]);
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
    expect(providerSection.rows[0].statusTone).toBe("success");
    expect(providerSection.rows[0].ariaLabel).toContain("Polygon provider Healthy");
    expect(providerSection.rows[0].trustFields).toContainEqual({
      id: "trust-score",
      label: "Trust score",
      value: "98%"
    });
    expect(providerSection.rows[0].recommendedActionText).toBe("Keep provider active.");
    expect(buildProviderSection([]).emptyState.title).toBe("No providers reported");

    const backfillSection = buildBackfillSection(backfills, "BF-1044", "backfills");
    expect(backfillSection.rows[1].selected).toBe(true);
    expect(backfillSection.rows[1].rowId).toBe("backfill-row-bf-1044");
    expect(backfillSection.rows[1].detailPanelId).toBe("backfill-detail-bf-1044");
    expect(backfillSection.rows[1].ariaLabel).toContain("Selected backfill BF-1044");
    expect(backfillSection.rows[0].detailDescription).toContain("details will replace the current backfill detail panel");
    expect(buildBackfillSection([], null, "backfills").emptyState.description).toContain("Trigger backfill");

    const exportSection = buildExportSection(exports);
    expect(exportSection.rows[0].summaryText).toBe("research pack · 124k · 4m ago");
    expect(exportSection.rows[0].statusVariant).toBe("success");
    expect(exportSection.rows[0].detailFields).toContainEqual({
      id: "export-id",
      label: "Export ID",
      value: "EX-2201"
    });
    expect(exportSection.rows[0].actionText).toContain("Attach export");
    expect(exportSection.rows[0].ariaLabel).toContain("Next action Attach export");
    expect(buildExportSection([]).emptyState.title).toBe("No exports available");
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
    expect(alpacaDialog.capabilityOptions.find((option) => option.id === "brokerage")?.selected).toBe(true);
    expect(alpacaDialog.submitAction.disabledReason).toBe("An API key is required for Alpaca.");

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
  });

  it("keeps non-active security records hidden until the filter is expanded", () => {
    const activeState = buildSecurityMasterWorkspaceState({
      query: "goldman",
      selectedSecurityId: null,
      activeTab: "overview",
      statusFilter: "active"
    });

    const allState = buildSecurityMasterWorkspaceState({
      query: "goldman",
      selectedSecurityId: null,
      activeTab: "overview",
      statusFilter: "all"
    });

    expect(activeState.results).toHaveLength(5);
    expect(allState.results).toHaveLength(7);
    expect(activeState.results.some((row) => row.status === "Pending")).toBe(false);
    expect(allState.results.some((row) => row.status === "Pending")).toBe(true);
    expect(allState.results.some((row) => row.status === "Inactive")).toBe(true);
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
    expect(exportSection.rows[0].actionText).toBe("Wait for the package writer to finish before handoff.");
    expect(exportSection.rows[1].statusVariant).toBe("warning");
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

    expect(detail?.id).toBe("backfill-detail-bf-1044");
    expect(detail?.title).toBe("Options chains / 7d");
    expect(detail?.description).toContain("waiting on operator review");
    expect(detail?.ariaLabel).toContain("Backfill detail for BF-1044");
    expect(detail?.rows).toContainEqual({ id: "updated", label: "Updated", value: "5m ago" });
    expect(buildSelectedBackfillDetail([], null)).toBeNull();
  });

  it("builds a security master workspace with search results, tab state, and packet detail", () => {
    const securityMaster = buildSecurityMasterWorkspaceState({
      query: "goldman",
      selectedSecurityId: "gs-bond-de",
      activeTab: "corporate-actions",
      statusFilter: "active"
    });

    expect(securityMaster.resultCountLabel).toBe("5 results");
    expect(securityMaster.statusChipLabel).toBe("Status: Active");
    expect(securityMaster.results.some((row) => row.selected && row.securityId === "gs-bond-de")).toBe(true);
    expect(securityMaster.tabs.find((tab) => tab.id === "corporate-actions")?.selected).toBe(true);
    expect(securityMaster.selectedSecurity?.titleCode).toBe("GOS 3.625 10/30");
    expect(securityMaster.selectedSecurity?.corporateActions[0].description).toContain("Semi-annual coupon");
    expect(securityMaster.selectedSecurity?.printPacketId).toBe("SM-PACKET-2026-06-09-GOS");
  });

  it("derives a security master empty state when the query returns no matches", () => {
    const securityMaster = buildSecurityMasterWorkspaceState({
      query: "nonexistent issuer",
      selectedSecurityId: null,
      activeTab: "overview",
      statusFilter: "active"
    });

    expect(securityMaster.hasResults).toBe(false);
    expect(securityMaster.emptyState?.title).toBe("No matching securities");
    expect(securityMaster.selectedSecurity).toBeNull();
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
    expect(presentation.selectedBackfillDetail).toBeNull();
    expect(presentation.backfillDetailEmptyState?.title).toBe("No backfill activity yet");
  });
});
