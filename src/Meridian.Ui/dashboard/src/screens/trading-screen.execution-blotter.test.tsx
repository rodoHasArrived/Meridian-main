import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import * as blotterApi from "@/lib/api/execution-blotter.api";
import { ExecutionBlotterPanel } from "@/screens/trading-screen.execution-blotter";
import type { ExecutionBlotterSnapshot } from "@/types/execution-blotter.types";

vi.mock("@/lib/api/execution-blotter.api", () => ({
  getExecutionGatewayHealth: vi.fn(),
  getExecutionAccountSnapshot: vi.fn(),
  getExecutionBlotter: vi.fn(),
  upsizeExecutionPosition: vi.fn()
}));

const api = vi.mocked(blotterApi);

const blotter: ExecutionBlotterSnapshot = {
  positions: [
    {
      positionKey: "pos-1",
      symbol: "SPY",
      underlyingSymbol: "SPY",
      productDescription: "SPDR S&P 500 ETF",
      tradeId: null,
      quantity: 120,
      averageCostBasis: 480.5,
      marketPrice: 502.25,
      marketValue: 60_270,
      unrealisedPnl: 2_610,
      realisedPnl: 0,
      assetClass: "Equity",
      side: "Long",
      supportsClose: true,
      supportsUpsize: true
    },
    {
      positionKey: "pos-2",
      symbol: "TLT",
      underlyingSymbol: "TLT",
      productDescription: "iShares 20+ Year Treasury",
      tradeId: null,
      quantity: 40,
      averageCostBasis: 92.1,
      marketPrice: 90.4,
      marketValue: 3_616,
      unrealisedPnl: -68,
      realisedPnl: 0,
      assetClass: "Equity",
      side: "Long",
      supportsClose: true,
      supportsUpsize: false
    }
  ],
  isBrokerBacked: true,
  isLive: true,
  source: "Alpaca live account",
  statusMessage: "Book reconciled 12 seconds ago.",
  asOf: "2026-05-29T14:00:00Z"
};

function primeReads() {
  api.getExecutionGatewayHealth.mockResolvedValue({
    brokerName: "Alpaca",
    mode: "Live",
    isAvailable: true,
    asOf: "2026-05-29T14:00:00Z",
    selectedGatewayId: "alpaca-1"
  });
  api.getExecutionAccountSnapshot.mockResolvedValue({
    cash: 25_000,
    portfolioValue: 85_270,
    unrealisedPnl: 2_610,
    realisedPnl: -140,
    positionCount: 2,
    asOf: "2026-05-29T14:00:00Z"
  });
  api.getExecutionBlotter.mockResolvedValue(blotter);
}

afterEach(() => {
  vi.resetAllMocks();
});

describe("ExecutionBlotterPanel", () => {
  it("renders the gateway posture and the broker position book", async () => {
    primeReads();
    render(<ExecutionBlotterPanel />);

    const posture = await screen.findByLabelText("Execution gateway posture");
    expect(within(posture).getByText("Alpaca")).toBeInTheDocument();

    const table = screen.getByLabelText("Execution blotter positions");
    expect(within(table).getByText("SPY")).toBeInTheDocument();
    expect(within(table).getByText("TLT")).toBeInTheDocument();
    expect(screen.getByText("Broker book · live")).toBeInTheDocument();
  });

  it("labels a simulated book so paper rows are never read as the broker's", async () => {
    primeReads();
    api.getExecutionBlotter.mockResolvedValue({
      ...blotter,
      isBrokerBacked: false,
      isLive: false,
      source: "Paper simulator"
    });
    render(<ExecutionBlotterPanel />);

    expect(await screen.findByText("Simulated book · not live")).toBeInTheDocument();
  });

  it("reports an inactive execution host as inactive rather than as a failure", async () => {
    primeReads();
    api.getExecutionBlotter.mockRejectedValue(
      new ApiError({ path: "/api/execution/positions/blotter", status: 503, detail: "Execution services are not active." })
    );
    render(<ExecutionBlotterPanel />);

    expect(await screen.findByText(/Execution services are not active on this host/)).toBeInTheDocument();
    expect(screen.queryByText("Execution read needs attention")).not.toBeInTheDocument();
  });

  it("surfaces a genuine read failure", async () => {
    primeReads();
    api.getExecutionBlotter.mockRejectedValue(
      new ApiError({ path: "/api/execution/positions/blotter", status: 500, detail: "gateway exploded" })
    );
    render(<ExecutionBlotterPanel />);

    expect(await screen.findByText("gateway exploded")).toBeInTheDocument();
    expect(screen.getByText(/could not be read/)).toBeInTheDocument();
  });

  it("upsizes only the positions the server marks as supporting it", async () => {
    primeReads();
    api.upsizeExecutionPosition.mockResolvedValue({
      actionId: "act-1",
      status: "Accepted",
      message: "Added 10 SPY",
      occurredAt: "2026-05-29T14:01:00Z"
    });
    const user = userEvent.setup();
    render(<ExecutionBlotterPanel />);

    await screen.findByLabelText("Execution blotter positions");
    // TLT has supportsUpsize false, so exactly one button is offered.
    const upsizeButtons = screen.getAllByRole("button", { name: "Upsize" });
    expect(upsizeButtons).toHaveLength(1);

    await user.click(upsizeButtons[0]);
    await user.type(screen.getByLabelText("Quantity to add to SPY"), "10");
    await user.click(screen.getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(api.upsizeExecutionPosition).toHaveBeenCalledWith({ positionKey: "pos-1", quantity: 10 }));
    expect(await screen.findByText("Accepted: Added 10 SPY")).toBeInTheDocument();
  });

  it("refuses an upsize without a positive quantity instead of sending it", async () => {
    primeReads();
    const user = userEvent.setup();
    render(<ExecutionBlotterPanel />);

    await screen.findByLabelText("Execution blotter positions");
    await user.click(screen.getByRole("button", { name: "Upsize" }));
    await user.click(screen.getByRole("button", { name: "Confirm" }));

    expect(await screen.findByText("Enter a positive quantity to add to the position.")).toBeInTheDocument();
    expect(api.upsizeExecutionPosition).not.toHaveBeenCalled();
  });
});
