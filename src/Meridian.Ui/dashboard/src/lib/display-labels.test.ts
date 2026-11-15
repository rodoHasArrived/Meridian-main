import { describe, expect, it } from "vitest";
import { containsPascalCaseIdentifier, executionEventLabel, formatEnumLabel } from "@/lib/display-labels";

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
});
