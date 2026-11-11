import { createHash } from "node:crypto";
import { existsSync, mkdtempSync, mkdirSync, writeFileSync, readFileSync, readdirSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import assert from "node:assert/strict";
import { projectFontCss, synchronizeDesignSystemAssets } from "./sync-design-system-assets.mjs";

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");

/** Stand-in for the fonttools converter: deterministic "WOFF2" bytes plus the provenance manifest. */
function fakeConvert({ sourceDirectory, targetDirectory }) {
  const fonts = {};
  for (const name of readdirSync(sourceDirectory).filter((n) => n.endsWith(".ttf")).sort()) {
    const source = readFileSync(join(sourceDirectory, name));
    const woff2 = Buffer.concat([Buffer.from("wOF2:"), source]);
    const target = name.replace(/\.ttf$/, ".woff2");
    writeFileSync(join(targetDirectory, target), woff2);
    fonts[target] = { source: `assets/fonts/${name}`, sourceSha256: sha256(source), woff2Sha256: sha256(woff2) };
  }
  writeFileSync(join(targetDirectory, "woff2-sources.json"), JSON.stringify({ fonts }));
}

function fixture() {
  const root = mkdtempSync(join(tmpdir(), "meridian-design-assets-"));
  const packageRoot = join(root, "package"), dashboardRoot = join(root, "dashboard");
  const put = (path, value) => { mkdirSync(join(path, ".."), { recursive: true }); writeFileSync(path, value); };
  put(join(packageRoot, "release.json"), JSON.stringify({ version: "4.3.0" }));
  put(join(packageRoot, "assets/brand/meridian-mark.svg"), "canonical mark");
  put(join(packageRoot, "assets/icons/trading.svg"), "canonical icon");
  put(join(packageRoot, "assets/fonts/IBMPlexSans-Regular.ttf"), "fixture font bytes");
  put(join(packageRoot, "assets/fonts/OFL-IBMPlexSans.txt"), "fixture font license");
  put(join(packageRoot, "tokens/fonts.css"),
    `@font-face { font-family: "IBM Plex Sans"; src: local("IBM Plex Sans"), url("../assets/fonts/IBMPlexSans-Regular.ttf") format("truetype"); }`);
  put(join(dashboardRoot, "src/design-system/assets.ts"), 'export const DESIGN_SYSTEM_VERSION = "4.3.0" as const;');
  return { root, packageRoot, dashboardRoot, put };
}

test("native asset projection detects drift and retains the font license in Vite public output", () => {
  const { root, packageRoot, dashboardRoot, put } = fixture();
  try {
    assert.throws(() => synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }), /asset drift/);
    synchronizeDesignSystemAssets({ dashboardRoot, packageRoot, write: true, convertFonts: fakeConvert });
    assert.equal(readFileSync(join(dashboardRoot, "public/licenses/OFL-IBMPlexSans.txt"), "utf8"), "fixture font license");
    assert.equal(synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }).files, 5);
    put(join(dashboardRoot, "src/assets/brand/meridian-mark.svg"), "stale brand");
    assert.throws(() => synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }), /meridian-mark.svg/);
    put(join(packageRoot, "release.json"), JSON.stringify({ version: "4.4.0" }));
    assert.throws(() => synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }), /canonical design-system version 4.4.0/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test("package TTFs are served as WOFF2 with recorded provenance", () => {
  const { root, packageRoot, dashboardRoot, put } = fixture();
  try {
    synchronizeDesignSystemAssets({ dashboardRoot, packageRoot, write: true, convertFonts: fakeConvert });
    const fonts = join(dashboardRoot, "src/assets/fonts");
    assert.deepEqual(readdirSync(fonts).sort(), ["IBMPlexSans-Regular.woff2", "woff2-sources.json"]);
    const css = readFileSync(join(dashboardRoot, "src/design-system/fonts.css"), "utf8");
    assert.match(css, /url\("\.\.\/assets\/fonts\/IBMPlexSans-Regular\.woff2"\) format\("woff2"\)/);
    assert.doesNotMatch(css, /\.ttf|truetype/);

    // A changed package TTF invalidates the recorded conversion; check mode reports it without Python.
    put(join(packageRoot, "assets/fonts/IBMPlexSans-Regular.ttf"), "updated font bytes");
    assert.throws(() => synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }), /IBMPlexSans-Regular\.woff2/);
    synchronizeDesignSystemAssets({ dashboardRoot, packageRoot, write: true, convertFonts: fakeConvert });
    synchronizeDesignSystemAssets({ dashboardRoot, packageRoot });

    // A hand-edited WOFF2 no longer matches its recorded hash.
    put(join(fonts, "IBMPlexSans-Regular.woff2"), "tampered");
    assert.throws(() => synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }), /IBMPlexSans-Regular\.woff2/);
    synchronizeDesignSystemAssets({ dashboardRoot, packageRoot, write: true, convertFonts: fakeConvert });

    // A leftover TTF copy is drift, and write mode removes it.
    put(join(fonts, "IBMPlexSans-Regular.ttf"), "fixture font bytes");
    assert.throws(() => synchronizeDesignSystemAssets({ dashboardRoot, packageRoot }), /only WOFF2 is shipped/);
    synchronizeDesignSystemAssets({ dashboardRoot, packageRoot, write: true, convertFonts: fakeConvert });
    assert.equal(existsSync(join(fonts, "IBMPlexSans-Regular.ttf")), false);

    // A converter that leaves stale output fails loudly instead of reporting success.
    put(join(packageRoot, "assets/fonts/IBMPlexSans-Regular.ttf"), "third revision");
    assert.throws(() => synchronizeDesignSystemAssets({ dashboardRoot, packageRoot, write: true, convertFonts: () => {} }), /conversion left font drift/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test("font CSS projection rewrites every TTF source and rejects unrecognised ones", () => {
  const projected = projectFontCss(`src: url("../assets/fonts/A-Regular.ttf") format("truetype");\nsrc: url('../assets/fonts/B-Bold.ttf')  format('truetype');`);
  assert.match(projected, /A-Regular\.woff2"\) format\("woff2"\)/);
  assert.match(projected, /B-Bold\.woff2'\)  format\('woff2'\)/);
  assert.throws(() => projectFontCss(`src: url("../fonts/A.ttf") format("truetype");`), /does not recognise/);
});
