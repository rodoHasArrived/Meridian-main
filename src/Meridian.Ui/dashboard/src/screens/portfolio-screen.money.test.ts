import { describe, expect, it } from "vitest";
import { formatBrokerageHouseholdMoney, formatBrokerageMoney } from "./portfolio-screen.money";
import { buildPortfolioScreenViewModel } from "./portfolio-screen.view-model";
import type { BrokerageHouseholdPortfolio } from "@/types";

function portfolio(currencies = ["EUR"]): BrokerageHouseholdPortfolio {
  return {
    providerId: "test", asOf: "2026-11-11T12:00:00Z", currency: "EUR",
    totalEquity: 12000, totalCash: 1000, totalBuyingPower: 2000, warnings: [],
    accounts: currencies.map((currency, index) => ({
      fundAccountId: `account-${index}`, providerId: "test", externalAccountId: `external-${index}`,
      displayName: `Account ${index}`, accountKind: "TaxableBrokerage", health: "Healthy",
      cash: 1000, equity: 12000, buyingPower: 2000, currency, syncedAt: "2026-11-11T12:00:00Z",
      positionCount: 1, cashTransactionCount: 0, warnings: []
    })),
    positions: [{
      fundAccountId: "account-0", providerId: "test", externalAccountId: "external-0", accountKind: "TaxableBrokerage",
      symbol: "TEST", quantity: 2, averageEntryPrice: 1.234567, marketPrice: 2.345678, marketValue: 4.69,
      unrealizedPnl: -2.22, assetClass: "Equity", security: null, description: null,
      positionId: "position-1", currency: "EUR"
    }]
  };
}

describe("Portfolio native monetary presentation", () => {
  it("uses retained denominations, signed values and fractional price precision", () => {
    expect(formatBrokerageMoney(1234, "USD")).toBe("$1,234");
    expect(formatBrokerageMoney(-1234, "eur", { signed: true })).toBe("-EUR 1,234");
    expect(formatBrokerageMoney(1234, "JPY")).toBe("JPY 1,234");
    expect(formatBrokerageMoney(1.234567, "EUR", { precise: true })).toBe("EUR 1.234567");
    expect(formatBrokerageMoney(0, "USD", { signed: true })).toBe("+$0");
    expect(formatBrokerageMoney(-0.02, "USD", { signed: true })).toBe("-$0.02");
  });

  it.each([null, undefined, "", " ", "XXX", "unknown"])("does not invent currency for %s", currency => {
    const result = formatBrokerageMoney(12.5, currency, { precise: true });
    expect(result).toBe("12.50 (currency unavailable)");
    expect(result).not.toContain("$");
  });

  it("leaves non-finite observations unavailable", () => {
    expect(formatBrokerageMoney(Number.NaN, "USD")).toBe("—");
    expect(formatBrokerageMoney(Number.POSITIVE_INFINITY, "EUR")).toBe("—");
  });

  it("uses service household totals only when native account currencies are comparable", () => {
    expect(formatBrokerageHouseholdMoney(portfolio(), "totalEquity")).toBe("EUR 12,000");
    expect(formatBrokerageHouseholdMoney(portfolio(["EUR", "USD"]), "totalEquity")).toBe("Not comparable — inspect account currencies");
    expect(formatBrokerageHouseholdMoney(portfolio([""]), "totalCash")).toBe("Not comparable — inspect account currencies");
    expect(formatBrokerageHouseholdMoney(null, "totalEquity")).toBe("—");
  });

  it("keeps native currency throughout account rows, positions, inspectors, headers and trust summaries", () => {
    const vm = buildPortfolioScreenViewModel({ trading: null, strategy: null, accounting: null, brokeragePortfolio: portfolio() });
    expect(vm.brokerageAccountRows[0].equity).toBe("EUR 12,000");
    expect(vm.brokeragePositionRows[0]).toMatchObject({ currency: "EUR", averagePrice: "EUR 1.234567", unrealizedPnl: "-EUR 2.22" });
    expect(vm.selectedBrokeragePosition?.fields.find(field => field.label === "Recorded mark price")?.value).toBe("EUR 2.345678");
    expect(vm.headerChips[0].value).toBe("EUR 12,000");
    expect(vm.brokerageTrustSnapshot.summary).toContain("EUR 12,000 equity");
    expect(vm.selectedBrokerageAccount.fields.find(field => field.label === "Equity")?.value).toBe("EUR 12,000");
  });

  it("keeps mixed-currency household totals unavailable and requests review", () => {
    const vm = buildPortfolioScreenViewModel({ trading: null, strategy: null, accounting: null, brokeragePortfolio: portfolio(["EUR", "USD"]) });
    expect(vm.headerChips[0].value).toContain("Not comparable");
    expect(vm.brokerageTrustSnapshot.statusTone).toBe("warning");
    expect(vm.brokerageWarningRows.some(row => row.id === "household-currency")).toBe(true);
    expect(vm.selectedBrokerageAccount.fields.find(field => field.label === "Equity")?.value).toContain("Not comparable");
    expect(vm.brokerageAccountRows.map(row => row.equity)).toEqual(["EUR 12,000", "$12,000"]);
  });
});
