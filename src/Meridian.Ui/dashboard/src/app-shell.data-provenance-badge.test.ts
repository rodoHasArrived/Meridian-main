import { describe, expect, it } from "vitest";
import {
  buildDataProvenanceBadgeViewModel,
  normalizeDataProvenance,
  resolveWorkstationDataProvenance
} from "@/app-shell.data-provenance-badge";

describe("buildDataProvenanceBadgeViewModel", () => {
  it("renders no badge for real data", () => {
    const badge = buildDataProvenanceBadgeViewModel({ provenance: "real" });

    expect(badge.visible).toBe(false);
    expect(badge.label).toBe("");
    // Persistent by construction even when hidden — never a dismissable toggle.
    expect(badge.dismissable).toBe(false);
  });

  it.each([
    ["simulated", "SIMULATED", "Simulated data"],
    ["seeded", "SEEDED", "Seeded demo data"],
    ["sample", "SAMPLE", "Sample data"],
    ["unknown", "UNKNOWN", "Data source unverified"]
  ] as const)("labels %s data as a persistent, non-dismissable badge", (provenance, label, headline) => {
    const badge = buildDataProvenanceBadgeViewModel({ provenance });

    expect(badge.visible).toBe(true);
    expect(badge.dismissable).toBe(false);
    expect(badge.role).toBe("status");
    expect(badge.label).toBe(label);
    expect(badge.headline).toBe(headline);
    expect(badge.ariaLabel).toContain(label);
    expect(badge.detail.length).toBeGreaterThan(0);
  });

  it("prefers a caller-supplied detail when present", () => {
    const badge = buildDataProvenanceBadgeViewModel({
      provenance: "simulated",
      detail: "Random-walk simulator; no real fills."
    });

    expect(badge.detail).toBe("Random-walk simulator; no real fills.");
  });
});

describe("normalizeDataProvenance", () => {
  it.each([
    ["live", "real"],
    ["real", "real"],
    ["simulated", "simulated"],
    ["seeded", "seeded"],
    ["demo", "seeded"],
    ["fixture", "seeded"],
    ["sample", "sample"],
    ["placeholder", "sample"]
  ] as const)("maps %s to %s", (token, expected) => {
    expect(normalizeDataProvenance(token)).toBe(expected);
  });

  it("never upgrades an unknown token to real", () => {
    expect(normalizeDataProvenance("mystery-source")).toBe("simulated");
  });

  it("treats a wire token 'unknown' as a claim to fail closed on, not as the client unknown state", () => {
    expect(normalizeDataProvenance("unknown")).toBe("simulated");
  });

  it.each([null, undefined, "", "   ", 0, {}, []])(
    "fails closed for missing or malformed wire value %p",
    (token) => {
      expect(normalizeDataProvenance(token)).toBe("simulated");
    }
  );
});

describe("resolveWorkstationDataProvenance", () => {
  it("uses successful demo-mode provenance even when no development fixture header was observed", () => {
    expect(resolveWorkstationDataProvenance({
      usingDevelopmentFixtures: false,
      demoMode: { enabled: true, provenance: "seeded" }
    })).toBe("seeded");
  });

  it("never accepts a real or missing provenance claim from enabled demo mode", () => {
    expect(resolveWorkstationDataProvenance({
      usingDevelopmentFixtures: false,
      demoMode: { enabled: true, provenance: "real" }
    })).toBe("simulated");
    expect(resolveWorkstationDataProvenance({
      usingDevelopmentFixtures: false,
      demoMode: { enabled: true }
    })).toBe("simulated");
  });

  it("lets development fixture evidence override a disabled demo-mode real posture", () => {
    expect(resolveWorkstationDataProvenance({
      usingDevelopmentFixtures: true,
      demoMode: { enabled: false, provenance: "real" }
    })).toBe("seeded");
  });

  it("claims real data only after the server explicitly reports demo mode disabled", () => {
    // An unanswered probe is `unknown` — a persistent badge with a retry control —
    // never a silent real claim and never a confirmed SIMULATED brand.
    expect(resolveWorkstationDataProvenance({
      usingDevelopmentFixtures: false,
      demoMode: null
    })).toBe("unknown");
    expect(resolveWorkstationDataProvenance({
      usingDevelopmentFixtures: false,
      demoMode: { enabled: false, provenance: "seeded" }
    })).toBe("real");
  });
});
