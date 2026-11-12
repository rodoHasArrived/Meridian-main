// @vitest-environment node
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { build, type UserConfig } from "vite";
import { describe, expect, it } from "vitest";
import config, { meridianAssetsInlineLimit } from "../vite.config";

const settings = config as UserConfig;
const dashboardRoot = path.resolve(__dirname, "..");
const favicon = path.join(dashboardRoot, "src/assets/brand/meridian-mark.svg");

describe("HTML-linked asset URLs", () => {
  it("keeps the favicon's JavaScript and HTML references on the emitted file", () => {
    expect(readFileSync(path.join(dashboardRoot, "index.html"), "utf8"))
      .toContain('href="/src/assets/brand/meridian-mark.svg"');
    expect(meridianAssetsInlineLimit(favicon)).toBe(false);
    expect(settings.build?.assetsInlineLimit).toBe(meridianAssetsInlineLimit);
    expect(meridianAssetsInlineLimit(path.join(dashboardRoot, "src/assets/brand/meridian-mark-light.svg")))
      .toBeUndefined();
  });

  it("emits the same favicon URL even when only JavaScript has processed it", async () => {
    const scratch = mkdtempSync(path.join(tmpdir(), "meridian-asset-order-"));
    try {
      const entry = path.join(scratch, "entry.js");
      writeFileSync(entry, `import mark from ${JSON.stringify(favicon)}; globalThis.__meridianAssetUrl = mark;`);
      const render = async (assetsInlineLimit: NonNullable<UserConfig["build"]>["assetsInlineLimit"]) => {
        const result = await build({
          configFile: false,
          root: dashboardRoot,
          base: settings.base,
          publicDir: false,
          logLevel: "silent",
          build: {
            assetsInlineLimit,
            write: false,
            minify: false,
            rolldownOptions: { input: entry }
          }
        });
        const outputs = (Array.isArray(result) ? result : [result]).flatMap(item => "output" in item ? item.output : []);
        return {
          code: outputs.filter(item => item.type === "chunk").map(item => item.code).join("\n"),
          files: outputs.map(item => item.fileName)
        };
      };

      // With no HTML processing, Vite's default inlines this small SVG. That is the alternate
      // result that previously depended on which pipeline visited the shared favicon first.
      const defaultResult = await render(undefined);
      expect(defaultResult.code).toContain("data:image/svg+xml");

      const first = await render(settings.build?.assetsInlineLimit);
      const second = await render(settings.build?.assetsInlineLimit);
      expect(first.code).not.toContain("data:image/svg+xml");
      expect(first.code).toMatch(/\/workstation\/assets\/meridian-mark-[\w-]+\.svg/);
      expect(first.files).toEqual(expect.arrayContaining([expect.stringMatching(/^assets\/meridian-mark-[\w-]+\.svg$/)]));
      expect(second).toEqual(first);
    } finally {
      rmSync(scratch, { recursive: true, force: true });
    }
  });
});
