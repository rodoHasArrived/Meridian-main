import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { DataOperationsScreen } from "@/screens/data-operations-screen";
import type { DataOperationsWorkspaceResponse } from "@/types";

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
      <MemoryRouter initialEntries={["/data-operations"]}>
        <DataOperationsScreen data={data} />
      </MemoryRouter>
    );

    expect(screen.getByText("Provider health")).toBeInTheDocument();
    expect(screen.getByText("Backfill queue")).toBeInTheDocument();
    expect(screen.getByText("Recent exports")).toBeInTheDocument();
    expect(screen.getByText("Polygon")).toBeInTheDocument();
  });

  it("adapts the hero copy for deep-link routes", () => {
    render(
      <MemoryRouter initialEntries={["/data-operations/backfills"]}>
        <DataOperationsScreen data={data} />
      </MemoryRouter>
    );

    expect(screen.getByText("Backfill queue focus")).toBeInTheDocument();
  });
});
