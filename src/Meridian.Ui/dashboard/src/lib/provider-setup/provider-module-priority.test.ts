import { describe, expect, it } from "vitest";
import {
  PROVIDER_MODULE_PRIORITY_RULE,
  parseProviderModulePriority,
  validateProviderModulePriority
} from "./provider-module-priority";

describe("provider module priority", () => {
  it("accepts whole numbers from 1 to 999, as the edit drawer's number input does", () => {
    expect(parseProviderModulePriority("1")).toBe(1);
    expect(parseProviderModulePriority("100")).toBe(100);
    expect(parseProviderModulePriority("999")).toBe(999);
    expect(parseProviderModulePriority(" 42 ")).toBe(42);
    expect(parseProviderModulePriority(250)).toBe(250);
    expect(validateProviderModulePriority("10")).toBe(true);
  });

  it.each(["", " ", "0", "1000", "-5", "+5", "2.5", "1e2", "abc", "12abc", "007", "0100"])(
    "refuses %j with the range rule",
    (draft) => {
      expect(parseProviderModulePriority(draft)).toBeNull();
      expect(validateProviderModulePriority(draft)).toBe(PROVIDER_MODULE_PRIORITY_RULE);
    }
  );

  it("refuses values that are not text or numbers", () => {
    expect(parseProviderModulePriority(null)).toBeNull();
    expect(parseProviderModulePriority(undefined)).toBeNull();
    expect(parseProviderModulePriority(Number.NaN)).toBeNull();
    expect(parseProviderModulePriority(12.5)).toBeNull();
  });

  it("states the range in the message", () => {
    expect(PROVIDER_MODULE_PRIORITY_RULE).toBe("Enter a whole number from 1 to 999.");
  });
});
