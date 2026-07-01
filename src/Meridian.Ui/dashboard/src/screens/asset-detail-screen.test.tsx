import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import * as api from "@/lib/api";
import { AssetDetailScreen } from "@/screens/asset-detail-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import type { CorporateAction, SecurityIdentityDrillIn, SecurityMasterEntry, TradingParameters } from "@/types";

vi.mock("@/lib/api", async () => {
  const actual = await vi.importActual<typeof import("@/lib/api")>("@/lib/api");
  return {
    ...actual,
    searchSecurities: vi.fn(),
    getSecurityDetail: vi.fn(),
    getSecurityIdentity: vi.fn(),
    getCorporateActions: vi.fn(),
    getTradingParameters: vi.fn(),
    getSecurityTrustSnapshot: vi.fn(),
    getOperatorOverrides: vi.fn()
  };
});

const entry: SecurityMasterEntry = {
  securityId: "sec-aapl",
  displayName: "Apple Inc.",
  status: "Active",
  classification: {
    assetClass: "Equity",
    subType: "Common Stock",
    primaryIdentifierKind: "Ticker",
    primaryIdentifierValue: "AAPL"
  },
  economicDefinition: {
    currency: "USD",
    version: 3,
    effectiveFrom: "2020-01-01",
    effectiveTo: null,
    subType: null,
    assetFamily: null,
    issuerType: null
  }
};

const identity: SecurityIdentityDrillIn = {
  securityId: "sec-aapl",
  displayName: "Apple Inc.",
  assetClass: "Equity",
  status: "Active",
  version: 3,
  effectiveFrom: "2020-01-01",
  effectiveTo: null,
  identifiers: [],
  aliases: []
};

const corporateActions: CorporateAction[] = [];

const tradingParameters: TradingParameters = {
  securityId: "sec-aapl",
  lotSize: 100,
  tickSize: 0.01,
  contractMultiplier: null,
  marginRequirementPct: 25,
  tradingHoursUtc: "13:30-20:00",
  circuitBreakerThresholdPct: 10,
  asOf: "2026-06-30T00:00:00Z"
};

async function renderScreen(initialEntry: string) {
  const result = renderWithRouter(<AssetDetailScreen />, { initialEntries: [initialEntry] });
  await waitForAsyncEffects();
  return result;
}

describe("AssetDetailScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.getSecurityIdentity).mockResolvedValue(identity);
    vi.mocked(api.getCorporateActions).mockResolvedValue(corporateActions);
    vi.mocked(api.getTradingParameters).mockResolvedValue(tradingParameters);
    vi.mocked(api.getOperatorOverrides).mockResolvedValue({ securityId: "sec-aapl", values: {}, updatedBy: "", updatedAt: "" });
    vi.mocked(api.getSecurityTrustSnapshot).mockResolvedValue({ securityId: "sec-aapl", retrievedAtUtc: "2026-06-30T00:00:00Z" });
  });

  it("shows a search box when no securityId is provided", async () => {
    await renderScreen("/accounting/security-master/detail");

    expect(screen.getByRole("heading", { name: "Asset Detail" })).toBeInTheDocument();
    expect(screen.getByLabelText("Search securities")).toBeInTheDocument();
  });

  it("searches securities and navigates to the selected security's detail", async () => {
    vi.mocked(api.searchSecurities).mockResolvedValue([entry]);
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);
    const user = userEvent.setup();

    await renderScreen("/accounting/security-master/detail");

    await user.type(screen.getByLabelText("Search securities"), "apple");

    const result = await screen.findByRole("button", { name: /Apple Inc\./ });
    await user.click(result);

    expect(await screen.findByRole("heading", { name: "Apple Inc." })).toBeInTheDocument();
  });

  it("renders the overview, passport, details, and trading-parameter tabs for a resolved security", async () => {
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);

    await renderScreen("/accounting/security-master/detail?securityId=sec-aapl");

    expect(await screen.findByRole("heading", { name: "Apple Inc." })).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByText("No corporate actions recorded for this security.")).toBeInTheDocument();

    const user = userEvent.setup();
    await user.click(screen.getByRole("tab", { name: "Passport" }));
    expect(await screen.findByTestId("security-passport-editor")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Trading Parameters" }));
    await waitFor(() => expect(screen.getByText("100")).toBeInTheDocument());
    expect(screen.getByText("25%")).toBeInTheDocument();
  });

  it("renders a not-found state when the security lookup fails", async () => {
    vi.mocked(api.getSecurityDetail).mockRejectedValueOnce(new Error("not found"));

    await renderScreen("/accounting/security-master/detail?securityId=missing-id");

    expect(await screen.findByText("Asset not found")).toBeInTheDocument();
    expect(screen.getByText(/No security matching "missing-id" was found/)).toBeInTheDocument();
  });
});
