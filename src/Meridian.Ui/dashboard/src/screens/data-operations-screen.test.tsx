import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { vi } from "vitest";
import * as api from "@/lib/api";
import { DataOperationsScreen } from "@/screens/data-operations-screen";
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
      note: "Realtime subscriptions are stable."
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
    render(
      <MemoryRouter initialEntries={["/data"]}>
        <DataOperationsScreen data={data} />
      </MemoryRouter>
    );

    expect(screen.getAllByText("Provider health").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Backfill queue").length).toBeGreaterThan(0);
    expect(screen.getAllByText("Recent exports").length).toBeGreaterThan(0);
    expect(screen.getByText("Polygon")).toBeInTheDocument();
  });

  it("adapts the hero copy for deep-link routes", () => {
    render(
      <MemoryRouter initialEntries={["/data/backfills"]}>
        <DataOperationsScreen data={data} />
      </MemoryRouter>
    );

    expect(screen.getByText("Backfill queue focus")).toBeInTheDocument();
    expect(screen.getByText("Backfill Detail")).toBeInTheDocument();
    expect(screen.getByText(/Replay is currently advancing/)).toBeInTheDocument();
  });

  it("switches the detail panel when a backfill row is selected", async () => {
    const user = userEvent.setup();

    render(
      <MemoryRouter initialEntries={["/data/backfills"]}>
        <DataOperationsScreen data={data} />
      </MemoryRouter>
    );

    await user.click(screen.getByRole("button", { name: /BF-1044/i }));

    expect(screen.getAllByText("Options chains / 7d").length).toBeGreaterThan(0);
    expect(screen.getByText(/waiting on operator review/i)).toBeInTheDocument();
  });

  it("opens the trigger backfill dialog when the Trigger backfill button is clicked", async () => {
    const user = userEvent.setup();

    render(
      <MemoryRouter initialEntries={["/data"]}>
        <DataOperationsScreen data={data} />
      </MemoryRouter>
    );

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Trigger backfill" })).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/AAPL MSFT SPY/i)).toBeInTheDocument();
  });

  it("keeps preview disabled until the backfill symbols are valid", async () => {
    const user = userEvent.setup();

    render(
      <MemoryRouter initialEntries={["/data"]}>
        <DataOperationsScreen data={data} />
      </MemoryRouter>
    );

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));

    expect(screen.getByRole("button", { name: /^preview$/i })).toBeDisabled();

    await user.type(screen.getByPlaceholderText(/AAPL MSFT SPY/i), "AAPL");

    expect(screen.getByRole("button", { name: /^preview$/i })).toBeEnabled();
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

    render(
      <MemoryRouter initialEntries={["/data"]}>
        <DataOperationsScreen data={data} />
      </MemoryRouter>
    );

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    await user.type(screen.getByPlaceholderText(/AAPL MSFT SPY/i), "AAPL");
    await user.click(screen.getByRole("button", { name: /^preview$/i }));

    await waitFor(() => {
      expect(screen.getByText(/preview — polygon/i)).toBeInTheDocument();
      expect(screen.getByText(/Bars:\s*2,100/i)).toBeInTheDocument();
    });
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

    render(
      <MemoryRouter initialEntries={["/data"]}>
        <DataOperationsScreen data={data} />
      </MemoryRouter>
    );

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    await user.type(screen.getByPlaceholderText(/AAPL MSFT SPY/i), "MSFT");
    await user.click(screen.getByRole("button", { name: /^preview$/i }));

    await waitFor(() => expect(screen.getByRole("button", { name: /run backfill/i })).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /run backfill/i }));

    await waitFor(() => {
      expect(screen.getByText(/backfill complete — polygon/i)).toBeInTheDocument();
      expect(screen.getByText(/Bars:\s*512/i)).toBeInTheDocument();
    });
  });

  it("shows an error banner when previewBackfill rejects", async () => {
    const user = userEvent.setup();

    vi.spyOn(api, "previewBackfill").mockRejectedValueOnce(new Error("Provider offline"));

    render(
      <MemoryRouter initialEntries={["/data"]}>
        <DataOperationsScreen data={data} />
      </MemoryRouter>
    );

    await user.click(screen.getByRole("button", { name: /trigger backfill/i }));
    await user.type(screen.getByPlaceholderText(/AAPL MSFT SPY/i), "SPY");
    await user.click(screen.getByRole("button", { name: /^preview$/i }));

    await waitFor(() => {
      expect(screen.getByText("Provider offline")).toBeInTheDocument();
    });
  });
});
