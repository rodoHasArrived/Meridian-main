import { describe, expect, it } from "vitest";
import {
  buildBackfillSection,
  buildBackfillNarrative,
  buildBackfillRequest,
  buildBackfillResultCardState,
  buildBackfillTriggerState,
  buildDataOperationsPresentationState,
  buildExportSection,
  buildProviderRow,
  buildProviderSection,
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
    expect(running.statusAnnouncement).toBe("Running backfill request.");
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
    expect(backfillSection.rows[1].ariaLabel).toContain("Inspect backfill BF-1044");
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
    expect(presentation.backfillDetailEmptyState?.title).toBe("No backfill activity yet");
  });
});
