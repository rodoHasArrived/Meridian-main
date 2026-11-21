import { describe, expect, it } from "vitest";
import { isInternalCopy, operatorNote } from "@/lib/internal-copy";

describe("internal copy", () => {
  it("recognises text that describes fixtures or scaffolding", () => {
    expect(isInternalCopy("Adapter wiring from screenshot fixtures.")).toBe(true);
    expect(isInternalCopy("Adapter wiring preview from local development fixtures.")).toBe(true);
    expect(isInternalCopy("Paper account connected through the primary broker.")).toBe(false);
  });

  it("hides fixture copy outside the mock-data build and keeps operator notes everywhere", () => {
    expect(operatorNote("Adapter wiring from screenshot fixtures.", false)).toBeNull();
    expect(operatorNote("Adapter wiring from screenshot fixtures.", true)).toBe("Adapter wiring from screenshot fixtures.");
    expect(operatorNote("Paper account connected through the primary broker.", false)).toBe("Paper account connected through the primary broker.");
    expect(operatorNote("  ", true)).toBeNull();
  });
});
