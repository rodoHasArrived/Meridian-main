import { beforeEach, describe, expect, it, vi } from "vitest";
import { apiGetJson } from "@/lib/api";
import { getProviderVerificationHistory } from "./provider-verification-history.api";
import { getAccountBalanceHistory } from "./account-balance-history.api";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));
beforeEach(() => vi.resetAllMocks());

describe("retained chart evidence boundaries", () => {
  it("requires real provider history for the exact selected connection", async () => {
    vi.mocked(apiGetJson).mockResolvedValue({ connectionId: "connection/1", providerId: "alpaca", asOfUtc: "2026-11-11T00:00:00Z", isTruncated: false, observations: [] });
    await getProviderVerificationHistory("connection/1", { allowDevelopmentFallback: true });
    expect(apiGetJson).toHaveBeenCalledWith("/api/workstation/provider-verification-history/connection%2F1", { allowDevelopmentFallback: false });
    await expect(getProviderVerificationHistory("connection/2")).rejects.toThrow(/selected connection/);
  });

  it("refuses a provider timeline with an invalid event timestamp", async () => {
    vi.mocked(apiGetJson).mockResolvedValue({ connectionId: "c1", providerId: "alpaca", asOfUtc: "2026-11-11T00:00:00Z", isTruncated: false, observations: [{ recordedAtUtc: "unknown", succeeded: true }] });
    await expect(getProviderVerificationHistory("c1")).rejects.toThrow(/incomplete evidence/);
  });

  it("preserves exact financial strings and refuses rounded or foreign account evidence", async () => {
    const accountId = "11111111-1111-1111-1111-111111111111";
    const history = { accountId, asOfUtc: "2026-11-11T00:00:00Z", isTruncated: false, points: [{
      accountId, snapshotId: "snapshot-1", asOfDate: "2026-11-10", recordedAtUtc: "2026-11-11T00:00:00Z", currency: "JPY", source: "retained-statement",
      cashBalance: "123456789012345678.123456789", securitiesMarketValue: null
    }] };
    vi.mocked(apiGetJson).mockResolvedValue(history);
    expect((await getAccountBalanceHistory(accountId)).points[0].cashBalance).toBe(history.points[0].cashBalance);
    vi.mocked(apiGetJson).mockResolvedValue({ ...history, points: [{ ...history.points[0], cashBalance: 1.23 }] });
    await expect(getAccountBalanceHistory(accountId)).rejects.toThrow(/incomplete evidence/);
    vi.mocked(apiGetJson).mockResolvedValue({ ...history, points: [{ ...history.points[0], accountId: "foreign-account" }] });
    await expect(getAccountBalanceHistory(accountId)).rejects.toThrow(/selected account/);
  });
});
