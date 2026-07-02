import { describe, expect, it } from "vitest";
import {
  buildPrivateCapitalFundEventCommandCenterRoute,
  manualJournalPrivateCapitalReadinessTone,
  normalizeQueryValue,
} from "./accounting-screen.view-model.shared";

describe("accounting-screen shared view-model helpers", () => {
  it("normalizes optional query values without inventing defaults", () => {
    expect(normalizeQueryValue(" fund-alpha ")).toBe("fund-alpha");
    expect(normalizeQueryValue("   ")).toBeNull();
    expect(normalizeQueryValue(null)).toBeNull();
  });

  it("builds private-capital command-center routes with valid scoped identifiers", () => {
    expect(buildPrivateCapitalFundEventCommandCenterRoute(
      " fund-alpha ",
      "11111111-1111-1111-1111-111111111111",
      "fund-event-42"
    )).toBe("/api/ledger/private-capital/fund-event-command-center?fundProfileId=fund-alpha&ledgerBookId=11111111-1111-1111-1111-111111111111&fundEventId=fund-event-42");

    expect(buildPrivateCapitalFundEventCommandCenterRoute(
      "fund-alpha",
      "not-a-guid",
      "fund-event-42"
    )).toBe("/api/ledger/private-capital/fund-event-command-center?fundProfileId=fund-alpha&fundEventId=fund-event-42");
  });

  it("maps private-capital readiness into existing accounting tones", () => {
    expect(manualJournalPrivateCapitalReadinessTone("Blocked")).toBe("danger");
    expect(manualJournalPrivateCapitalReadinessTone("Ready")).toBe("success");
    expect(manualJournalPrivateCapitalReadinessTone("ReportReview")).toBe("warning");
    expect(manualJournalPrivateCapitalReadinessTone(null)).toBe("outline");
  });
});
