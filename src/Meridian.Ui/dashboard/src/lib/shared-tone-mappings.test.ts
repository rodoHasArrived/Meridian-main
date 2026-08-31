import { describe, expect, it } from "vitest";
import {
  badgeVariantToMetricTone,
  badgeVariantToOperatorSeverity,
  badgeVariantToSeverityStatus,
  categoricalVariantToSeverityStatus,
  evidenceStatusToneToTextClass,
  readinessToneToBadgeVariant,
  readinessToneToPanelClass,
  readinessToneToSeverityStatus,
  semanticToneToMetricCardTone,
  semanticToneToTextClass
} from "@/lib/shared-tone-mappings";

describe("shared tone mappings", () => {
  it("maps badge variants to severity labels and metric tones", () => {
    expect(badgeVariantToSeverityStatus("success")).toBe("Ready");
    expect(badgeVariantToSeverityStatus("warning")).toBe("ReviewRequired");
    expect(badgeVariantToSeverityStatus("danger")).toBe("Blocked");
    expect(badgeVariantToSeverityStatus("default")).toBe("Info");
    expect(badgeVariantToMetricTone("outline")).toBe("neutral");
    expect(badgeVariantToOperatorSeverity("warning")).toBe("action");
    expect(categoricalVariantToSeverityStatus("paper")).toBe("review");
    expect(categoricalVariantToSeverityStatus("warning")).toBe("action");
    expect(semanticToneToMetricCardTone("default")).toBe("neutral");
  });

  it("maps readiness tones to shared variants and utility classes", () => {
    expect(readinessToneToSeverityStatus("ready")).toBe("Ready");
    expect(readinessToneToSeverityStatus("blocked")).toBe("Blocked");
    expect(readinessToneToSeverityStatus("pending")).toBe("Pending");
    expect(readinessToneToBadgeVariant("review")).toBe("warning");
    expect(readinessToneToPanelClass("danger")).toBe("border-danger/35 bg-danger/10");
    expect(semanticToneToTextClass("muted")).toBe("text-muted-foreground");
    expect(evidenceStatusToneToTextClass("muted")).toBe("text-foreground");
  });
});
