import { act, fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { vi } from "vitest";
import * as api from "@/lib/api";
import { DataOperationsScreen } from "@/screens/data-operations-screen";
import { DATA_BACKFILL_DETAIL_PANEL_ID } from "@/screens/data-operations-screen.view-model";
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
  it("renders an accessible route-aware loading panel when bootstrap data is unavailable", () => {
    renderWithRouter(<DataOperationsScreen data={null} />, { initialEntries: ["/data/backfills"] });

    const loading = screen.getByRole("status", { name: "Data backfill loading state" });
    expect(loading).toHaveAttribute("aria-busy", "true");
    expect(screen.getByText("Loading backfill queue")).toBeInTheDocument();
    expect(screen.getByText("Bootstrap pending")).toBeInTheDocument();
    expect(screen.getByText("Backfills")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open settings to check provider setup/i })).toHaveAttribute("href", "/settings");
    expect(screen.getByRole("link", { name: /open live quotes while data workspace loads/i })).toHaveAttribute("href", "/data/quotes");
  });

  it("renders provider, backfill, and export summaries", () => {
    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    expect(screen.getByText("Data operations command deck")).toBeInTheDocument();
    expect(screen.getByText("Provider posture")).toBeInTheDocument();
    expect(screen.getByText("Backfill repair")).toBeInTheDocument();
    expect(screen.getByText("Export readiness")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Data workspace route focus" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Security Master in Accounting" }))
      .toHaveAttribute("href", "/accounting/security-master");
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
    expect(screen.getByRole("status", { name: "Backfill detail empty state" })).toHaveTextContent("No backfill activity yet");
    expect(screen.getAllByRole("status").length).toBeGreaterThanOrEqual(3);
  });

  it("clears provider credentials after setup and suppresses browser autocomplete", async () => {
    const user = userEvent.setup();

    vi.spyOn(api, "setupProvider").mockResolvedValueOnce({
      success: true,
      providerId: "provider-alpaca",
      providerName: "Alpaca paper",
      message: "Provider configured.",
      error: null
    });

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /configure a new data provider/i }));
    await user.selectOptions(screen.getByLabelText("Select provider type"), "alpaca");

    const apiKey = screen.getByLabelText("Provider API key");
    const apiSecret = screen.getByLabelText("Provider API secret");
    expect(apiKey).toHaveAttribute("autocomplete", "new-password");
    expect(apiSecret).toHaveAttribute("autocomplete", "new-password");

    await user.type(apiKey, "key-123");
    await user.type(apiSecret, "secret-456");
    await user.click(screen.getByRole("button", { name: /configure and register provider/i }));

    await waitFor(() => expect(api.setupProvider).toHaveBeenCalledWith(expect.objectContaining({
      kind: "alpaca",
      apiKey: "key-123",
      apiSecret: "secret-456"
    })));

    expect(await screen.findByText("Alpaca paper configured")).toBeInTheDocument();
    expect(screen.getByRole("region", { name: "Provider setup next validation" }))
      .toHaveTextContent("Next validation");
    expect(screen.getByRole("link", { name: "Validate live quotes after configuring Alpaca" }))
      .toHaveAttribute("href", "/data/quotes?symbol=AAPL");
    expect(screen.getByRole("link", { name: "Preview a historical backfill after configuring Alpaca" }))
      .toHaveAttribute("href", "/data/backfills");
    expect(screen.getByRole("link", { name: "Check Trading readiness after configuring Alpaca" }))
      .toHaveAttribute("href", "/trading/readiness");

    await user.click(screen.getByRole("button", { name: "Configure another" }));

    expect(screen.getByLabelText("Provider API key")).toHaveValue("");
    expect(screen.getByLabelText("Provider API secret")).toHaveValue("");
  });

  it("adapts the hero copy for deep-link routes", () => {
    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data/backfills"] });

    expect(screen.getByText("Backfill queue focus")).toBeInTheDocument();
    expect(screen.getByText("Backfill Detail")).toBeInTheDocument();
    expect(screen.getByText(/Replay is currently advancing/)).toBeInTheDocument();
  });

  it("keeps the old static Security Master workbench out of the Data route", () => {
    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    expect(screen.queryByRole("textbox", { name: /search securities/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: /show overview/i })).not.toBeInTheDocument();
    expect(screen.queryByText("Security Master command deck")).not.toBeInTheDocument();
  });

  it("switches the detail panel when a backfill row is selected", async () => {
    const user = userEvent.setup();

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data/backfills"] });

    const reviewBackfill = screen.getByRole("button", { name: /BF-1044/i });
    const runningBackfill = screen.getByRole("button", { name: /BF-1042/i });

    expect(runningBackfill).toHaveAttribute("aria-pressed", "true");
    expect(runningBackfill).toHaveAttribute("aria-expanded", "true");
    expect(runningBackfill).toHaveAttribute("aria-controls", DATA_BACKFILL_DETAIL_PANEL_ID);
    expect(reviewBackfill).toHaveAttribute("aria-controls", DATA_BACKFILL_DETAIL_PANEL_ID);
    expect(reviewBackfill).toHaveAttribute("aria-expanded", "false");

    await user.click(reviewBackfill);

    expect(reviewBackfill).toHaveAttribute("aria-pressed", "true");
    expect(reviewBackfill).toHaveAttribute("aria-expanded", "true");
    expect(runningBackfill).toHaveAttribute("aria-expanded", "false");
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

    fireEvent.change(screen.getByRole("textbox", { name: "Backfill symbols" }), {
      target: { value: "AAPL" }
    });

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

  it("locks backfill request fields while preview is pending", async () => {
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
    let resolvePreview!: (value: BackfillTriggerResult) => void;

    vi.spyOn(api, "previewBackfill").mockReturnValueOnce(new Promise<BackfillTriggerResult>((resolve) => {
      resolvePreview = resolve;
    }));

    renderWithRouter(<DataOperationsScreen data={data} />, { initialEntries: ["/data"] });

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    fireEvent.change(screen.getByRole("textbox", { name: "Backfill symbols" }), {
      target: { value: "AAPL" }
    });
    await user.click(screen.getByRole("button", { name: "Preview backfill request" }));

    await waitFor(() => expect(screen.getByRole("textbox", { name: "Backfill symbols" })).toBeDisabled());
    expect(screen.getByRole("textbox", { name: "Backfill provider" })).toBeDisabled();
    expect(screen.getByLabelText("Backfill start date")).toBeDisabled();
    expect(screen.getByLabelText("Backfill end date")).toBeDisabled();
    expect(screen.getByRole("textbox", { name: "Backfill symbols" }))
      .toHaveAttribute("title", "Backfill request is running; wait for the current request to finish before editing.");

    await act(async () => {
      resolvePreview(mockPreview);
    });

    await waitFor(() => expect(screen.getByRole("textbox", { name: "Backfill symbols" })).toBeEnabled());
    expect(screen.getByRole("status", { name: /preview ready — polygon/i })).toHaveTextContent("Preview only");
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
