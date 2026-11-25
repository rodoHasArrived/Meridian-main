import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { AssetMarketValuation } from "./asset-market-valuation";
import { getAssetMarketValuation } from "@/lib/api/asset-market-valuation.api";
import { marketValuationFixture, marketValuationQuery } from "@/test/asset-market-valuation-fixture";
import type { AssetPositionMarketValuation } from "@/types/asset-market-valuation";
vi.mock("@/lib/api/asset-market-valuation.api", () => ({ getAssetMarketValuation: vi.fn() }));
beforeEach(() => {
  vi.resetAllMocks();
  vi.mocked(getAssetMarketValuation).mockResolvedValue(marketValuationFixture);
});
const component = (query = marketValuationQuery) => (
  <AssetMarketValuation securityId="sec-aapl" query={query} currency="USD" />
);
it("shows server amounts, explicit comparison basis and source evidence", async () => {
  const { container } = render(component());
  expect(await screen.findByText("1,406.00")).toBeInTheDocument();
  expect(screen.getByText("171.50")).toBeInTheDocument();
  expect(screen.getByText(/mark-to-carrying comparison/)).toBeInTheDocument();
  await userEvent.click(screen.getByText("Valuation calculation and source evidence"));
  expect(screen.getByText("receipt-1")).toBeVisible();
  expect((await axe(container)).violations).toHaveLength(0);
});
it("keeps partial results and metric-specific blockers separate from zero", async () => {
  vi.mocked(getAssetMarketValuation).mockResolvedValue({
    ...marketValuationFixture,
    status: "Partial",
    unrealizedPnl: null,
    blockers: [
      { metric: "UnrealizedPnl", code: "CarryingBasisUnknown", reason: "The accrued-interest basis is unavailable." }
    ]
  });
  render(component());
  expect(await screen.findByText("1,406.00")).toBeInTheDocument();
  expect(screen.getByText("Unavailable")).toBeInTheDocument();
  expect(screen.getByRole("list", { name: "Valuation blockers" })).toHaveTextContent("accrued-interest basis");
});
it("clears amounts on scope changes and ignores delayed prior-state responses", async () => {
  let finish!: (value: AssetPositionMarketValuation) => void;
  vi.mocked(getAssetMarketValuation).mockReturnValueOnce(
    new Promise((resolve) => {
      finish = resolve;
    })
  );
  const result = render(component());
  vi.mocked(getAssetMarketValuation).mockResolvedValue({
    ...marketValuationFixture,
    economicStateId: "state-2",
    marketValue: "2000.00"
  });
  result.rerender(component({ ...marketValuationQuery, economicStateId: "state-2" }));
  expect(await screen.findByText("2,000.00")).toBeInTheDocument();
  await act(async () => finish(marketValuationFixture));
  expect(screen.queryByText("1,406.00")).not.toBeInTheDocument();
});
it("recovers from a failed request without showing stale amounts", async () => {
  vi.mocked(getAssetMarketValuation).mockRejectedValueOnce(new Error("offline"));
  render(component());
  expect(await screen.findByRole("alert")).toHaveTextContent("Amounts are withheld");
  await userEvent.click(screen.getByRole("button", { name: "Retry valuation" }));
  expect(await screen.findByText("1,406.00")).toBeInTheDocument();
});
