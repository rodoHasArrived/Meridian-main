import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import assert from "node:assert/strict";
import { synchronizeDesignSystemAssets } from "./sync-design-system-assets.mjs";

test("native asset projection detects drift and retains the font license in Vite public output", () => {
  const root = mkdtempSync(join(tmpdir(), "meridian-design-assets-"));
  try {
    const packageRoot = join(root, "package"), dashboardRoot = join(root, "dashboard");
    const put = (path, value) => { mkdirSync(join(path, ".."), { recursive: true }); writeFileSync(path, value); };
    put(join(packageRoot, "release.json"), JSON.stringify({ version: "4.3.0" }));
    put(join(packageRoot, "assets/brand/meridian-mark.svg"), "canonical mark");
    put(join(packageRoot, "assets/icons/trading.svg"), "canonical icon");
    put(join(packageRoot, "assets/fonts/IBMPlexSans-Regular.ttf"), "fixture font bytes");
    put(join(packageRoot, "assets/fonts/OFL-IBMPlexSans.txt"), "fixture font license");
    put(join(packageRoot, "tokens/fonts.css"), "@font-face { font-family: 'IBM Plex Sans' }");
    put(join(dashboardRoot, "src/design-system/assets.ts"), 'export const DESIGN_SYSTEM_VERSION = "4.3.0" as const;');
    assert.throws(() => synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }), /asset drift/);
    synchronizeDesignSystemAssets({ dashboardRoot, packageRoot, write: true });
    assert.equal(readFileSync(join(dashboardRoot, "public/licenses/OFL-IBMPlexSans.txt"), "utf8"), "fixture font license");
    assert.equal(synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }).files, 5);
    put(join(dashboardRoot, "src/assets/brand/meridian-mark.svg"), "stale brand");
    assert.throws(() => synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }), /meridian-mark.svg/);
    put(join(packageRoot, "release.json"), JSON.stringify({ version: "4.4.0" }));
    assert.throws(() => synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }), /canonical design-system version 4.4.0/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
