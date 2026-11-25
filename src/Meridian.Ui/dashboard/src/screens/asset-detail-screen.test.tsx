import { act, screen, waitFor, within } from "@testing-library/react";
import { ApiError } from "@/lib/api-errors";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { Link, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import * as api from "@/lib/api";
import { readRecordWorkspaceContext, withoutRecordWorkspaceContext } from "@/lib/record-workspace-context";
import { AssetDetailPanel, AssetDetailScreen } from "@/screens/asset-detail-screen";
import { InstrumentDossierScreen } from "@/screens/instrument-dossier-screen";
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

vi.mock("@/components/meridian/asset-financial-summary", () => ({
  AssetFinancialSummary: () => <section aria-label="Financial context">Retained financial context</section>
}));
vi.mock("@/components/meridian/asset-passport-summary", () => ({
  AssetPassportSummary: ({ onOpenPassport }: { onOpenPassport: () => void }) => (
    <button onClick={onOpenPassport}>Open governed passport</button>
  )
}));

vi.mock("@/components/meridian/security-instrument-terms", () => ({
  SecurityInstrumentTerms: ({ expectedVersion, onOpenFull }: { expectedVersion: number; onOpenFull?: () => void }) => (
    <section aria-label="Instrument terms">Retained terms version {expectedVersion}
      {onOpenFull ? <button onClick={onOpenFull}>View all terms</button> : null}
    </section>
  )
}));

function RouteProbe() {
  const location = useLocation();
  const navigate = useNavigate();
  return (
    <>
      <output aria-label="Current route">{location.search}</output>
      <button onClick={() => navigate(-1)}>Back in history</button>
      <button onClick={() => navigate("?securityId=sec-msft&symbol=MSFT&tab=events&accountId=account-1")}>
        Select Microsoft route
      </button>
    </>
  );
}

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
const bondEntry: SecurityMasterEntry = {
  ...entry,
  securityId: "bond-1",
  displayName: "Synthetic Treasury 2030",
  classification: { ...entry.classification, assetClass: "Bond", primaryIdentifierValue: "US-SYNTHETIC" }
};

function WorkflowDestination() {
  const location = useLocation();
  return <output aria-label="Workflow destination">{location.pathname}{location.search}</output>;
}

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
  const result = renderWithRouter(<AssetDetailScreen />, {
    initialEntries: [initialEntry]
  });
  await waitForAsyncEffects();
  return result;
}

describe("AssetDetailScreen", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    window.localStorage.clear();
    vi.mocked(api.getSecurityIdentity).mockResolvedValue(identity);
    vi.mocked(api.getCorporateActions).mockResolvedValue(corporateActions);
    vi.mocked(api.getTradingParameters).mockResolvedValue(tradingParameters);
    vi.mocked(api.getOperatorOverrides).mockResolvedValue({
      securityId: "sec-aapl",
      values: {},
      updatedBy: "",
      updatedAt: ""
    });
    vi.mocked(api.getSecurityTrustSnapshot).mockResolvedValue({
      securityId: "sec-aapl",
      retrievedAtUtc: "2026-06-30T00:00:00Z"
    });
  });

  it("keeps embedded asset workflows inside the dossier reference tab and preserves operating scope", async () => {
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);
    renderWithRouter(<>
      <Routes><Route path="/records/instrument/:id" element={<InstrumentDossierScreen />} /></Routes>
      <RouteProbe />
    </>, { initialEntries: ["/records/instrument/sec-aapl?tab=reference&ledgerBookId=book-1&accountId=account-1&asOf=2026-11-21&securityId=foreign-query"] });
    await screen.findByRole("heading", { name: "Apple Inc.", level: 1 });
    await screen.findByRole("heading", { name: "Apple Inc.", level: 2 });
    expect(screen.getByRole("tab", { name: "Reference" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveAttribute("aria-selected", "true");
    expect(screen.queryByRole("combobox", { name: "Switch asset" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Open instrument dossier" })).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole("tab", { name: "Events" }));
    expect(screen.getByRole("tab", { name: "Reference" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Events" })).toHaveAttribute("aria-selected", "true");
    let query = new URLSearchParams(screen.getByLabelText("Current route").textContent ?? "");
    expect(query.get("tab")).toBe("reference");
    expect(query.get("assetTab")).toBe("events");
    expect(query.get("ledgerBookId")).toBe("book-1");
    expect(query.get("accountId")).toBe("account-1");
    expect(query.get("asOf")).toBe("2026-11-21");
    expect(api.getSecurityDetail).not.toHaveBeenCalledWith("foreign-query", expect.anything());

    await userEvent.click(screen.getByRole("tab", { name: "Overview" }));
    await userEvent.click(screen.getByRole("button", { name: "View all terms" }));
    expect(screen.getByRole("tab", { name: "Reference" })).toHaveAttribute("aria-selected", "true");
    expect(screen.getByRole("tab", { name: "Reference data" })).toHaveAttribute("aria-selected", "true");
    query = new URLSearchParams(screen.getByLabelText("Current route").textContent ?? "");
    expect(query.get("tab")).toBe("reference");
    expect(query.get("assetTab")).toBe("reference");
    expect(query.get("ledgerBookId")).toBe("book-1");
  });

  it("follows a changed dossier path identity and ignores the previous embedded asset response", async () => {
    const microsoft = { ...entry, securityId: "sec-msft", displayName: "Microsoft Corp.", classification: { ...entry.classification, primaryIdentifierValue: "MSFT" } };
    let resolvePrevious!: (value: SecurityMasterEntry) => void;
    let appleReads = 0;
    vi.mocked(api.getSecurityDetail).mockImplementation(async (securityId) => {
      if (securityId === "sec-aapl") {
        if (++appleReads === 1) return entry;
        return new Promise((resolve) => { resolvePrevious = resolve; });
      }
      return microsoft;
    });
    vi.mocked(api.getSecurityIdentity).mockImplementation(async (securityId) => ({ ...identity, securityId, displayName: securityId === "sec-msft" ? microsoft.displayName : entry.displayName }));
    vi.mocked(api.getTradingParameters).mockImplementation(async (securityId) => ({ ...tradingParameters, securityId }));
    const scope = "?tab=reference&assetTab=events&ledgerBookId=book-1&accountId=account-1&asOf=2026-11-21";
    renderWithRouter(<>
      <Routes><Route path="/records/instrument/:id" element={<InstrumentDossierScreen />} /></Routes>
      <Link to={"/records/instrument/sec-msft" + scope}>Open Microsoft dossier</Link>
      <WorkflowDestination />
    </>, { initialEntries: ["/records/instrument/sec-aapl" + scope] });
    await screen.findByRole("heading", { name: "Apple Inc.", level: 1 });
    await waitFor(() => expect(appleReads).toBe(2));
    const previousSignal = vi.mocked(api.getSecurityDetail).mock.calls.filter(([id]) => id === "sec-aapl").at(-1)?.[1]?.signal;
    await userEvent.click(screen.getByRole("link", { name: "Open Microsoft dossier" }));
    await screen.findByRole("heading", { name: "Microsoft Corp.", level: 1 });
    await screen.findByRole("heading", { name: "Microsoft Corp.", level: 2 });
    expect(previousSignal?.aborted).toBe(true);
    await act(async () => { resolvePrevious(entry); await Promise.resolve(); });
    expect(screen.queryByRole("heading", { name: "Apple Inc." })).not.toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Events" })).toHaveAttribute("aria-selected", "true");
    const destination = new URL(screen.getByLabelText("Workflow destination").textContent ?? "", "https://meridian.local");
    expect(destination.pathname).toBe("/records/instrument/sec-msft");
    expect(destination.searchParams.get("tab")).toBe("reference");
    expect(destination.searchParams.get("assetTab")).toBe("events");
    expect(destination.searchParams.get("ledgerBookId")).toBe("book-1");
    expect(destination.searchParams.get("accountId")).toBe("account-1");
    expect(destination.searchParams.get("asOf")).toBe("2026-11-21");
  });

  it("retries an unavailable embedded asset without offering a search that cannot change its parent identity", async () => {
    vi.mocked(api.getSecurityDetail).mockRejectedValueOnce(new Error("Unavailable")).mockResolvedValueOnce(entry);
    renderWithRouter(<AssetDetailPanel securityId="sec-aapl" embedded />, {
      initialEntries: ["/records/instrument/sec-aapl?tab=reference&ledgerBookId=book-1"]
    });
    await screen.findByRole("heading", { name: "Couldn’t load asset detail" });
    expect(screen.queryByRole("button", { name: "Search assets" })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Retry asset" }));
    expect(await screen.findByRole("heading", { name: "Apple Inc." })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Overview" })).toHaveAttribute("aria-selected", "true");
  });

  it("shows a search box when no securityId is provided", async () => {
    await renderScreen("/accounting/security-master/detail");

    expect(screen.getByRole("heading", { name: "Asset Detail" })).toBeInTheDocument();
    expect(screen.getByLabelText("Search securities")).toBeInTheDocument();
    expect(screen.getByLabelText("Asset detail search readiness")).toHaveTextContent("Start from a known instrument");
    expect(screen.getByLabelText("Suggested asset detail searches")).toHaveTextContent("AAPL");
    expect(screen.getByText("Review areas")).toBeInTheDocument();
    expect(
      screen.getByRole("complementary", {
        name: "Asset detail example searches and next actions"
      })
    ).toHaveTextContent("Example searches");
    expect(
      screen.getByRole("region", {
        name: "Asset detail example searches"
      })
    ).toHaveTextContent("Equity example");
    expect(screen.getByRole("region", { name: "Asset detail next actions" })).toHaveTextContent("Open live quotes");
    expect(
      screen
        .getAllByRole("link", { name: "Open Security Master" })
        .some((link) => link.getAttribute("href") === "/accounting/security-master")
    ).toBe(true);
  });

  it("has no basic accessibility violations in the search-ready state", async () => {
    const { container } = await renderScreen("/accounting/security-master/detail");

    expect(screen.getByLabelText("Asset detail search readiness")).toBeInTheDocument();
    const results = await axe(container);
    expect(results.violations).toHaveLength(0);
  });

  it("opens a uniquely matched portfolio holding symbol directly", async () => {
    vi.mocked(api.searchSecurities).mockResolvedValue([entry]);
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);

    await renderScreen("/portfolio/asset-detail?symbol=AAPL");

    expect(await screen.findByRole("heading", { name: "Apple Inc." })).toBeInTheDocument();
    expect(screen.queryByLabelText("Search securities")).not.toBeInTheDocument();
    expect(api.searchSecurities).toHaveBeenCalledWith("AAPL");
  });

  it("keeps search-ready focus order on search, result, saved views, and next actions", async () => {
    vi.mocked(api.searchSecurities).mockResolvedValue([entry]);
    const user = userEvent.setup();

    await renderScreen("/portfolio/asset-detail?query=apple");
    const result = await screen.findByRole("button", {
      name: /Apple Inc\./
    });

    await user.tab();
    expect(screen.getByLabelText("Search securities")).toHaveFocus();

    await user.tab();
    expect(result).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("button", { name: "AAPL" })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("button", { name: "US91282CHT18" })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("button", { name: "MSFT" })).toHaveFocus();

    await user.tab();
    expect(screen.getByRole("link", { name: "Open Security Master" })).toHaveFocus();
  });

  it("searches securities and navigates to the selected security's detail", async () => {
    vi.mocked(api.searchSecurities).mockResolvedValue([entry]);
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);
    const user = userEvent.setup();

    await renderScreen("/accounting/security-master/detail");

    await user.type(screen.getByLabelText("Search securities"), "apple");

    const result = await screen.findByRole("button", {
      name: /Apple Inc\./
    });
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
    const identity = within(screen.getByRole("region", { name: "Instrument identity" }));
    expect(identity.getByText("Ticker AAPL")).toBeVisible();
    expect(identity.getByText("sec-aapl")).not.toBeVisible();
    await user.click(identity.getByText("Technical identity"));
    expect(identity.getByText("sec-aapl")).toBeVisible();
    await user.click(screen.getByRole("tab", { name: "Passport" }));
    expect(await screen.findByTestId("security-passport-editor")).toBeInTheDocument();

    await user.click(screen.getByRole("tab", { name: "Trading Parameters" }));
    await waitFor(() => expect(screen.getByText("100")).toBeInTheDocument());
    expect(screen.getByText("25%")).toBeInTheDocument();
  });

  it("renders a not-found state when the security lookup fails", async () => {
    vi.mocked(api.getSecurityDetail).mockRejectedValueOnce(new ApiError({ path: "/security/missing-id", status: 404 }));

    await renderScreen("/accounting/security-master/detail?securityId=missing-id");

    expect(await screen.findByText("Asset not found")).toBeInTheDocument();
    expect(screen.getByText(/No security matching "missing-id" was found/)).toBeInTheDocument();
  });
  it("distinguishes a service failure from a missing asset and allows retry", async () => {
    vi.mocked(api.getSecurityDetail)
      .mockRejectedValueOnce(new Error("network unavailable"))
      .mockResolvedValueOnce(entry);
    await renderScreen("/portfolio/asset-detail?securityId=sec-aapl");
    expect(await screen.findByText("Couldn’t load asset detail")).toBeInTheDocument();
    expect(screen.queryByText("Asset not found")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Retry asset" }));
    expect(await screen.findByRole("heading", { name: "Apple Inc." })).toBeInTheDocument();
  });

  it("keeps useful asset content while a failed events section retries independently", async () => {
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);
    vi.mocked(api.getCorporateActions).mockRejectedValueOnce(new Error("events offline")).mockResolvedValueOnce([]);
    await renderScreen("/portfolio/asset-detail?securityId=sec-aapl");
    expect(await screen.findByRole("heading", { name: "Apple Inc." })).toBeInTheDocument();
    expect(screen.getByText(/Event availability is unknown/)).toBeInTheDocument();
    expect(screen.queryByText("No corporate actions recorded for this security.")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Retry corporate actions" }));
    expect(await screen.findByText("No corporate actions recorded for this security.")).toBeInTheDocument();
    expect(api.getSecurityDetail).toHaveBeenCalledTimes(1);
  });

  it("restores deep-linked tabs, preserves scope, and supports browser history", async () => {
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);
    renderWithRouter(
      <>
        <AssetDetailScreen />
        <RouteProbe />
      </>,
      {
        initialEntries: ["/portfolio/asset-detail?securityId=sec-aapl&tab=events&accountId=account-1"]
      }
    );
    expect(await screen.findByRole("tab", { name: "Events" })).toHaveAttribute("aria-selected", "true");
    await userEvent.click(screen.getByRole("tab", { name: "Trading Parameters" }));
    expect(screen.getByLabelText("Current route")).toHaveTextContent("tab=trading-parameters");
    expect(screen.getByLabelText("Current route")).toHaveTextContent("accountId=account-1");
    await userEvent.click(screen.getByRole("button", { name: "Back in history" }));
    expect(await screen.findByRole("tab", { name: "Events" })).toHaveAttribute("aria-selected", "true");
    const linked = new URL(screen.getByRole("link", { name: "Open Security Master" }).getAttribute("href")!, "https://meridian.local");
    expect(linked.pathname).toBe("/accounting/security-master");
    expect(withoutRecordWorkspaceContext(linked.search)).toBe("?securityId=sec-aapl");
  });

  it("does not let a delayed prior asset response overwrite the next asset", async () => {
    let resolveFirst!: (value: SecurityMasterEntry) => void;
    vi.mocked(api.getSecurityDetail).mockImplementation((id) =>
      id === "sec-aapl"
        ? new Promise((resolve) => {
            resolveFirst = resolve;
          })
        : Promise.resolve({
            ...entry,
            securityId: "sec-msft",
            displayName: "Microsoft Corp.",
            classification: {
              ...entry.classification,
              primaryIdentifierValue: "MSFT"
            }
          })
    );
    renderWithRouter(
      <>
        <AssetDetailScreen />
        <RouteProbe />
      </>,
      { initialEntries: ["/portfolio/asset-detail?securityId=sec-aapl"] }
    );
    await userEvent.click(screen.getByRole("button", { name: "Select Microsoft route" }));
    expect(await screen.findByRole("heading", { name: "Microsoft Corp." })).toBeInTheDocument();
    await act(async () => resolveFirst(entry));
    expect(screen.queryByRole("heading", { name: "Apple Inc." })).not.toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Events" })).toHaveAttribute("aria-selected", "true");
  });

  it("reports a trading-parameters failure without claiming no parameters are configured", async () => {
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);
    vi.mocked(api.getTradingParameters)
      .mockRejectedValueOnce(new Error("offline"))
      .mockResolvedValueOnce(tradingParameters);
    await renderScreen("/portfolio/asset-detail?securityId=sec-aapl&tab=trading-parameters");
    expect(await screen.findByText(/Couldn’t load trading parameters/)).toBeInTheDocument();
    expect(screen.queryByText("No trading parameters are configured for this security.")).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Retry trading parameters" }));
    expect(await screen.findByText("25%")).toBeInTheDocument();
  });

  it("has no basic accessibility violations in the resolved overview", async () => {
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);
    const { container } = await renderScreen("/portfolio/asset-detail?securityId=sec-aapl");
    expect(await screen.findByRole("heading", { name: "Apple Inc." })).toBeInTheDocument();
    expect((await axe(container)).violations).toHaveLength(0);
  });
  it("keeps unsaved local-lot inputs across tab changes", async () => {
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);
    await renderScreen("/portfolio/asset-detail?securityId=sec-aapl&tab=holdings");
    const quantity = await screen.findByLabelText("Quantity");
    await userEvent.type(quantity, "25");
    await userEvent.click(screen.getByRole("tab", { name: "Overview" }));
    await userEvent.click(screen.getByRole("tab", { name: "Holdings & Lots" }));
    expect(screen.getByLabelText("Quantity")).toBeVisible();
    expect(screen.getByLabelText("Quantity")).toHaveValue(25);
  });

  it("reports failed security search and recovers without claiming no matches", async () => {
    vi.mocked(api.searchSecurities).mockRejectedValue(new Error("search offline"));
    await renderScreen("/portfolio/asset-detail?query=apple");
    expect(await screen.findByText("Couldn’t search securities.")).toBeInTheDocument();
    expect(screen.queryByText("No securities match this search.")).not.toBeInTheDocument();
    vi.mocked(api.searchSecurities).mockResolvedValue([entry]);
    await userEvent.click(screen.getByRole("button", { name: "Retry search" }));
    expect(await screen.findByRole("button", { name: /Apple Inc\./ })).toBeInTheDocument();
  });

  it("links a selected bond directly to analytics and governed convention migration", async () => {
    vi.mocked(api.getSecurityDetail).mockResolvedValue(bondEntry);
    await renderScreen("/portfolio/asset-detail?securityId=bond-1");

    for (const [name, query] of [
      ["Bond analytics", "?securityId=bond-1&tab=evidence&bondAction=analytics"],
      ["Convention migration", "?securityId=bond-1&tab=evidence&bondAction=migration"],
      ["Open Security Master", "?securityId=bond-1"]
    ]) {
      const url = new URL(screen.getByRole("link", { name }).getAttribute("href")!, "https://meridian.local");
      expect(url.pathname).toBe("/accounting/security-master");
      expect(withoutRecordWorkspaceContext(url.search)).toBe(query);
      expect(readRecordWorkspaceContext(url.search)?.origin).toBe("/portfolio/asset-detail?securityId=bond-1");
    }
  });

  it.each(["analytics", "migration"])("resolves an exact holding identity before opening %s", async (action) => {
    vi.mocked(api.searchSecurities).mockResolvedValue([bondEntry]);
    vi.mocked(api.getSecurityDetail).mockResolvedValue(bondEntry);
    renderWithRouter(<Routes>
      <Route path="/portfolio/asset-detail" element={<AssetDetailScreen />} />
      <Route path="/accounting/security-master" element={<WorkflowDestination />} />
    </Routes>, { initialEntries: [`/portfolio/asset-detail?symbol=US-SYNTHETIC&bondAction=${action}`] });

    expect(await screen.findByLabelText("Workflow destination")).toHaveTextContent(`/accounting/security-master?securityId=bond-1&tab=evidence&bondAction=${action}`);
    expect(api.getSecurityDetail).toHaveBeenCalledWith("bond-1", expect.objectContaining({ signal: expect.any(AbortSignal) }));
  });

  it("keeps ambiguous holding identities selectable and preserves the requested workflow", async () => {
    vi.mocked(api.searchSecurities).mockResolvedValue([bondEntry, { ...bondEntry, securityId: "bond-2", displayName: "Other synthetic bond" }]);
    vi.mocked(api.getSecurityDetail).mockResolvedValue(bondEntry);
    const user = userEvent.setup();
    renderWithRouter(<Routes>
      <Route path="/portfolio/asset-detail" element={<AssetDetailScreen />} />
      <Route path="/accounting/security-master" element={<WorkflowDestination />} />
    </Routes>, { initialEntries: ["/portfolio/asset-detail?symbol=US-SYNTHETIC&bondAction=analytics"] });

    const selected = await screen.findByRole("button", { name: /Synthetic Treasury 2030/ });
    expect(api.getSecurityDetail).not.toHaveBeenCalled();
    await user.click(selected);
    expect(await screen.findByLabelText("Workflow destination")).toHaveTextContent("securityId=bond-1&tab=evidence&bondAction=analytics");
  });

  it("explains when a resolved holding cannot use a bond workflow", async () => {
    vi.mocked(api.getSecurityDetail).mockResolvedValue(entry);
    await renderScreen("/portfolio/asset-detail?securityId=sec-aapl&bondAction=analytics");

    expect(screen.getByText("Bond workflow unavailable for this instrument")).toBeInTheDocument();
    expect(screen.getByText(/The selected Security Master record is Equity/)).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Bond analytics" })).not.toBeInTheDocument();
  });

  it("refuses to forward to a different security returned by the detail read", async () => {
    vi.mocked(api.getSecurityDetail).mockResolvedValue(bondEntry);
    renderWithRouter(<Routes>
      <Route path="/portfolio/asset-detail" element={<AssetDetailScreen />} />
      <Route path="/accounting/security-master" element={<WorkflowDestination />} />
    </Routes>, { initialEntries: ["/portfolio/asset-detail?securityId=other-bond&bondAction=analytics"] });

    expect(await screen.findByText("Couldn’t load asset detail")).toBeInTheDocument();
    expect(screen.queryByLabelText("Workflow destination")).not.toBeInTheDocument();
  });
});
