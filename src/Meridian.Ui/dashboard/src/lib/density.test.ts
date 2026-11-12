import { afterEach, describe, expect, it } from "vitest";
import { applyDensity, readStoredDensity, writeStoredDensity } from "@/lib/density";

afterEach(() => {
  localStorage.clear();
  document.body.removeAttribute("data-theme-density");
});

describe("stored display density", () => {
  it("restores Terminal from the browser preference before a settings control mounts", () => {
    writeStoredDensity("terminal");
    applyDensity(readStoredDensity());
    expect(document.body).toHaveAttribute("data-theme-density", "terminal");
    applyDensity("default");
    expect(document.body).not.toHaveAttribute("data-theme-density");
  });

  it("falls back to the existing default for invalid or inaccessible preferences", () => {
    localStorage.setItem("meridian.workstation.density.v1", "unknown");
    expect(readStoredDensity()).toBe("default");
    const unavailable = { getItem: () => { throw new Error("Storage unavailable"); } } as unknown as Storage;
    expect(readStoredDensity(undefined, unavailable)).toBe("default");
  });
});
