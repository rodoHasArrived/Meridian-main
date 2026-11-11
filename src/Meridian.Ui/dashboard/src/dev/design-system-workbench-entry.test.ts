import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { runInNewContext } from "node:vm";
import { describe, expect, it, vi } from "vitest";
import { DESIGN_SYSTEM_WORKBENCH_PATH, isDesignSystemWorkbenchEntry } from "./design-system-workbench-entry";
import { DEFAULT_WORKBENCH_OPTIONS, readWorkbenchOptions, writeWorkbenchOptions, type WorkbenchOptions } from "./design-system-workbench-state";

describe("native workbench entry", () => {
  it("accepts only the dedicated fixture-development path", () => {
    expect(isDesignSystemWorkbenchEntry(DESIGN_SYSTEM_WORKBENCH_PATH, true, "fixture-only")).toBe(true);
    expect(isDesignSystemWorkbenchEntry(`${DESIGN_SYSTEM_WORKBENCH_PATH}/`, true, "fixture-only")).toBe(true);
    for (const path of ["/workstation/", "/workstation/trading", "/workstation/__design-system/extra"]) {
      expect(isDesignSystemWorkbenchEntry(path, true, "fixture-only")).toBe(false);
    }
  });

  it("cannot be enabled in production or connected development", () => {
    expect(isDesignSystemWorkbenchEntry(DESIGN_SYSTEM_WORKBENCH_PATH, false, "fixture-only")).toBe(false);
    expect(isDesignSystemWorkbenchEntry(DESIGN_SYSTEM_WORKBENCH_PATH, true, "backend-connected")).toBe(false);
    expect(isDesignSystemWorkbenchEntry(DESIGN_SYSTEM_WORKBENCH_PATH, true, "")).toBe(false);
  });

  it("keeps startup preference reads out of the reserved preview entry", () => {
    const html = readFileSync(resolve(process.cwd(), "index.html"), "utf8");
    const script = html.match(/<script data-meridian-appearance>([\s\S]*?)<\/script>/)?.[1];
    expect(script).toBeTruthy();
    for (const pathname of [DESIGN_SYSTEM_WORKBENCH_PATH, `${DESIGN_SYSTEM_WORKBENCH_PATH}/`]) {
      const getItem = vi.fn(() => "dark");
      runInNewContext(script!, { window: { location: { pathname } }, localStorage: { getItem }, document: { documentElement: { setAttribute: vi.fn() } } });
      expect(getItem).not.toHaveBeenCalled();
    }
    const getItem = vi.fn(() => "dark");
    const setAttribute = vi.fn();
    runInNewContext(script!, { window: { location: { pathname: "/workstation/trading" } }, localStorage: { getItem }, document: { documentElement: { setAttribute } } });
    expect(getItem).toHaveBeenCalledWith("meridian.workstation.appearance.v1");
    expect(setAttribute).toHaveBeenCalledWith("data-theme", "dark");
  });
});

describe("preview-only URL state", () => {
  it("uses safe defaults for missing or invalid values", () => {
    expect(readWorkbenchOptions(new URLSearchParams())).toEqual(DEFAULT_WORKBENCH_OPTIONS);
    expect(readWorkbenchOptions(new URLSearchParams("theme=other&density=huge&surface=dark&data=deleted&buttons=execute")))
      .toEqual(DEFAULT_WORKBENCH_OPTIONS);
  });

  it("round-trips shareable visual options without carrying application scope", () => {
    const options: WorkbenchOptions = { theme: "system", density: "compact", surface: "light", data: "error", buttons: "busy" };
    const search = writeWorkbenchOptions(options);
    expect(readWorkbenchOptions(search)).toEqual(options);
    expect(search.toString()).toBe("theme=system&density=compact&surface=light&data=error&buttons=busy");
    const applicationSearch = new URLSearchParams("theme=dark&fund=real-fund&account=real-account");
    expect(writeWorkbenchOptions(readWorkbenchOptions(applicationSearch)).toString()).toBe("theme=dark");
    expect(writeWorkbenchOptions(DEFAULT_WORKBENCH_OPTIONS).toString()).toBe("");
  });
});
