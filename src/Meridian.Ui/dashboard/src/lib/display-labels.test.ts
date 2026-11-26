// @vitest-environment node
import { describe, expect, it } from "vitest";
import { containsPascalCaseIdentifier, executionEventLabel, formatEnumLabel, humanizeIdentifiersInText } from "@/lib/display-labels";

describe("display labels", () => {
  it("turns enum identifiers into sentence-case words", () => {
    expect(formatEnumLabel("PartiallyFilled")).toBe("Partially filled");
    expect(formatEnumLabel("review_required")).toBe("Review required");
    expect(formatEnumLabel("FIXSessionDown")).toBe("FIX session down");
    expect(formatEnumLabel("NAV")).toBe("NAV");
    expect(formatEnumLabel("Open")).toBe("Open");
  });

  it("leaves operator copy alone and falls back for absent values", () => {
    expect(formatEnumLabel("Cash variance needs review")).toBe("Cash variance needs review");
    expect(formatEnumLabel(null)).toBe("—");
    expect(formatEnumLabel("  ", "Unknown")).toBe("Unknown");
  });

  it("labels execution events in plain words, with a humanized fallback", () => {
    expect(executionEventLabel("ControlEvaluated")).toBe("Control check run");
    expect(executionEventLabel("PartialFill")).toBe("Partial fill");
    expect(executionEventLabel("PartiallyFilled")).toBe("Partly filled");
    expect(executionEventLabel("VenueRejected")).toBe("Venue rejected");
    expect(containsPascalCaseIdentifier(executionEventLabel("SomeNewEvent"))).toBe(false);
  });

  it("detects leftover PascalCase identifiers", () => {
    expect(containsPascalCaseIdentifier("PartialFill · PartiallyFilled")).toBe(true);
    expect(containsPascalCaseIdentifier("Partial fill · Partly filled")).toBe(false);
  });

  it("spells out identifiers embedded in a service sentence", () => {
    expect(humanizeIdentifiersInText("SymbolConcentration: NVDA 26.40% (threshold 30.00%, state Observe)."))
      .toBe("Symbol concentration: NVDA 26.40% (threshold 30.00%, state Observe).");
    expect(humanizeIdentifiersInText("Blocked by OrderRateThrottle.")).toBe("Blocked by order rate throttle.");
    expect(humanizeIdentifiersInText("No active guardrails")).toBe("No active guardrails");
  });
});
