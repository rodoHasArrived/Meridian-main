import { describe, expect, it } from "vitest";
import {
  EMPTY_HISTORICAL_WINDOW_DRAFT,
  HISTORICAL_WINDOW_IDENTIFIER_KINDS,
  buildHistoricalWindowInput,
  historicalWindowKindLabel,
  utcToday,
  validateHistoricalWindowDraft,
  type HistoricalWindowDraft
} from "./security-passport-historical-window.view-model";

const TODAY = "2026-11-25";

const VALID: HistoricalWindowDraft = {
  kind: "Ticker",
  value: " FB ",
  provider: "",
  validFrom: "2012-05-18",
  validTo: "2022-06-09",
  evidenceReference: " evidence-vault://sm/meta-rename ",
  justification: " Listed as FB until the 2022 ticker change. "
};

describe("validateHistoricalWindowDraft", () => {
  it("accepts a complete historical window", () => {
    expect(validateHistoricalWindowDraft(VALID, TODAY)).toEqual({ valid: true, reason: null });
  });

  it("accepts an open-ended window", () => {
    expect(validateHistoricalWindowDraft({ ...VALID, validTo: "" }, TODAY).valid).toBe(true);
  });

  it.each<[string, Partial<HistoricalWindowDraft>, RegExp]>([
    ["an unknown kind", { kind: "Unknown" }, /identifier kind/i],
    ["a blank value", { value: "  " }, /identifier value/i],
    ["a provider symbol without a provider", { kind: "ProviderSymbol", provider: " " }, /provider/i],
    ["no start date", { validFrom: "" }, /became valid/i],
    ["a start date of today", { validFrom: TODAY, validTo: "" }, /before today/i],
    ["an end before the start", { validTo: "2012-05-18" }, /after the start/i],
    ["no evidence", { evidenceReference: " " }, /evidence vault/i],
    ["no justification", { justification: "" }, /explain/i]
  ])("refuses %s", (_, patch, reason) => {
    const result = validateHistoricalWindowDraft({ ...VALID, ...patch }, TODAY);
    expect(result.valid).toBe(false);
    expect(result.reason).toMatch(reason);
  });

  it("starts from an invalid empty draft", () => {
    expect(validateHistoricalWindowDraft(EMPTY_HISTORICAL_WINDOW_DRAFT, TODAY).valid).toBe(false);
  });
});

describe("buildHistoricalWindowInput", () => {
  it("trims text, pins dates to UTC midnight and nulls empty optionals", () => {
    expect(buildHistoricalWindowInput({ ...VALID, validTo: "" }, 7, null)).toEqual({
      expectedVersion: 7,
      kind: "Ticker",
      value: "FB",
      provider: null,
      validFrom: "2012-05-18T00:00:00Z",
      validTo: null,
      evidenceReference: "evidence-vault://sm/meta-rename",
      justification: "Listed as FB until the 2022 ticker change.",
      fundProfileId: null
    });
  });

  it("keeps a provider for a provider symbol window", () => {
    const input = buildHistoricalWindowInput({ ...VALID, kind: "ProviderSymbol", provider: " polygon " }, 3, "fund-1");
    expect(input.provider).toBe("polygon");
    expect(input.fundProfileId).toBe("fund-1");
  });
});

describe("utcToday", () => {
  it("formats the UTC date", () => {
    expect(utcToday(new Date("2026-11-25T23:30:00-05:00"))).toBe("2026-11-26");
  });
});

describe("historicalWindowKindLabel", () => {
  it("labels every proposable kind for operators", () => {
    expect(historicalWindowKindLabel("Isin")).toBe("ISIN");
    expect(historicalWindowKindLabel("OccOptionSymbol")).toBe("OCC option symbol");
    for (const kind of HISTORICAL_WINDOW_IDENTIFIER_KINDS) {
      expect(historicalWindowKindLabel(kind).length).toBeGreaterThan(0);
    }
  });
});
