import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ResearchScreen } from "@/screens/research-screen";
import type { ResearchWorkspaceResponse } from "@/types";

const data: ResearchWorkspaceResponse = {
  metrics: [
    { id: "1", label: "Runs", value: "24", delta: "+8%", tone: "success" },
    { id: "2", label: "Queued", value: "3", delta: "0%", tone: "default" },
    { id: "3", label: "Needs Review", value: "2", delta: "-1%", tone: "warning" },
    { id: "4", label: "Promotions", value: "5", delta: "+2%", tone: "default" }
  ],
  runs: [
    {
      id: "run-1",
      strategyName: "Mean Reversion FX",
      engine: "Meridian Native",
      mode: "paper",
      status: "Running",
      dataset: "FX Majors",
      window: "90d",
      pnl: "+4.2%",
      sharpe: "1.41",
      lastUpdated: "2m ago",
      notes: "Primary paper candidate."
    }
  ]
};

describe("ResearchScreen", () => {
  it("opens a focus-trapped detail dialog", async () => {
    const user = userEvent.setup();
    render(<ResearchScreen data={data} />);

    await user.click(screen.getByRole("button", { name: /open/i }));

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Primary paper candidate.")).toBeInTheDocument();
  });

  it("shows paper mode badge", () => {
    render(<ResearchScreen data={data} />);

    expect(screen.getByText("PAPER")).toBeInTheDocument();
  });
});
