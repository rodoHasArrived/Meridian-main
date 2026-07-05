import { describe, expect, it } from "vitest";
import { buildCorporateActionInboxModel } from "./data-screen.corporate-action-inbox.view-model";
import type { CorporateActionInboxResponse, CorporateActionProposalEntry } from "@/types";

function proposal(overrides: Partial<CorporateActionProposalEntry> = {}): CorporateActionProposalEntry {
  return {
    securityId: "3f0c9a53-3f8f-4b04-9c1e-0f8f4b049c1e",
    ticker: "GME",
    actionType: "Dividend",
    exDate: "2026-08-14",
    recordDate: null,
    payableDate: "2026-08-28",
    amount: 0.24,
    currency: "USD",
    splitFromFactor: null,
    splitToFactor: null,
    winningSource: "finnhub",
    agreeingSources: ["finnhub"],
    dissentingSources: [],
    autoApplied: false,
    ...overrides
  };
}

function response(overrides: Partial<CorporateActionInboxResponse> = {}): CorporateActionInboxResponse {
  return {
    lastIngestAt: "2026-07-05T12:00:00Z",
    stagedCount: 1,
    appliedLastRun: 2,
    duplicatesSkippedLastRun: 1,
    staged: [proposal()],
    errors: [],
    ...overrides
  };
}

describe("buildCorporateActionInboxModel", () => {
  it("formats dividends with ex-date countdown and consensus", () => {
    const model = buildCorporateActionInboxModel(response(), new Date("2026-07-05T15:00:00Z"));

    const row = model.rows[0];
    expect(row.ticker).toBe("GME");
    expect(row.valueLabel).toBe("0.24 USD");
    expect(row.daysUntilEx).toBe(40);
    expect(row.countdownLabel).toBe("in 40 days");
    expect(row.consensusLabel).toBe("1/1 source agree");
    expect(row.tone).toBe("neutral");
    expect(model.summary).toContain("1 staged proposal");
  });

  it("orders rows by ex-date urgency and flags disputes with a warning tone", () => {
    const model = buildCorporateActionInboxModel(
      response({
        stagedCount: 2,
        staged: [
          proposal({ ticker: "MSFT", exDate: "2026-09-15" }),
          proposal({
            ticker: "AAPL",
            exDate: "2026-07-20",
            splitFromFactor: 1,
            splitToFactor: 4,
            amount: null,
            currency: null,
            agreeingSources: ["tiingo"],
            dissentingSources: ["alphavantage"]
          })
        ]
      }),
      new Date("2026-07-05T15:00:00Z")
    );

    expect(model.rows.map((row) => row.ticker)).toEqual(["AAPL", "MSFT"]);
    const disputed = model.rows[0];
    expect(disputed.tone).toBe("warning");
    expect(disputed.valueLabel).toBe("4:1 split");
    expect(disputed.consensusLabel).toBe("1/2 sources agree");
    expect(disputed.dissentingSources).toEqual(["alphavantage"]);
  });

  it("reports an empty inbox without rows", () => {
    const model = buildCorporateActionInboxModel(
      response({ stagedCount: 0, staged: [], lastIngestAt: null })
    );

    expect(model.rows).toHaveLength(0);
    expect(model.lastIngestLabel).toBe("never");
    expect(model.summary).toContain("No staged corporate actions");
  });
});
