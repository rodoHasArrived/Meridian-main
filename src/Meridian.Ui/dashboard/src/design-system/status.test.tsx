import { describe, expect, it } from "vitest";
import { normalizeDesignSystemSeverity, worstDesignSystemSeverity } from "./status";

describe("canonical presentation severity", () => {
  it("rolls up the worst source severity while retaining the five existing categories", () => {
    expect(worstDesignSystemSeverity(["Ready", "Info", "Review", "Degraded", "Blocked"])).toBe("blocked");
    expect(worstDesignSystemSeverity(["Ready", "Review", "Degraded"])).toBe("action");
    expect(worstDesignSystemSeverity(["Ready", "Review"])).toBe("review");
    expect(worstDesignSystemSeverity(["Ready", null])).toBe("info");
    expect(worstDesignSystemSeverity(["Ready", "Healthy"])).toBe("ready");
  });
  it("never promotes absent or unrecognized evidence to Ready", () => {
    expect(worstDesignSystemSeverity([])).toBe("info");
    expect(normalizeDesignSystemSeverity("NewServerState")).toBe("action");
    expect(worstDesignSystemSeverity(["Ready", "NewServerState"])).toBe("action");
  });
});
