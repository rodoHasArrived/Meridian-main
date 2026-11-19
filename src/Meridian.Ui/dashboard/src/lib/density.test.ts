import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  applySessionDensity,
  clearSessionDensity,
  DENSITY_STORAGE_KEY,
  readEffectiveDensity,
  readStoredDensity,
  writeStoredDensity
} from "@/lib/density";
import { onOperatorPreferenceChanged } from "@/lib/operator-preference-events";

describe("session density (restored layouts)", () => {
  beforeEach(() => {
    localStorage.clear();
    clearSessionDensity();
    document.body.removeAttribute("data-theme-density");
  });
  afterEach(() => clearSessionDensity());

  it("applies a layout's density without storing or announcing it as the operator preference", () => {
    writeStoredDensity("default");
    const changed = vi.fn();
    const unsubscribe = onOperatorPreferenceChanged(changed);

    applySessionDensity("compact");

    expect(document.body.getAttribute("data-theme-density")).toBe("compact");
    expect(readStoredDensity()).toBe("default");
    expect(localStorage.getItem(DENSITY_STORAGE_KEY)).toBe("default");
    expect(readEffectiveDensity()).toBe("compact");
    expect(changed).not.toHaveBeenCalled();
    unsubscribe();
  });

  it("lets an explicit preference write replace the session density", () => {
    applySessionDensity("compact");

    writeStoredDensity("spacious");

    expect(readEffectiveDensity()).toBe("spacious");
  });

  it("falls back to the stored preference when no session density is active", () => {
    writeStoredDensity("spacious");

    expect(readEffectiveDensity()).toBe("spacious");
  });
});
