import { describe, expect, it } from "vitest";
import { ledgerPeriodAccountRunningBalanceEndpoint } from "@/lib/ledger-reports-api";

describe("ledgerPeriodAccountRunningBalanceEndpoint", () => {
  const periodId = "11111111-1111-1111-1111-111111111111";

  it("builds the running-balance route with its account query", () => {
    expect(ledgerPeriodAccountRunningBalanceEndpoint(periodId, { account: "Management fees", symbol: "AAPL" })).toBe(
      `/api/ledger/periods/${periodId}/accounts/running-balance?account=Management+fees&symbol=AAPL`
    );
  });

  it("omits optional filters that are not set", () => {
    expect(
      ledgerPeriodAccountRunningBalanceEndpoint(periodId, { account: "Cash", symbol: null, financialAccountId: "1000" })
    ).toBe(`/api/ledger/periods/${periodId}/accounts/running-balance?account=Cash&financialAccountId=1000`);
  });
});
