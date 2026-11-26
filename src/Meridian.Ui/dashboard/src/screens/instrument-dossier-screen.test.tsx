import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes, useLocation } from "react-router-dom";
import { axe } from "jest-axe";
import { InstrumentDossierScreen } from "./instrument-dossier-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import { getSecurityDetail } from "@/lib/api";
import { getLedgerBooks, getLedgerPeriods, getLedgerPeriodJournalEntries } from "@/lib/ledger-reports-api";
import { FundAccountScopeSelectionProvider } from "@/lib/operating-scope/scope-picker-context";
import type { AccountingWorkspaceResponse, BrokerageHouseholdPortfolio, BrokerageHouseholdPosition, LedgerBook, LedgerPeriod, LedgerPostedJournalEntry, PortfolioWorkspaceResponse, SecurityMasterEntry, TradingWorkspaceResponse } from "@/types";
import type { AmountPassportReference } from "@/types/amount-passport";

vi.mock("@/lib/api", async () => ({ ...await vi.importActual("@/lib/api"), getSecurityDetail: vi.fn() }));
vi.mock("@/lib/ledger-reports-api", () => ({ getLedgerBooks: vi.fn(), getLedgerPeriods: vi.fn(), getLedgerPeriodJournalEntries: vi.fn() }));
vi.mock("@/screens/asset-detail-screen", () => ({ AssetDetailPanel: ({ securityId }: { securityId: string }) => <div>Retained asset workflows for {securityId}</div> }));
const id = "a0000000-0000-0000-0000-000000000001";
const otherId = "a0000000-0000-0000-0000-000000000002";
const security = { securityId: id, displayName: "Apple", status: "Active", classification: { assetClass: "Equity", primaryIdentifierValue: "AAPL" }, economicDefinition: { currency: "USD" } } as SecurityMasterEntry;
const brokeragePosition: BrokerageHouseholdPosition = {
  providerId: "Alpaca", fundAccountId: "account", externalAccountId: "Alpaca Roth IRA", accountKind: "RothIra", symbol: "AAPL",
  quantity: 38, averageEntryPrice: 180, marketPrice: 188.4, marketValue: 7159.2, unrealizedPnl: 319.2, currency: "USD",
  assetClass: "Equity", description: null, positionId: "position-1",
  security: { securityId: id, displayName: "Apple", assetClass: "Equity", currency: "USD", status: "Active", primaryIdentifier: "AAPL", subType: null }
};
function brokerageHousehold(position: BrokerageHouseholdPosition): BrokerageHouseholdPortfolio {
  return { providerId: "Alpaca", asOf: "2026-05-07T12:00:00Z", totalCash: 0, totalEquity: 7159.2, totalBuyingPower: 0,
    currency: "USD", accounts: [], positions: [position], warnings: [] };
}
function Location() { return <output aria-label="Current location">{useLocation().search}</output>; }
function renderDossier(query = "", props = {}) {
  return renderWithRouter(<><Routes><Route path="/records/instrument/:id" element={<InstrumentDossierScreen {...props} />} /></Routes><Location /></>, { initialEntries: [`/records/instrument/${id}${query}`] });
}

describe("InstrumentDossierScreen", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getSecurityDetail).mockResolvedValue(security);
    vi.mocked(getLedgerBooks).mockResolvedValue([{ ledgerBookId: "book", displayName: "Operating book", baseCurrency: "USD", fundProfileId: "fund" }] as LedgerBook[]);
    vi.mocked(getLedgerPeriods).mockResolvedValue([{ periodId: "period", ledgerBookId: "book", label: "November" }] as LedgerPeriod[]);
    vi.mocked(getLedgerPeriodJournalEntries).mockResolvedValue([]);
  });

  it("persists tab selection and scope and embeds the existing reference workflows", async () => {
    renderDossier("?fundAccountId=account&tab=positions");
    expect(await screen.findByRole("heading", { name: "Apple" })).toBeInTheDocument();
    await userEvent.click(screen.getByRole("tab", { name: "Reference" }));
    expect(screen.getByText(`Retained asset workflows for ${id}`)).toBeInTheDocument();
    expect(screen.getByLabelText("Current location")).toHaveTextContent("fundAccountId=account&tab=reference");
  });

  it("filters ledger reads and rendering by instrument and never displays other instruments", async () => {
    vi.mocked(getLedgerPeriodJournalEntries).mockResolvedValue([{ journalEntryId: "journal", ledgerBookId: "book", periodId: "period", lines: [
      { entryId: "kept", journalEntryId: "journal", timestamp: "2026-11-01", financialAccountId: "account", accountName: "Instrument cash", description: "Identity linked", debit: 5, credit: 0, dimensions: { instrumentId: id } },
      { entryId: "other", journalEntryId: "journal", timestamp: "2026-11-01", financialAccountId: "account", accountName: "Other instrument cash", description: "Different identity", debit: 50, credit: 0, dimensions: { instrumentId: otherId } },
      { entryId: "other-account", journalEntryId: "journal", timestamp: "2026-11-01", financialAccountId: "another-account", accountName: "Other account cash", description: "Different account", debit: 50, credit: 0, dimensions: { instrumentId: id } }
    ] }] as LedgerPostedJournalEntry[]);
    renderDossier("?tab=ledger&ledgerBookId=book&periodId=period&financialAccountId=account");
    await waitForAsyncEffects();
    expect(await screen.findByText("Identity linked")).toBeInTheDocument();
    expect(screen.queryByText("Different identity")).not.toBeInTheDocument();
    expect(screen.queryByText("Different account")).not.toBeInTheDocument();
    expect(getLedgerPeriodJournalEntries).toHaveBeenCalledWith("period", { instrumentId: id, accountId: "account" }, expect.objectContaining({ allowDevelopmentFallback: false }));
  });

  it("does not fall back to an unfiltered ledger on failure", async () => {
    vi.mocked(getLedgerPeriodJournalEntries).mockRejectedValue(new Error("Unavailable"));
    renderDossier("?tab=ledger&ledgerBookId=book&periodId=period");
    await waitForAsyncEffects();
    expect(await screen.findByRole("alert")).toHaveTextContent("No unfiltered fallback");
    expect(getLedgerPeriodJournalEntries).toHaveBeenCalledTimes(1);
  });

  it("does not treat a brokerage account ID as a ledger financial-account mapping", async () => {
    renderDossier("?tab=ledger&ledgerBookId=book&periodId=period&fundAccountId=brokerage-account");
    expect(await screen.findByText(/selected brokerage account is distinct/)).toBeInTheDocument();
    await waitFor(() => expect(getLedgerPeriods).toHaveBeenCalled());
    expect(getLedgerPeriodJournalEntries).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole("button", { name: "Clear brokerage scope for book-wide review" }));
    await waitFor(() => expect(getLedgerPeriodJournalEntries).toHaveBeenCalledWith("period", { instrumentId: id }, expect.anything()));
  });

  it("keeps remembered shell brokerage scope from silently widening the ledger query", async () => {
    renderWithRouter(<FundAccountScopeSelectionProvider value={{ options: [], select: vi.fn(), scope: { fundAccountId: "remembered-brokerage-account" } }}>
      <Routes><Route path="/records/instrument/:id" element={<InstrumentDossierScreen />} /></Routes>
    </FundAccountScopeSelectionProvider>, { initialEntries: [`/records/instrument/${id}?tab=ledger&ledgerBookId=book&periodId=period`] });
    expect(await screen.findByText(/selected brokerage account is distinct/)).toBeInTheDocument();
    await waitFor(() => expect(getLedgerPeriods).toHaveBeenCalled());
    expect(getLedgerPeriodJournalEntries).not.toHaveBeenCalled();
  });

  it("opens cases with the existing reconciliation selection and account context", async () => {
    renderDossier("?tab=cases&fundAccountId=account", { accounting: { breakQueue: [{ instrumentId: id, breakId: "case-1", runId: "run-1", fundAccountId: "account", reason: "Review retained difference", status: "Open" }] } as AccountingWorkspaceResponse });
    const link = await screen.findByRole("link", { name: "Review retained difference" });
    expect(link).toHaveAttribute("href", "/accounting/reconciliation?fundAccountId=account&reconciliationBreak=case-1&reconciliationRun=run-1");
  });

  it("discloses missing position identities instead of matching ticker text", async () => {
    const trading = { positions: [{ symbol: "AAPL", side: "Long", quantity: "10", markPrice: "100", exposure: "1000" }] } as TradingWorkspaceResponse;
    const { container } = renderDossier("", { trading });
    expect(await screen.findByText(/1 loaded position\(s\) have no retained instrument identity/)).toBeInTheDocument();
    expect(screen.getByText(/No identity-linked positions/)).toBeInTheDocument();
    expect((await axe(container)).violations).toHaveLength(0);
  });

  it("preserves fractional quantities rather than applying currency rounding to units", async () => {
    const trading = { positions: [{ symbol: "AAPL", side: "Long", quantity: "0.000123", markPrice: "100", exposure: "0.0123", currency: "USD", markFreshness: { securityId: id, financialAccountId: "account", status: "Current" } }] } as TradingWorkspaceResponse;
    renderDossier("", { trading });
    expect(await screen.findByText("0.000123 units")).toBeInTheDocument();
  });

  it("names the position source and discloses retained identities without implying value confirmation", async () => {
    const trading = { positions: [{ symbol: "AAPL", side: "Long", quantity: "10", markPrice: "100", exposure: "1000", currency: "USD",
      markFreshness: { securityId: id, financialAccountId: "account", status: "Current", policyVersion: "retained-mark-policy-7" } }] } as TradingWorkspaceResponse;
    renderDossier("", { trading });
    expect(await screen.findByText("10 units")).toBeInTheDocument();
    const table = screen.getByRole("table", { name: "Instrument positions by account" });
    expect(table.closest("[data-position-source]")).toHaveTextContent("Source: Trading workspace positions");
    expect(screen.getAllByText(/^Data condition unavailable for values/)).toHaveLength(1);
    expect(screen.getByRole("group", { name: "10 units · Data condition unavailable" })).toBeInTheDocument();
    expect(screen.getByText("Policy version: retained-mark-policy-7")).not.toBeVisible();
    await userEvent.click(screen.getByText("Mark evidence", { exact: true }));
    expect(screen.getByText("Policy version: retained-mark-policy-7")).toBeVisible();
    expect(screen.getByText(id)).not.toBeVisible();
    await userEvent.click(screen.getByText("Technical identity", { exact: true }));
    expect(screen.getByText(id)).toBeVisible();
  });

  it("names the brokerage source, consolidates unknown conditions and keeps raw identity and policy inspectable", async () => {
    const brokerage = brokerageHousehold({ ...brokeragePosition,
      markFreshness: { symbol: "AAPL", securityId: id, financialAccountId: "account", status: "Current", policyVersion: "retained-mark-policy-v1", observedOn: "2026-05-07", valuationDate: "2026-05-07" }
    });
    const { container } = renderDossier("?fundAccountId=account&tab=positions", { brokeragePortfolio: brokerage });
    const table = await screen.findByRole("table", { name: "Instrument positions by account" });
    expect(table.closest("[data-position-source]")).toHaveAttribute("data-position-source", "brokerage-household");
    expect(screen.getByText(/Source: Alpaca household/)).toHaveTextContent("Snapshot observed 2026-05-07T12:00:00Z");
    for (const value of ["38 units", "188.40 USD", "7,159.20 USD"]) expect(within(table).getByText(value)).toBeVisible();
    const note = screen.getByText("Data condition unavailable for values without supported amount-level source evidence. Original source amounts are shown.");
    expect(note).toBeVisible();
    expect(within(table).queryByText("Data condition unavailable", { exact: true })).not.toBeInTheDocument();
    for (const value of ["38 units", "188.40 USD", "7,159.20 USD"]) {
      expect(within(table).getByText(value).closest("[aria-describedby]"))
        .toHaveAttribute("aria-describedby", expect.stringContaining(note.id));
    }
    expect(within(table).queryByText("Confirmed")).not.toBeInTheDocument();
    expect(within(table).queryByRole("button", { name: /Amount passport/ })).not.toBeInTheDocument();
    expect(screen.getByText(id, { exact: true })).not.toBeVisible();
    await userEvent.click(screen.getByText("Technical identity", { exact: true }));
    expect(screen.getByText(id, { exact: true })).toBeVisible();
    const policy = screen.getByText("Policy version: retained-mark-policy-v1");
    expect(policy).not.toBeVisible();
    await userEvent.click(within(table).getByText("Mark evidence", { exact: true }));
    expect(policy).toBeVisible();
    expect(within(table).getByText("Observed 2026-05-07 · valuation 2026-05-07")).toBeVisible();
    expect((await axe(container)).violations).toHaveLength(0);
  });

  it("retains supplied stale and estimated amount conditions and scoped passport controls", async () => {
    const reference = (exactValue: string, currency: string | null, changes: Partial<AmountPassportReference> = {}): AmountPassportReference => ({
      identity: { subjectKind: "position-valuation", subjectId: `retained:${exactValue}`, asOf: "2026-05-07T12:00:00Z", revision: "retained-revision",
        scope: { tenantId: "tenant", companyId: "company", accountId: "account", basis: "Brokerage observation" } },
      currency, exactValue, unit: currency ? "currency" : "units", origin: "sourced", isZero: false, freshness: "current", ...changes
    });
    const brokerage = brokerageHousehold({ ...brokeragePosition,
      amountPassports: { quantity: reference("38", null), mark: reference("188.4", "USD", { freshness: "stale" }),
        "market-value": reference("7159.2", "USD", { origin: "estimated" }) }
    });
    renderDossier("?tab=positions", { brokeragePortfolio: brokerage });
    const table = await screen.findByRole("table", { name: "Instrument positions by account" });
    expect(within(table).getByText("Stale", { exact: true })).toBeVisible();
    expect(within(table).getByText("Estimated", { exact: true })).toBeVisible();
    expect(within(table).getByRole("button", { name: /188\.40 USD · Stale/ })).toBeInTheDocument();
    expect(within(table).getByRole("button", { name: /7,159\.20 USD · Estimated/ })).toBeInTheDocument();
    expect(screen.queryByText(/Data condition unavailable for values/)).not.toBeInTheDocument();
  });

  it("labels the selected workspace source without inventing a snapshot observation time", async () => {
    const position = { symbol: "AAPL", side: "Long", quantity: "0.125", markPrice: "100", exposure: "12.5", currency: "USD",
      markFreshness: { securityId: id, financialAccountId: "account", status: "Current" } };
    renderDossier("?tab=positions&fundAccountId=account", { portfolio: { positions: [position] } as PortfolioWorkspaceResponse });
    const table = await screen.findByRole("table", { name: "Instrument positions by account" });
    expect(table.closest("[data-position-source]")).toHaveAttribute("data-position-source", "live-holdings");
    expect(screen.getByText(/Source: Portfolio holdings · selected account/)).toHaveTextContent("Snapshot observation time not supplied");
    expect(within(table).getByText("0.125 units")).toBeInTheDocument();
  });
});
