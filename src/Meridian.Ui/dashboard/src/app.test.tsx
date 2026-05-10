import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "@/app";
import { useWorkstationData } from "@/hooks/use-workstation-data";
import { renderWithRouter } from "@/test/render";
import { useNavigate } from "react-router-dom";
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
      usingDevelopmentFixtures: false,
      loading: true,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      upsertWorkflowPreset: vi.fn()
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

  it("provides a skip link into the workbench content", () => {
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.getByRole("link", { name: "Skip to workbench" })).toHaveAttribute("href", "#workbench-content");
    expect(screen.getByRole("main")).toHaveAttribute("id", "workbench-content");
  });

  it("announces route changes and moves focus to the workbench", async () => {
    const user = userEvent.setup();

    function Harness() {
      const navigate = useNavigate();
      return (
        <>
          <button type="button" onClick={() => navigate("/portfolio")}>Route to portfolio</button>
          <App />
        </>
      );
    }

    renderWithRouter(<Harness />, { initialEntries: ["/trading"] });

    await user.click(screen.getByRole("button", { name: "Route to portfolio" }));

    expect(await screen.findByText("Portfolio Workstation loaded.")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByRole("main")).toHaveFocus());
  });

  it("announces and focuses hash-targeted workflow links", async () => {
    mockedUseWorkstationData.mockReturnValue({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "settings",
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
      usingDevelopmentFixtures: true,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/settings#alpaca-provider-setup"] });

    const alpacaSetup = document.getElementById("alpaca-provider-setup");
    expect(alpacaSetup).not.toBeNull();
    expect(await screen.findByText("Settings Workstation loaded. Jumping to alpaca provider setup.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open Alpaca paper provider setup" })).toHaveAttribute(
      "aria-current",
      "step"
    );
    await waitFor(() => expect(alpacaSetup).toHaveFocus());
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
    expect(within(navigationDialog).getByLabelText("Trading workspace, current route, Review")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Close workspace navigation" }));
    expect(screen.queryByRole("dialog", { name: "Workspace navigation" })).not.toBeInTheDocument();
  });

  it("labels fixture-backed bootstrap data as demo data and can retry live data", async () => {
    const user = userEvent.setup();
    const refresh = vi.fn();

    mockedUseWorkstationData.mockReturnValue({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "settings",
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
      usingDevelopmentFixtures: true,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh,
      refreshTrading: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/settings"] });

    expect(screen.getByText("Demo data")).toBeInTheDocument();
    expect(screen.getByText("Showing local fixture responses because the Meridian API host is unavailable.")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Demo workflow" })).toBeInTheDocument();
    expect(screen.getByText("Evidence path")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open sample watchlist demo lane" })).toHaveAttribute("href", "/data/watchlist");
    expect(screen.getByRole("link", { name: "Open sample live quotes for AAPL" })).toHaveAttribute("href", "/data/quotes?symbol=AAPL");
    expect(screen.getByRole("link", { name: "Open sample readiness console" })).toHaveAttribute("href", "/trading/readiness");
    expect(screen.getByRole("link", { name: "Open Alpaca paper provider setup" })).toHaveAttribute(
      "href",
      "/settings#alpaca-provider-setup"
    );
    await user.click(screen.getByRole("button", { name: "Retry Meridian API host and reload live workstation data" }));
    expect(refresh).toHaveBeenCalledOnce();
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
      usingDevelopmentFixtures: false,
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/portfolio"] });

    const positionsTable = screen.getByRole("table", { name: /open positions/i });
    expect(within(positionsTable).getByText("NVDA")).toBeInTheDocument();
    expect(screen.getByText("Portfolio workspace")).toBeInTheDocument();
  });
});
