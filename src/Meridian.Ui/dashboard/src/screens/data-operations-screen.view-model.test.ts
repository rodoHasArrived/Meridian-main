import { describe, expect, it } from "vitest";
import {
  buildBackfillSection,
  buildBackfillNarrative,
  buildBackfillRequest,
  buildBackfillTriggerState,
  buildDataOperationsPresentationState,
  buildExportSection,
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
    note: "Realtime subscriptions are stable."
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

  it("derives provider, backfill, and export section rows with empty guidance", () => {
    const providerSection = buildProviderSection(providers);
    expect(providerSection.hasRows).toBe(true);
    expect(providerSection.rows[0].statusTone).toBe("success");
    expect(providerSection.rows[0].ariaLabel).toContain("Polygon provider Healthy");
    expect(buildProviderSection([]).emptyState.title).toBe("No providers reported");

    const backfillSection = buildBackfillSection(backfills, "BF-1044", "backfills");
    expect(backfillSection.rows[1].selected).toBe(true);
    expect(backfillSection.rows[1].ariaLabel).toContain("Inspect backfill BF-1044");
    expect(buildBackfillSection([], null, "backfills").emptyState.description).toContain("Trigger backfill");

    const exportSection = buildExportSection(exports);
    expect(exportSection.rows[0].summaryText).toBe("research pack - 124k");
    expect(buildExportSection([]).emptyState.title).toBe("No exports available");
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
