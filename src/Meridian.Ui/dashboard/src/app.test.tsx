import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "@/app";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import { renderWithRouter } from "@/test/render";
import type { PortfolioWorkspaceResponse } from "@/types";

vi.mock("@/hooks/use-workstation-data", () => ({
  useWorkstationData: vi.fn()
}));

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    markWorkflowPresetUsed: vi.fn().mockResolvedValue(undefined)
  };
});

const mockedUseWorkstationData = vi.mocked(useWorkstationData);

const portfolio: PortfolioWorkspaceResponse = {
  metrics: [],
  positions: [
    {
      symbol: "NVDA",
      side: "Long",
      quantity: "12",
      averagePrice: "840.00",
      markPrice: "850.00",
      dayPnl: "+$120",
      unrealizedPnl: "+$120",
      exposure: "$10,200"
    }
  ],
  risk: {
    state: "Healthy",
    summary: "Portfolio endpoint risk posture.",
    netExposure: "$10,200",
    grossExposure: "$10,200",
    var95: "$500",
    maxDrawdown: "0%",
    buyingPowerUsed: "22%",
    activeGuardrails: []
  },
  brokerage: {
    provider: "Alpaca",
    account: "PF-ENDPOINT",
    environment: "paper",
    connection: "Connected",
    lastHeartbeat: "1s ago",
    orderIngress: "healthy",
    fillFeed: "healthy",
    notes: ""
  },
  runs: [],
  cashFlow: null
};

describe("App", () => {
  beforeEach(() => {
    mockedUseWorkstationData.mockReturnValue({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "trading",
        commandCount: 7
      },
      overview: null,
      research: null,
      trading: null,
      portfolio: null,
      dataOperations: null,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowError: null,
      loading: true,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn()
    });
  });

  it("opens and closes the command palette with Control+K", async () => {
    const user = userEvent.setup();
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.queryByRole("dialog", { name: "Open workspace" })).not.toBeInTheDocument();

    await user.keyboard("{Control>}k{/Control}");
    expect(screen.getByRole("dialog", { name: "Open workspace" })).toBeInTheDocument();

    await user.keyboard("{Control>}k{/Control}");
    expect(screen.queryByRole("dialog", { name: "Open workspace" })).not.toBeInTheDocument();
  });

  it("does not open the command palette shortcut while typing in an input", async () => {
    const user = userEvent.setup();
    renderWithRouter(
      <>
        <App />
        <label htmlFor="scratch-input">Scratch</label>
        <input id="scratch-input" />
      </>,
      { initialEntries: ["/trading"] }
    );

    await user.click(screen.getByLabelText("Scratch"));
    await user.keyboard("{Control>}k{/Control}");

    expect(screen.queryByRole("dialog", { name: "Open workspace" })).not.toBeInTheDocument();
  });

  it("opens and closes the mobile workspace navigation drawer", async () => {
    const user = userEvent.setup();
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.queryByRole("dialog", { name: "Workspace navigation" })).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Open workspace navigation" }));
    const navigationDialog = screen.getByRole("dialog", { name: "Workspace navigation" });
    expect(navigationDialog).toBeInTheDocument();
    expect(within(navigationDialog).getByLabelText("Current workspace: Trading, Review posture")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Close workspace navigation" }));
    expect(screen.queryByRole("dialog", { name: "Workspace navigation" })).not.toBeInTheDocument();
  });

  it("renders the Portfolio route from the fetched portfolio workspace payload", () => {
    mockedUseWorkstationData.mockReturnValue({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "portfolio",
        commandCount: 7
      },
      overview: null,
      research: null,
      trading: null,
      portfolio,
      dataOperations: null,
      governance: null,
      reporting: null,
      brokerageConnection: null,
      brokeragePortfolio: null,
      workflowLibrary: null,
      workflowPresets: null,
      workflowError: null,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/portfolio"] });

    const positionsTable = screen.getByRole("table", { name: /open positions/i });
    expect(within(positionsTable).getByText("NVDA")).toBeInTheDocument();
    expect(screen.getByText("Portfolio workspace")).toBeInTheDocument();
  });
});
