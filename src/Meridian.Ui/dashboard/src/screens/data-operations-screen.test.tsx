import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import * as api from "@/lib/api";
import { DataOperationsScreen } from "@/screens/data-operations-screen";
import { renderWithRouter } from "@/test/render";
import type { BackfillProgressResponse, BackfillTriggerResult, DataOperationsWorkspaceResponse } from "@/types";

const data: DataOperationsWorkspaceResponse = {
  metrics: [
    { id: "m1", label: "Providers Healthy", value: "4", delta: "0", tone: "success" },
    { id: "m2", label: "Backfills Running", value: "2", delta: "+1", tone: "default" },
    { id: "m3", label: "Exports Ready", value: "3", delta: "+1", tone: "success" },
    { id: "m4", label: "Needs Review", value: "1", delta: "+1", tone: "warning" }
  ],
  providers: [
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
  ],
  backfills: [
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
  ],
  exports: [
    {
      exportId: "EX-2201",
      profile: "python-pandas",
      target: "research pack",
      status: "Ready",
      rows: "124k",
      updatedAt: "4m ago"
    }
  ]
};

describe("DataOperationsScreen", () => {
  it("renders provider, backfill, and export summaries", () => {
    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    expect(screen.getByText("Security Master command deck")).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: /search securities/i })).toHaveValue("goldman");
    expect(screen.getByText("Search and resolve instruments")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Status: Active/i })).toBeInTheDocument();
    expect(screen.getAllByText("Provider health").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Backfill queue").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Recent exports").length).toBeGreaterThan(0);
    expect(screen.getByText("Polygon")).toBeInTheDocument();
    expect(screen.getByLabelText("Polygon trust evidence")).toBeInTheDocument();
    expect(screen.getByText("Trust score")).toBeInTheDocument();
    expect(screen.getByText("98%")).toBeInTheDocument();
    expect(screen.getByText("Keep provider active.")).toBeInTheDocument();
    expect(screen.getByText("Reason: TRUST_OK")).toBeInTheDocument();
    const exportRow = screen.getByRole("group", { name: /python-pandas export ready/i });
    expect(exportRow).toHaveTextContent("EX-2201");
    expect(exportRow).toHaveTextContent("research pack · 124k · 4m ago");
    expect(exportRow).toHaveTextContent("Next action");
    expect(exportRow).toHaveTextContent("Attach export to the report pack");
  });

  it("renders explicit empty guidance when provider, backfill, and export arrays are empty", () => {
    const emptyData: DataOperationsWorkspaceResponse = {
      metrics: [],
      providers: [],
      backfills: [],
      exports: []
    };

    renderWithRouter(<DataOperationsScreen data={emptyData} />, { initialEntries: ["/data/backfills"] });

    expect(screen.getByText("No providers configured")).toBeInTheDocument();
    expect(screen.getByText("No backfills queued")).toBeInTheDocument();
    expect(screen.getByText("No exports available")).toBeInTheDocument();
    expect(screen.getByText("No backfill activity yet")).toBeInTheDocument();
    expect(screen.getAllByRole("status").length).toBeGreaterThanOrEqual(3);
  });

  it("adapts the hero copy for deep-link routes", () => {
    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data/backfills"] });

    expect(screen.getByText("Backfill queue focus")).toBeInTheDocument();
    expect(screen.getByText("Backfill Detail")).toBeInTheDocument();
    expect(screen.getByText(/Replay is currently advancing/)).toBeInTheDocument();
  });

  it("switches security master results and tab content inside the data lane", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    expect(screen.getByRole("tab", { name: /overview/i })).toHaveAttribute("aria-selected", "true");

    await user.click(screen.getByRole("button", { name: /Open Goldman Sachs Group Inc\. ticker GSN/i }));

    expect(screen.getByText("Turquoise · United Kingdom")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /company/i }));
    expect(screen.getByRole("heading", { name: "The Goldman Sachs Group, Inc." })).toBeInTheDocument();
    expect(screen.getByText("Coverage posture")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /corporate actions/i }));
    expect(screen.getByText("Event timeline")).toBeInTheDocument();
    expect(screen.getByText("Quarterly dividend packet")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: /print \/ export/i }));
    expect(screen.getByText("Packet contents")).toBeInTheDocument();
    expect(screen.getAllByText("SM-PACKET-2026-05-31-GS").length).toBeGreaterThan(0);
  });

  it("supports roving keyboard navigation for security master detail tabs", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    const overviewTab = screen.getByRole("tab", { name: /show overview/i });
    expect(overviewTab).toHaveAttribute("tabindex", "0");

    overviewTab.focus();
    await user.keyboard("{ArrowRight}");

    const companyTab = screen.getByRole("tab", { name: /show company/i });
    await waitFor(() => expect(companyTab).toHaveAttribute("aria-selected", "true"));
    expect(companyTab).toHaveAttribute("tabindex", "0");
    expect(overviewTab).toHaveAttribute("tabindex", "-1");
    await waitFor(() => expect(companyTab).toHaveFocus());

    await user.keyboard("{End}");
    const printTab = screen.getByRole("tab", { name: /show print \/ export/i });
    await waitFor(() => expect(printTab).toHaveAttribute("aria-selected", "true"));
    expect(screen.getByText("Packet contents")).toBeInTheDocument();

    await user.keyboard("{Home}");
    await waitFor(() => expect(overviewTab).toHaveAttribute("aria-selected", "true"));
    expect(screen.getByText("Identifier groups")).toBeInTheDocument();
  });

  it("reveals pending and inactive matches when the status filter is expanded", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    expect(screen.queryByRole("button", { name: /ticker GS\.DR/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /ticker GSL/i })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /Status: Active/i }));

    expect(screen.getByRole("button", { name: /Status: All/i })).toBeInTheDocument();
    expect(screen.getByText("7 results")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /ticker GS\.DR/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /ticker GSL/i })).toBeInTheDocument();
  });

  it("offers a reset path when a security master search returns no rows", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    const searchBox = screen.getByRole("textbox", { name: /search securities/i });

    await user.clear(searchBox);
    await user.type(searchBox, "zzzz");

    expect(screen.getByText("No matching securities")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /reset to default search/i }));

    expect(screen.queryByText("No matching securities")).not.toBeInTheDocument();
    expect(searchBox).toHaveValue("goldman");
  });

  it("switches the detail panel when a backfill row is selected", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data/backfills"] });

    const reviewBackfill = screen.getByRole("button", { name: /BF-1044/i });

    expect(screen.getByRole("button", { name: /BF-1042/i })).toHaveAttribute("aria-pressed", "true");
    expect(reviewBackfill).toHaveAttribute("aria-controls", "backfill-detail-bf-1044");

    await user.click(reviewBackfill);

    expect(reviewBackfill).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByRole("region", { name: /backfill detail for BF-1044/i })).toBeInTheDocument();
    expect(screen.getAllByText("Options chains / 7d").length).toBeGreaterThan(0);
    expect(screen.getByText(/waiting on operator review/i)).toBeInTheDocument();
    expect(screen.getByText("5m ago")).toBeInTheDocument();
  });

  it("opens the trigger backfill dialog when the Trigger backfill button is clicked", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Trigger backfill" })).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("textbox", { name: "Backfill symbols" })).toHaveFocus());
    expect(screen.getByRole("group", { name: "Backfill request form" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close backfill dialog" })).toBeInTheDocument();
    expect(screen.getByText("Enter at least one symbol before previewing a backfill.")).toBeInTheDocument();
  });

  it("keeps preview disabled until the backfill symbols are valid", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));

    const disabledPreview = screen.getByRole("button", { name: /preview backfill unavailable/i });
    expect(disabledPreview).toBeDisabled();
    expect(disabledPreview).toHaveAttribute("title", "Enter at least one symbol before previewing a backfill.");

    await user.type(screen.getByRole("textbox", { name: "Backfill symbols" }), "AAPL");

    expect(screen.getByRole("button", { name: "Preview backfill request" })).toBeEnabled();
    expect(screen.getByText("Backfill request is ready to preview.")).toBeInTheDocument();
  });

  it("calls previewBackfill and shows preview result", async () => {
    const user = userEvent.setup();

    const mockPreview: BackfillTriggerResult = {
      success: true,
      provider: "polygon",
      symbols: ["AAPL"],
      from: "2024-01-01",
      to: "2024-01-31",
      barsWritten: 2100,
      startedUtc: "2024-01-31T10:00:00Z",
      completedUtc: "2024-01-31T10:00:05Z",
      error: null
    };

    vi.spyOn(api, "previewBackfill").mockResolvedValueOnce(mockPreview);

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    await user.type(screen.getByRole("textbox", { name: "Backfill symbols" }), "AAPL");
    await user.click(screen.getByRole("button", { name: "Preview backfill request" }));

    await waitFor(() => {
      const previewStatus = screen.getByRole("status", { name: /preview ready — polygon/i });
      expect(previewStatus).toHaveTextContent("Preview only");
      expect(previewStatus).toHaveTextContent("Bars");
      expect(previewStatus).toHaveTextContent("2,100");
      expect(previewStatus).toHaveTextContent("2024-01-01 to 2024-01-31");
    });
    expect(screen.getByText("Preview is ready. Review the summary before running.")).toBeInTheDocument();
  });

  it("calls triggerBackfill after preview and shows success result", async () => {
    const user = userEvent.setup();

    const mockPreview: BackfillTriggerResult = {
      success: true,
      provider: "polygon",
      symbols: ["MSFT"],
      from: null,
      to: null,
      barsWritten: 500,
      startedUtc: "2024-01-31T10:00:00Z",
      completedUtc: "2024-01-31T10:00:05Z",
      error: null
    };

    const mockResult: BackfillTriggerResult = {
      ...mockPreview,
      barsWritten: 512
    };

    const mockProgress: BackfillProgressResponse = {
      active: false,
      provider: null,
      symbols: [],
      message: null
    };

    vi.spyOn(api, "previewBackfill").mockResolvedValueOnce(mockPreview);
    vi.spyOn(api, "triggerBackfill").mockResolvedValueOnce(mockResult);
    vi.spyOn(api, "getBackfillProgress").mockResolvedValue(mockProgress);

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    await user.type(screen.getByRole("textbox", { name: "Backfill symbols" }), "MSFT");
    await user.click(screen.getByRole("button", { name: "Preview backfill request" }));

    await waitFor(() => expect(screen.getByRole("button", { name: "Run previewed backfill request" })).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: "Run previewed backfill request" }));

    await waitFor(() => {
      const resultStatus = screen.getByRole("status", { name: /backfill complete — polygon/i });
      expect(resultStatus).toHaveTextContent("Written");
      expect(resultStatus).toHaveTextContent("MSFT");
      expect(resultStatus).toHaveTextContent("512");
    });
  });

  it("shows an error banner when previewBackfill rejects", async () => {
    const user = userEvent.setup();

    vi.spyOn(api, "previewBackfill").mockRejectedValueOnce(new Error("Provider offline"));

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    await user.type(screen.getByRole("textbox", { name: "Backfill symbols" }), "SPY");
    await user.click(screen.getByRole("button", { name: "Preview backfill request" }));

    await waitFor(() => {
      expect(screen.getByRole("alert")).toHaveTextContent("Provider offline");
    });
  });
});
