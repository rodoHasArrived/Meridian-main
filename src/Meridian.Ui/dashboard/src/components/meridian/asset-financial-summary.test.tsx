import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { axe } from "jest-axe";
import { AssetFinancialSummary } from "./asset-financial-summary";
import { getAssetOperations, getLiveQuotesSnapshot } from "@/lib/api";
import type { AssetOperationsDetail, BookPosition, QuotesSnapshotResponse } from "@/types";

vi.mock("@/lib/api", () => ({
  getAssetOperations: vi.fn(),
  getLiveQuotesSnapshot: vi.fn()
}));

vi.mock("./asset-market-valuation", () => ({
  AssetMarketValuation: ({ query }: { query: { economicStateId: string } }) => (
    <section aria-label="Market valuation">Retained state: {query.economicStateId}</section>
  )
}));

const position: BookPosition = {
  positionId: "position-1",
  securityId: "sec-aapl",
  roleId: "holder",
  positionSide: "Long",
  status: "Open",
  effectiveFrom: "2026-01-01",
  version: 1,
  primaryAccountId: "account-1",
  evidenceLinks: [],
  bookContext: {
    ledgerBookId: "book-1",
    fundProfileId: "fund-1",
    fundStructureNodeId: "node-1",
    fundStructureNodeKind: "Fund",
    displayName: "GAAP book",
    baseCurrency: "USD",
    accountingBasis: "Gaap",
    accountingPolicyId: "policy-1",
    accountingPolicyVersion: "1"
  },
  currentEconomicState: {
    economicStateId: "state-1",
    positionId: "position-1",
    asOfDate: "2026-11-20",
    currency: "USD",
    version: 2,
    quantity: 10,
    unitCost: 123.45,
    carryingAmount: 1234.5,
    evidenceLinks: []
  }
};
const operations: AssetOperationsDetail = {
  subject: {
    securityId: "sec-aapl",
    displayName: "Apple",
    assetClass: "Equity",
    primaryIdentifier: "AAPL",
    operationalProfile: []
  },
  termsHistory: [],
  lifecycleEvents: [],
  cashFlowProjectionRuns: [],
  projectedCashFlows: [],
  actualActivity: [],
  reconciliationRuns: [],
  reconciliationResults: [],
  ledgerProjections: [],
  workflowAudit: [],
  bookPositions: [position],
  readiness: {
    securityId: "sec-aapl",
    status: "Ready",
    capabilities: [],
    readyCapabilities: [],
    missingCapabilities: [],
    blockers: [],
    evaluatedAt: "2026-11-20",
    sourceDomain: "test",
    sourceEntityId: "sec-aapl"
  }
};
const quotes: QuotesSnapshotResponse = {
  timestamp: "2026-11-20T16:00:00Z",
  count: 1,
  quotes: [
    {
      symbol: "AAPL",
      timestamp: "2026-11-20T15:59:00Z",
      bidPrice: 140,
      askPrice: 141,
      bidSize: 100,
      askSize: 100,
      midPrice: 140.5,
      spread: 1,
      lastPrice: 140.6,
      lastSize: 50,
      lastTradeTimestamp: "2026-11-20T15:58:00Z",
      sequenceNumber: 1,
      streamId: "provider-stream",
      venue: "NASDAQ",
      session: null
    }
  ]
};
const props = {
  securityId: "sec-aapl",
  symbol: "AAPL",
  currency: "USD",
  identifierKind: "Ticker"
};
const renderSummary = (additional = {}) =>
  render(<AssetFinancialSummary {...props} {...additional} />, {
    wrapper: MemoryRouter
  });

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getAssetOperations).mockResolvedValue(operations);
  vi.mocked(getLiveQuotesSnapshot).mockResolvedValue(quotes);
});

it("shows retained book amounts and distinguishes symbol quotes from valuations", async () => {
  const { container } = renderSummary();
  expect(await screen.findByText("1,234.5")).toBeInTheDocument();
  expect(screen.getByText(/GAAP book \(book-1\).*Account account-1/)).toBeInTheDocument();
  expect(screen.getByText(/State state-1 · Version 2/)).toHaveTextContent("As of 2026-11-20");
  expect(screen.getByText("140.6")).toBeInTheDocument();
  expect(screen.getByText(/quote currency is not supplied/)).toBeInTheDocument();
  expect(screen.getByRole("region", { name: "Market valuation" })).toHaveTextContent("state-1");
  expect(getAssetOperations).toHaveBeenCalledWith(
    "sec-aapl",
    expect.objectContaining({ allowDevelopmentFallback: false })
  );
  expect((await axe(container)).violations).toHaveLength(0);
});

it("requires selection for multiple positions and filters exact book/account/date scope", async () => {
  const other = {
    ...position,
    positionId: "position-2",
    primaryAccountId: "account-2",
    bookContext: { ...position.bookContext, ledgerBookId: "book-2" },
    currentEconomicState: {
      ...position.currentEconomicState!,
      positionId: "position-2",
      carryingAmount: 5678
    }
  };
  vi.mocked(getAssetOperations).mockResolvedValue({
    ...operations,
    bookPositions: [position, other]
  });
  const result = renderSummary();
  const select = await screen.findByRole("combobox", {
    name: "Book position"
  });
  expect(screen.queryByText("1,234.5")).not.toBeInTheDocument();
  await userEvent.setup().selectOptions(select, "position-2");
  expect(screen.getByText("5,678")).toBeInTheDocument();
  result.rerender(<AssetFinancialSummary {...props} ledgerBookId="book-1" accountId="account-1" asOf="2026-11-20" />);
  expect(screen.getByText("1,234.5")).toBeInTheDocument();
  expect(screen.queryByText("5,678")).not.toBeInTheDocument();
  result.rerender(<AssetFinancialSummary {...props} ledgerBookId="book-1" accountId="account-2" />);
  expect(screen.getByText(/No retained book position matches/)).toBeInTheDocument();
  expect(screen.queryByText("1,234.5")).not.toBeInTheDocument();
});

it("withholds foreign identity and currency mismatches and permits retry", async () => {
  vi.mocked(getAssetOperations).mockResolvedValue({
    ...operations,
    subject: { ...operations.subject, securityId: "other-security" }
  });
  vi.mocked(getLiveQuotesSnapshot).mockResolvedValue({
    ...quotes,
    quotes: [{ ...quotes.quotes[0]!, symbol: "MSFT" }]
  });
  const result = renderSummary();
  expect(await screen.findByText("Couldn’t load book positions.")).toBeInTheDocument();
  expect(screen.getByText("No quote returned for this ticker.")).toBeInTheDocument();
  expect(screen.queryByText("140.6")).not.toBeInTheDocument();
  vi.mocked(getAssetOperations).mockResolvedValue(operations);
  await userEvent.setup().click(screen.getByRole("button", { name: "Retry book positions" }));
  expect(await screen.findByText("1,234.5")).toBeInTheDocument();
  result.rerender(<AssetFinancialSummary {...props} currency="EUR" />);
  expect(screen.getByText(/matching EUR economic state is unavailable/)).toBeInTheDocument();
  expect(screen.queryByText("1,234.5")).not.toBeInTheDocument();
});

it("keeps successful sections available while a failed quote can be retried", async () => {
  vi.mocked(getLiveQuotesSnapshot).mockRejectedValueOnce(new Error("offline"));
  renderSummary();
  expect(await screen.findByText("Couldn’t load the symbol quote.")).toBeInTheDocument();
  expect(screen.getByText("1,234.5")).toBeInTheDocument();
  await userEvent.setup().click(screen.getByRole("button", { name: "Retry quote" }));
  expect(await screen.findByText("140.6")).toBeInTheDocument();
});

it("clears old amounts immediately and ignores delayed responses after an asset switch", async () => {
  let finishOld!: (value: AssetOperationsDetail) => void;
  vi.mocked(getAssetOperations).mockImplementationOnce(
    () =>
      new Promise((resolve) => {
        finishOld = resolve;
      })
  );
  const result = renderSummary();
  await waitFor(() => expect(getAssetOperations).toHaveBeenCalledTimes(1));
  vi.mocked(getAssetOperations).mockResolvedValue({
    ...operations,
    subject: { ...operations.subject, securityId: "sec-msft" },
    bookPositions: []
  });
  result.rerender(<AssetFinancialSummary {...props} securityId="sec-msft" symbol="MSFT" />);
  expect(screen.queryByText("140.6")).not.toBeInTheDocument();
  await screen.findByText(/No retained book position matches/);
  await act(async () => {
    finishOld(operations);
  });
  expect(screen.queryByText("1,234.5")).not.toBeInTheDocument();
  expect(screen.queryByText("140.6")).not.toBeInTheDocument();
});

it("does not treat non-ticker identifiers as market symbols or infer unsupported account scope", async () => {
  renderSummary({
    symbol: "US91282CHT18",
    identifierKind: "ISIN",
    scopeUnavailableReason: "The selected fund account cannot be matched to a book position."
  });
  expect(await screen.findByText("A ticker is required to look up a symbol quote.")).toBeInTheDocument();
  expect(getLiveQuotesSnapshot).not.toHaveBeenCalled();
  expect(screen.queryByText("1,234.5")).not.toBeInTheDocument();
  expect(screen.getByText(/selected fund account cannot be matched/)).toBeInTheDocument();
});
