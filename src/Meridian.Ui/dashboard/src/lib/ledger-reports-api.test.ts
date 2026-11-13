import { describe, expect, it } from "vitest";
import { ledgerPeriodAccountRunningBalanceEndpoint } from "@/lib/ledger-reports-api";

describe("ledgerPeriodAccountRunningBalanceEndpoint", () => {
  const periodId = "11111111-1111-1111-1111-111111111111";

  it("builds the running-balance route with its account query", () => {
    expect(ledgerPeriodAccountRunningBalanceEndpoint(periodId, { account: "Management fees", symbol: "AAPL" })).toBe(
      `/api/ledger/periods/${periodId}/accounts/running-balance?account=Management+fees&symbol=AAPL`
    );
  });

  it("omits filters that are not given and sends an explicit empty filter for a null scope", () => {
    expect(ledgerPeriodAccountRunningBalanceEndpoint(periodId, { account: "Cash", financialAccountId: "1000" })).toBe(
      `/api/ledger/periods/${periodId}/accounts/running-balance?account=Cash&financialAccountId=1000`
    );
    expect(
      ledgerPeriodAccountRunningBalanceEndpoint(periodId, { account: "Cash", symbol: null, financialAccountId: null })
    ).toBe(`/api/ledger/periods/${periodId}/accounts/running-balance?account=Cash&symbol=&financialAccountId=`);
  });

  it("sends the selected trial-balance row's dimension set, including an explicit null", () => {
    const scoped = new URL(
      ledgerPeriodAccountRunningBalanceEndpoint(periodId, { account: "Cash", dimensions: { fundId: "alpha", sleeveId: "s1" } }),
      "http://localhost"
    );
    expect(JSON.parse(scoped.searchParams.get("dimensions") ?? "")).toEqual({ fundId: "alpha", sleeveId: "s1" });

    const unscoped = new URL(
      ledgerPeriodAccountRunningBalanceEndpoint(periodId, { account: "Cash", dimensions: null }),
      "http://localhost"
    );
    expect(unscoped.searchParams.get("dimensions")).toBe("null");
    expect(
      new URL(ledgerPeriodAccountRunningBalanceEndpoint(periodId, { account: "Cash" }), "http://localhost").searchParams.has(
        "dimensions"
      )
    ).toBe(false);
  });

  it("scopes a running balance to the selected accounting basis without changing unscoped filters", () => {
    const url = new URL(ledgerPeriodAccountRunningBalanceEndpoint(periodId, {
      account: "Cash", accountingBasis: "Gaap", symbol: null, financialAccountId: null, dimensions: null
    }), "http://localhost");
    expect(url.searchParams.get("accountingBasis")).toBe("Gaap");
    expect(url.searchParams.get("symbol")).toBe("");
    expect(url.searchParams.get("financialAccountId")).toBe("");
    expect(url.searchParams.get("dimensions")).toBe("null");
    expect(new URL(ledgerPeriodAccountRunningBalanceEndpoint(periodId, { account: "Cash" }), "http://localhost").searchParams.has("accountingBasis")).toBe(false);
  });
});
