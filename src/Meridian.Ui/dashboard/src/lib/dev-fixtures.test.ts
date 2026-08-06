import { describe, expect, it } from "vitest";
import { resolveDevFixture } from "@/lib/dev-fixtures";
import {
  SYMBOL_API_ENDPOINTS,
  brokerageConnectionStatusEndpoint,
  historicalBarsEndpoint,
  marketDataQuotesSnapshotEndpoint,
  workstationFinancialRecordExplorerEndpoint
} from "@/lib/workstation-endpoints";
import type {
  FinancialRecordExplorerDto,
  HistoricalBarsResponse,
  QuotesSnapshotResponse,
  BrokerageConnectionStatus,
  SymbolStatistics
} from "@/types";

describe("dev fixtures", () => {
  it("serves the portfolio financial record explorer for no-host previews", () => {
    const fixture = resolveDevFixture<FinancialRecordExplorerDto>(
      workstationFinancialRecordExplorerEndpoint("portfolio")
    );

    expect(fixture).toBeDefined();
    expect(fixture?.explorerId).toBe("portfolio");
    expect(fixture?.sourceState).toMatch(/demo data/i);
    expect(fixture?.rows.length).toBeGreaterThan(0);
    expect(fixture?.selectedRecord?.recordId).toBe(fixture?.rows[0]?.recordId);
    expect(fixture?.proofActions.some((action) => action.isEnabled)).toBe(true);
  });

  it("serves market data through the split fixture resolver", () => {
    const stats = resolveDevFixture<SymbolStatistics>(SYMBOL_API_ENDPOINTS.statistics);
    const snapshot = resolveDevFixture<QuotesSnapshotResponse>(marketDataQuotesSnapshotEndpoint(["AAPL", "MSFT"]));
    const bars = resolveDevFixture<HistoricalBarsResponse>(
      historicalBarsEndpoint("AAPL", { intervalMinutes: 15, from: "2026-05-08T13:30:00.000Z" })
    );

    expect(stats?.monitoredSymbols).toBe(4);
    expect(snapshot?.quotes.map((quote) => quote.symbol)).toEqual(["AAPL", "MSFT"]);
    expect(bars?.symbol).toBe("AAPL");
    expect(bars?.intervalMinutes).toBe(15);
    expect(bars?.bars.length).toBeGreaterThan(0);
  });

  it("keeps Robinhood and Alpaca brokerage fixtures provider-specific", () => {
    const alpaca = resolveDevFixture<BrokerageConnectionStatus>(
      brokerageConnectionStatusEndpoint("alpaca")
    );
    const robinhood = resolveDevFixture<BrokerageConnectionStatus>(
      brokerageConnectionStatusEndpoint("robinhood")
    );

    expect(alpaca).toMatchObject({
      providerId: "alpaca",
      displayName: "Alpaca paper",
      environment: "paper"
    });
    expect(robinhood).toMatchObject({
      providerId: "robinhood",
      displayName: "Robinhood read-only",
      environment: "read-only",
      externalAccountId: "RH-DEMO"
    });
    expect(robinhood).not.toEqual(alpaca);
  });
});
