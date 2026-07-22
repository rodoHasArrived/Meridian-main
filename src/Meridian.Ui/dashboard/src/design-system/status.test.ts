import { describe, expect, it } from "vitest";
import { normalizeDesignSystemSeverity } from "@/design-system/status";

describe("normalizeDesignSystemSeverity", () => {
  it("escalates unexpected server status strings instead of hiding them as info", () => {
    expect(normalizeDesignSystemSeverity("unmapped-server-criticality")).toBe("action");
  });

  it("preserves explicit informational aliases", () => {
    expect(normalizeDesignSystemSeverity("unknown")).toBe("info");
    expect(normalizeDesignSystemSeverity(null)).toBe("info");
  });
});
