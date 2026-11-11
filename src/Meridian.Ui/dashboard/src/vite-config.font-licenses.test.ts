// @vitest-environment node
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { build, type Plugin, type UserConfig } from "vite";
import { describe, expect, it } from "vitest";
import config, { createMeridianFontLicensePlugin } from "../vite.config";

describe("published font licenses", () => {
  it("registers the license emitter in the production configuration", () => {
    expect((config as UserConfig).plugins?.some((plugin) => (plugin as Plugin)?.name === "meridian-font-licenses")).toBe(true);
  });

  it("emits both canonical OFL notices into the published asset bundle", async () => {
    // Build a small real entry in memory: exercise Vite's emission pipeline without
    // rewriting the checked-in workstation distribution or compiling the whole UI.
    const result = await build({
      configFile: false,
      publicDir: false,
      logLevel: "silent",
      plugins: [createMeridianFontLicensePlugin()],
      build: {
        write: false,
        lib: { entry: resolve(process.cwd(), "src/lib/theme.ts"), formats: ["es"] }
      }
    });
    const outputs = Array.isArray(result) ? result : [result];
    const assets = outputs.flatMap((output) => "output" in output ? output.output : [])
      .filter((output) => output.type === "asset");
    for (const filename of ["OFL-IBMPlexSans.txt", "OFL-IBMPlexMono.txt"]) {
      const asset = assets.find((output) => output.fileName === `assets/fonts/${filename}`);
      expect(asset, filename).toBeDefined();
      const canonical = readFileSync(resolve(process.cwd(), "../../../Meridian Design System/assets/fonts", filename));
      expect(Buffer.from(asset!.source)).toEqual(canonical);
    }
  });
});
