// @vitest-environment node
import { describe, expect, it } from "vitest";
import { instrumentCases, instrumentPositions, isLedgerInstrumentId, readInstrumentDossierTab } from "./instrument-dossier.view-model";
import { ledgerPeriodJournalEntriesEndpoint } from "@/lib/workstation-endpoints";
import type { BrokerageHouseholdPortfolio, ReconciliationBreakQueueItem, TradingPosition } from "@/types";

const instrument = "a0000000-0000-0000-0000-000000000001";
const otherInstrument = "a0000000-0000-0000-0000-000000000002";
const position = (securityId?: string): TradingPosition => ({ symbol: "SAME", side: "Long", quantity: "10", averagePrice: "1", markPrice: "2", dayPnl: "0", unrealizedPnl: "10", exposure: "20", markFreshness: securityId ? { symbol: "SAME", securityId, financialAccountId: "account-1", valuationDate: "2026-11-01", policyVersion: "v1", status: "Current" } : null });

describe("instrument dossier identity and URL contracts", () => {
  it("defaults unsupported tab values without changing valid shareable tabs", () => {
    expect(readInstrumentDossierTab(null)).toBe("positions");
    expect(readInstrumentDossierTab("unknown")).toBe("positions");
    expect(readInstrumentDossierTab("ledger")).toBe("ledger");
  });

  it("requires a canonical nonempty GUID before querying instrument-filtered ledger reads", () => {
    expect(isLedgerInstrumentId(instrument)).toBe(true);
    expect(isLedgerInstrumentId("sec-aapl")).toBe(false);
    expect(isLedgerInstrumentId("00000000-0000-0000-0000-000000000000")).toBe(false);
    expect(ledgerPeriodJournalEntriesEndpoint("period-1", { instrumentId: instrument })).toContain(`?instrumentId=${instrument}`);
    expect(ledgerPeriodJournalEntriesEndpoint("period-1", { instrumentId: instrument, accountId: "account" })).toContain("dimensionAccountId=account");
    expect(ledgerPeriodJournalEntriesEndpoint("period-1")).not.toContain("?");
  });

  it("does not assign same-symbol or identity-free positions to the instrument", () => {
    const result = instrumentPositions(instrument, null, null, [position(instrument), position(otherInstrument), position()]);
    expect(result.rows).toHaveLength(1);
    expect(result.unlinkedCount).toBe(1);
    expect(instrumentPositions(instrument, "other-account", null, [position(instrument)]).rows).toHaveLength(0);
  });

  it("uses retained brokerage identity and account scope, preserving per-account amounts", () => {
    const brokerage = { positions: [
      { security: { securityId: instrument }, fundAccountId: "account-1", externalAccountId: "custody-1", providerId: "provider", positionId: "position-1", symbol: "SAME", quantity: 5, marketPrice: 2, marketValue: 10, currency: "USD", markFreshness: { symbol: "SAME", valuationDate: "2026-11-21", observedOn: "2026-11-21", policyVersion: "policy-2", status: "Current", blockReason: null } },
      { security: { securityId: otherInstrument }, fundAccountId: "account-1", symbol: "SAME" },
      { security: { securityId: instrument }, fundAccountId: "account-2", symbol: "SAME" }
    ] } as BrokerageHouseholdPortfolio;
    const result = instrumentPositions(instrument, "account-1", brokerage, []);
    expect(result.rows).toHaveLength(1);
    expect(result.rows[0]).toMatchObject({ account: "custody-1", quantity: 5, value: 10, currency: "USD", markStatus: "Current", markReason: "Observed 2026-11-21 · valuation 2026-11-21", markPolicyVersion: "policy-2" });
  });

  it("matches cases by retained instrument, account and period rather than reason text", () => {
    const rows = [
      { breakId: "kept", instrumentId: instrument, fundAccountId: "account-1", accountingPeriodId: "period-1" },
      { breakId: "other", instrumentId: otherInstrument, fundAccountId: "account-1", accountingPeriodId: "period-1" },
      { breakId: "wrong-period", instrumentId: instrument, fundAccountId: "account-1", accountingPeriodId: "period-2" },
      { breakId: "symbol-only", reason: "SAME variance" }
    ] as ReconciliationBreakQueueItem[];
    expect(instrumentCases(instrument, rows, "account-1", "period-1").map((row) => row.breakId)).toEqual(["kept"]);
    expect(instrumentCases(instrument, rows, "account-1", "period-1", "another-book")).toEqual([]);
  });
});
