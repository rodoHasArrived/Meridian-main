// @vitest-environment node
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { build, type UserConfig } from "vite";
import { describe, expect, it } from "vitest";
import config from "../vite.config";

describe("published font licenses", () => {
  it("copies both canonical OFL notices through Vite's public asset pipeline", async () => {
    const outDir = mkdtempSync(join(tmpdir(), "meridian-font-notices-"));
    try {
      const userConfig = config as UserConfig;
      // Build a small real entry into a disposable distribution. Use the actual
      // workstation publicDir setting, including Vite's default when omitted.
      await build({
        configFile: false,
        root: userConfig.root,
        publicDir: userConfig.publicDir,
        logLevel: "silent",
        build: {
          outDir,
          write: true,
          lib: { entry: resolve(process.cwd(), "src/lib/theme.ts"), formats: ["es"] }
        }
      });
      for (const filename of ["OFL-IBMPlexSans.txt", "OFL-IBMPlexMono.txt"]) {
        const published = readFileSync(resolve(outDir, "notices", filename));
        const canonical = readFileSync(resolve(process.cwd(), "../../../Meridian Design System/assets/fonts", filename));
        expect(published, filename).toEqual(canonical);
      }
    } finally {
      rmSync(outDir, { recursive: true, force: true });
    }
  });
});
