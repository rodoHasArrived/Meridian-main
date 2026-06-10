import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  getCapitalAccountWorkbench,
  getPrivateCapitalActivity,
  getPrivateCapitalCapitalAccountSubledger,
  getPrivateCapitalFundEventRecord,
  getPrivateCapitalReportOutput,
  resetDevelopmentFixtureUsage
} from "@/lib/api";

describe("private-capital API wiring", () => {
  const fetchMock = vi.fn();

  beforeEach(() => {
    fetchMock.mockReset();
    fetchMock.mockResolvedValue({
      ok: true,
      headers: { get: () => null },
      json: async () => ({}),
      text: async () => "{}"
    });
    vi.stubGlobal("fetch", fetchMock);
    resetDevelopmentFixtureUsage();
  });

  it("loads private-capital activity, event records, subledgers, and report outputs from shared ledger routes", async () => {
    const controller = new AbortController();

    await getPrivateCapitalActivity(
      {
        fundProfileId: "fund alpha",
        ledgerBookId: "book / 1",
        fundEventId: "fund-event:fund-alpha:capital-call:20260630",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1"
      },
      { signal: controller.signal }
    );
    await getPrivateCapitalFundEventRecord(
      {
        fundProfileId: "fund alpha",
        ledgerBookId: "book / 1",
        fundEventId: "fund-event:fund-alpha:capital-call:20260630"
      },
      { signal: controller.signal }
    );
    await getPrivateCapitalCapitalAccountSubledger(
      {
        fundProfileId: "fund alpha",
        ledgerBookId: "book / 1",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD"
      },
      { signal: controller.signal }
    );
    await getPrivateCapitalReportOutput(
      {
        fundProfileId: "fund alpha",
        ledgerBookId: "book / 1",
        reportOutputId: "report-output:fund-alpha:capital-call-notice",
        reportPackId: "report-pack / 1",
        fundEventId: "fund-event:fund-alpha:capital-call:20260630",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1"
      },
      { signal: controller.signal }
    );
    await getCapitalAccountWorkbench(
      {
        fundProfileId: "fund alpha",
        ledgerBookId: "book / 1",
        fundEventId: "fund-event:fund-alpha:capital-call:20260630",
        capitalAccountId: "capital-account:fund-alpha:lp-1",
        investorId: "investor:lp-1",
        currency: "USD"
      },
      { signal: controller.signal }
    );

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "/api/ledger/private-capital/activity?fundProfileId=fund+alpha&ledgerBookId=book+%2F+1&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      "/api/ledger/private-capital/fund-event-record?fundProfileId=fund+alpha&ledgerBookId=book+%2F+1&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      "/api/ledger/private-capital/capital-account-subledger?fundProfileId=fund+alpha&ledgerBookId=book+%2F+1&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1&currency=USD",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      4,
      "/api/ledger/private-capital/report-output?fundProfileId=fund+alpha&ledgerBookId=book+%2F+1&reportOutputId=report-output%3Afund-alpha%3Acapital-call-notice&reportPackId=report-pack+%2F+1&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1",
      expect.objectContaining({ signal: controller.signal })
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      5,
      "/api/ledger/private-capital/capital-account-workbench?fundProfileId=fund+alpha&ledgerBookId=book+%2F+1&fundEventId=fund-event%3Afund-alpha%3Acapital-call%3A20260630&capitalAccountId=capital-account%3Afund-alpha%3Alp-1&investorId=investor%3Alp-1&currency=USD",
      expect.objectContaining({ signal: controller.signal })
    );
  });
});
