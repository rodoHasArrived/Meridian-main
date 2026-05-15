import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
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
    document.title = "Meridian";
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
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });
  });

  it("opens and closes the command palette with Control+K", async () => {
    const user = userEvent.setup();
    renderWithRouter(<App />, { initialEntries: ["/trading"] });
    const trigger = screen.getByRole("button", { name: "Open workstation command palette (Ctrl K)" });

    expect(screen.queryByRole("dialog", { name: "Open workstation command" })).not.toBeInTheDocument();
    expect(trigger).toHaveAttribute("aria-controls", "command-palette-dialog");
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(trigger).toHaveAttribute("aria-haspopup", "dialog");

    await user.keyboard("{Control>}k{/Control}");
    const dialog = screen.getByRole("dialog", { name: "Open workstation command" });
    expect(dialog).toHaveAttribute("id", "command-palette-dialog");
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(trigger).toHaveAccessibleName("Close workstation command palette (Ctrl K)");

    await user.keyboard("{Control>}k{/Control}");
    expect(screen.queryByRole("dialog", { name: "Open workstation command" })).not.toBeInTheDocument();
    expect(trigger).toHaveAttribute("aria-expanded", "false");
  });

  it("provides a skip link into the workbench content", () => {
    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.getByRole("link", { name: "Skip to workbench" })).toHaveAttribute("href", "#workbench-content");
    expect(screen.getByRole("main")).toHaveAttribute("id", "workbench-content");
  });

  it("has no basic accessibility violations in the trading shell", async () => {
    const { container } = renderWithRouter(<App />, { initialEntries: ["/trading"] });

    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("renders route-aware workflow continuity without relying on memorized navigation", () => {
    mockedUseWorkstationData.mockReturnValue({
      session: {
        displayName: "Ops Desk",
        role: "Operator",
        environment: "paper",
        activeWorkspace: "data",
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
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/data/quotes?symbol=MSFT"] });

    expect(screen.getByRole("region", { name: "Market Data To Paper continuity" })).toBeInTheDocument();
    expect(screen.getByText("Data / MSFT")).toBeInTheDocument();
    expect(screen.getByText("/data/quotes?symbol=MSFT")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "Market Data To Paper workflow steps" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Live quotes, current workflow step, Waiting" })).toHaveAttribute(
      "href",
      "/data/quotes?symbol=MSFT"
    );
    expect(screen.getByRole("link", { name: "Price alerts, next workflow step, Waiting" })).toHaveAttribute(
      "href",
      "/data/alerts"
    );
    expect(screen.getByRole("link", { name: "Continue workflow to Price alerts" })).toHaveAttribute(
      "href",
      "/data/alerts"
    );
  });

  it("sets the workstation document title on first direct route load without moving focus", async () => {
    renderWithRouter(<App />, { initialEntries: ["/settings"] });

    await waitFor(() => expect(document.title).toBe("Settings Workstation - Meridian"));
    expect(screen.getByRole("main")).not.toHaveFocus();
    expect(screen.queryByText("Settings Workstation loaded.")).not.toBeInTheDocument();
  });

  it("redirects the legacy Data Security Master route into Accounting", async () => {
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
      loading: false,
      error: null,
      workspaceErrors: {},
      refresh: vi.fn(),
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/data/security-master"] });

    await waitFor(() => expect(document.title).toBe("Accounting Workstation - Meridian"));
    expect(screen.getByLabelText("Accounting workspace, current route, Review")).toHaveAttribute("aria-current", "page");
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
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/settings#alpaca-provider-setup"] });

    expect(await screen.findByText("Settings Workstation loaded. Jumping to alpaca provider setup.")).toBeInTheDocument();
    await waitFor(() => expect(document.getElementById("alpaca-provider-setup")).not.toBeNull(), { timeout: 5000 });
    const alpacaSetup = document.getElementById("alpaca-provider-setup");
    expect(screen.getByRole("link", { name: "Open Alpaca paper provider setup" })).toHaveAttribute(
      "aria-current",
      "step"
    );
    await waitFor(() => expect(alpacaSetup).toHaveFocus(), { timeout: 5000 });
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

    expect(screen.queryByRole("dialog", { name: "Open workstation command" })).not.toBeInTheDocument();
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
      refreshPortfolio: vi.fn(),
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

  it("routes degraded bootstrap recovery to Settings capability diagnostics", async () => {
    const user = userEvent.setup();
    const refresh = vi.fn();

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
      loading: false,
      error: "Data workspace unavailable",
      workspaceErrors: {
        data: "Backfill summary timed out."
      },
      refresh,
      refreshTrading: vi.fn(),
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/trading"] });

    expect(screen.getByRole("status", { name: "Workstation bootstrap is partially degraded" })).toBeInTheDocument();
    const diagnosticsLink = screen.getByRole("link", {
      name: "Review Settings capability coverage for failed workstation slices"
    });
    expect(diagnosticsLink).toHaveAttribute("href", "/settings#backend-capability-coverage");

    await user.click(diagnosticsLink);

    await waitFor(() => expect(document.getElementById("backend-capability-coverage")).not.toBeNull(), { timeout: 5000 });
    const capabilityCoverage = document.getElementById("backend-capability-coverage");
    expect(capabilityCoverage).not.toBeNull();
    await waitFor(() => expect(capabilityCoverage).toHaveFocus());
  });

  it("renders the Portfolio route from the fetched portfolio workspace payload", async () => {
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
      refreshPortfolio: vi.fn(),
      upsertWorkflowPreset: vi.fn()
    });

    renderWithRouter(<App />, { initialEntries: ["/portfolio"] });

    const positionsTable = await screen.findByRole("table", { name: /open positions/i }, { timeout: 5000 });
    expect(within(positionsTable).getByText("NVDA")).toBeInTheDocument();
    expect(screen.getAllByText("Portfolio workspace").length).toBeGreaterThan(0);
  });
});
