import { afterEach, describe, expect, it } from "vitest";
import { applyAppearance, APPEARANCE_STORAGE_KEY, readStoredAppearance, writeStoredAppearance } from "@/lib/theme";

afterEach(() => {
  document.documentElement.removeAttribute("data-theme");
  localStorage.clear();
});

describe("theme", () => {
  it("applies manual themes and removes the attribute for system appearance", () => {
    applyAppearance("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");

    applyAppearance("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");

    applyAppearance("system");
    expect(document.documentElement.hasAttribute("data-theme")).toBe(false);
  });

  it("persists only supported appearance values", () => {
    writeStoredAppearance("dark");
    expect(localStorage.getItem(APPEARANCE_STORAGE_KEY)).toBe("dark");
    expect(readStoredAppearance()).toBe("dark");

    localStorage.setItem(APPEARANCE_STORAGE_KEY, "sepia");
    expect(readStoredAppearance()).toBe("system");
  });
});
