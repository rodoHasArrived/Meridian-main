import { describe, expect, it } from "vitest";
import { buildScenarioPrepayments, type ScenarioPrepaymentDraft } from "./scenario-prepayment-inputs";

const draft = (patch: Partial<ScenarioPrepaymentDraft> = {}): ScenarioPrepaymentDraft => ({
  id: "row", streamId: "principal", startDate: "2027-01-01", endDate: "2027-06-01", method: "Cpr", value: ".06000000000001", policy: "PreservePayments", seasoningOrigin: "", ...patch
});

describe("scenario prepayment validation", () => {
  it("preserves decimal input without rounding and accepts adjacent dated intervals", () => {
    const result = buildScenarioPrepayments([draft(), draft({ id: "next", startDate: "2027-06-01", endDate: "2027-12-01", method: "Amount", value: "100000000000000.00000000001" })], "2026-12-31");
    expect(result).toEqual([
      { streamId: "principal", startDate: "2027-01-01", endDate: "2027-06-01", method: "Cpr", value: ".06000000000001", policy: "PreservePayments" },
      { streamId: "principal", startDate: "2027-06-01", endDate: "2027-12-01", method: "Amount", value: "100000000000000.00000000001", policy: "PreservePayments" }
    ]);
  });

  it.each([
    [{ value: "1.00000000000000000001" }, "between 0 and 1"],
    [{ value: "-0.001" }, "cannot be negative"],
    [{ value: "1e-3" }, "decimal value"],
    [{ method: "Psa", seasoningOrigin: "2027-02-01" }, "seasoning origin"],
    [{ startDate: "2026-12-01" }, "on or after the valuation date"],
    [{ endDate: "2027-01-01" }, "end after its start"]
  ] as Array<[Partial<ScenarioPrepaymentDraft>, string]>)("refuses invalid assumption %j", (patch, reason) => {
    expect(buildScenarioPrepayments([draft(patch)], "2026-12-31")).toContain(reason);
  });

  it("allows overlapping dates on different streams", () => {
    expect(buildScenarioPrepayments([draft(), draft({ id: "other", streamId: "other" })], "2026-12-31")).toHaveLength(2);
  });
});
