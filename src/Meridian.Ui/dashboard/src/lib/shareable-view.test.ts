import { afterEach, describe, expect, it } from "vitest";
import { readOperatingScopeFromSearch } from "@/app-shell.operating-scope";
import { buildShareableLink } from "@/components/meridian/copy-link-button";
import { normalizePortfolioPositionsTableState, type PortfolioPositionsTableState } from "@/lib/portfolio-positions-table";
import { encodeViewStateEnvelope, readViewStateFromSearch, VIEW_STATE_ENVELOPE_VERSION, VIEW_STATE_QUERY_KEY } from "@/lib/view-state-envelope";
import { WORKSTATION_SCREEN_VIEW_STATE_SCREENS } from "@/lib/workstation-screen-view-states";
import { parseLedgerSort } from "@/screens/finance-standard-pages-screen";
import { readReconciliationViewState } from "@/screens/reconciliation-view-state";

// Phase 3 acceptance (C1 + C4): a copied link restores scope, filters, sort and columns in a fresh
// browser — nothing the receiver sees may depend on the sender's local storage.
afterEach(() => window.localStorage.clear());

function freshBrowser() {
  window.localStorage.clear();
  window.sessionStorage.clear();
}

describe("a copied link restores the view in a fresh browser", () => {
  it("carries the positions table's filters, sort and hidden columns, and the scope", () => {
    const positions: PortfolioPositionsTableState = {
      search: "AA",
      side: "Long",
      readiness: "review-required",
      sort: { columnId: "symbol", direction: "desc" },
      hiddenColumns: ["average", "exposure"]
    };
    const token = encodeViewStateEnvelope({
      v: VIEW_STATE_ENVELOPE_VERSION,
      screen: WORKSTATION_SCREEN_VIEW_STATE_SCREENS.portfolioWorkstation,
      state: { positionsTable: positions }
    });
    const href = `https://host/workstation/portfolio?${VIEW_STATE_QUERY_KEY}=${token}`;
    const link = buildShareableLink(href, { symbol: "AAPL", fundAccountId: "fund-a", asOf: "2026-06-30" });

    freshBrowser();
    const search = new URL(link).search;
    const restored = normalizePortfolioPositionsTableState(
      readViewStateFromSearch(search, WORKSTATION_SCREEN_VIEW_STATE_SCREENS.portfolioWorkstation)?.state.positionsTable
    );
    expect(restored).toEqual(positions);
    expect(readOperatingScopeFromSearch(search)).toMatchObject({ symbol: "AAPL", fundAccountId: "fund-a", asOf: "2026-06-30" });
  });

  it("carries the break queue's search, status, sort and hidden columns", () => {
    const link = buildShareableLink(
      "https://host/workstation/accounting/reconciliation?reconciliationSearch=cash&reconciliationStatus=Open&reconciliationSort=oldest&reconciliationColumns=owner,source",
      { fundAccountId: "fund-a" }
    );
    freshBrowser();
    const view = readReconciliationViewState(new URL(link).search);
    expect(view).toMatchObject({ queueSearch: "cash", queueStatus: "Open", queueSort: "oldest", hiddenColumns: ["owner", "source"] });
  });

  it("carries the ledger explorer's search and sort", () => {
    const link = buildShareableLink("https://host/workstation/accounting/ledger?ledgerSearch=sweep&ledgerSort=debit%3Adesc", {});
    freshBrowser();
    const params = new URL(link).searchParams;
    expect(params.get("ledgerSearch")).toBe("sweep");
    expect(parseLedgerSort(params.get("ledgerSort"))).toEqual({ columnId: "debit", direction: "desc" });
  });

  it("ignores link values that name no real column or sort", () => {
    expect(readReconciliationViewState("?reconciliationColumns=break,owner,nope").hiddenColumns).toEqual(["owner"]);
    expect(parseLedgerSort("status:asc")).toBeNull();
    expect(parseLedgerSort("debit:sideways")).toBeNull();
  });
});
