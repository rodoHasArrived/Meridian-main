import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Route, Routes, useLocation } from "react-router-dom";
import { axe } from "jest-axe";
import { InstrumentDossierScreen } from "./instrument-dossier-screen";
import { renderWithRouter, waitForAsyncEffects } from "@/test/render";
import { getSecurityDetail } from "@/lib/api";
import { getLedgerBooks, getLedgerPeriods, getLedgerPeriodJournalEntries } from "@/lib/ledger-reports-api";
import { FundAccountScopeSelectionProvider } from "@/lib/operating-scope/scope-picker-context";
import type { AccountingWorkspaceResponse, LedgerBook, LedgerPeriod, LedgerPostedJournalEntry, SecurityMasterEntry, TradingWorkspaceResponse } from "@/types";

vi.mock("@/lib/api", async () => ({ ...await vi.importActual("@/lib/api"), getSecurityDetail: vi.fn() }));
vi.mock("@/lib/ledger-reports-api", () => ({ getLedgerBooks: vi.fn(), getLedgerPeriods: vi.fn(), getLedgerPeriodJournalEntries: vi.fn() }));
vi.mock("@/screens/asset-detail-screen", () => ({ AssetDetailPanel: ({ securityId }: { securityId: string }) => <div>Retained asset workflows for {securityId}</div> }));
const id = "a0000000-0000-0000-0000-000000000001";
const otherId = "a0000000-0000-0000-0000-000000000002";
const security = { securityId: id, displayName: "Apple", status: "Active", classification: { assetClass: "Equity", primaryIdentifierValue: "AAPL" }, economicDefinition: { currency: "USD" } } as SecurityMasterEntry;
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
    expect(table.closest("[data-position-source]")).toHaveTextContent("Source: Trading account positions");
    expect(screen.getAllByText(/^Data condition unavailable:/)).toHaveLength(1);
    expect(screen.getByRole("group", { name: "10 units · Data condition unavailable" })).toBeInTheDocument();
    expect(screen.getByText("retained-mark-policy-7")).not.toBeVisible();
    await userEvent.click(screen.getByText("Mark policy", { exact: true }));
    expect(screen.getByText("retained-mark-policy-7")).toBeVisible();
    expect(screen.getByText(id)).not.toBeVisible();
    await userEvent.click(screen.getByText("Technical identity", { exact: true }));
    expect(screen.getByText(id)).toBeVisible();
  });
});
