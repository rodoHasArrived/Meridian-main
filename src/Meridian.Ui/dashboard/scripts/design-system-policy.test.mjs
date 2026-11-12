import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { createHash } from "node:crypto";
import { analyzeSource, analyzeFiles, walkSources, vendorRuntimeStatus } from "./design-system-policy.mjs";

const codes = (file, source) => analyzeSource(file, source).map((diagnostic) => diagnostic.code);

test("documentation, token definitions, comments, identifiers and report branding data are not rendered styles", () => {
  assert.deepEqual(codes("README.md", "Copper #A85436; run #1841; border-radius: 12px;"), []);
  assert.deepEqual(codes("tokens.css", ":root{--accent:#A85436;--overlay:rgba(0,0,0,.2)}/* color:#fff */"), []);
  assert.deepEqual(codes("fixtures.ts", 'const run = "#1841"; const theme = { accentColor: "#AA5500", backgroundColor: "#fff" };'), []);
});

test("actual CSS colors include hex, named colors and numeric RGB/HSL functions", () => {
  assert.deepEqual(codes("component.css", ".x{color:#fff;background:rgba(0,0,0,.2);border-color:hsl(10 20% 30%)}"), ["raw-color", "raw-color", "raw-hex"]);
  assert.deepEqual(codes("component.css", ".x{color:white}"), ["raw-color"]);
  assert.deepEqual(codes("component.css", ".x{border:1px solid white;box-shadow:0 1px 2px black}"), ["raw-color", "raw-color"]);
});

test("balanced token fallbacks and token-backed channels are valid", () => {
  assert.deepEqual(codes("component.css", ".x{color:var(--fg,var(--other,#fff));background:hsl(var(--background) / .5);border-color:color-mix(in srgb,var(--border) 50%,transparent)}"), []);
});

test("inline HTML and JSX presentation attributes cannot hide literal paint", () => {
  assert.deepEqual(codes("demo.html", '<p>Run #1841</p><svg><text fill="#fff">amount</text></svg><div style="background:rgb(2,3,4)"></div>'), ["raw-color", "raw-hex"]);
  assert.deepEqual(codes("chart.tsx", 'const chart = <svg><text fill="#fff" style={{backgroundColor:"#eee"}}>value</text></svg>;'), ["raw-hex", "raw-hex"]);
});

test("injected CSS, conditional style values and canvas assignments are inspected", () => {
  const source = 'const CSS = `.x{color:#fff;}`; const x = <div style={{color: on ? "#fff" : "var(--fg)"}}/>; ctx.fillStyle="#000";';
  assert.deepEqual(codes("chart.tsx", source), ["raw-hex", "raw-hex", "raw-hex"]);
  assert.deepEqual(codes("chart.tsx", 'const x=<div style={{background:`rgba(217,138,100,${alpha})`,borderRadius:12}}/>;'), ["large-radius", "raw-color"]);
});

test("interpolation holes remain valid CSS while static paint is still audited", () => {
  assert.deepEqual(codes("demo.html", '<div style="background:${bg};color:var(--fg)"></div>'), []);
  assert.deepEqual(codes("demo.jsx", 'const css=`.x{width:${width}px;color:#fff;}`;'), ["raw-hex"]);
});

test("surface radius policy preserves functional circles and catches oversized surfaces", () => {
  assert.deepEqual(codes("component.css", ".x{border-radius:12px}.circle{border-radius:50%}.dot{width:8px;height:8px;border-radius:9999px}"), ["large-radius"]);
  assert.deepEqual(codes("component.css", ".pill{padding:6px 12px;border-radius:9999px}"), ["large-radius"]);
});

test("gradients require semantic role plus token-backed endpoints", () => {
  assert.deepEqual(codes("demo.css", ".x{background:linear-gradient(var(--a),var(--b))}"), ["decorative-gradient"]);
  assert.deepEqual(codes("demo.css", ".legend{/* @design-role quantitative-scale */background:linear-gradient(var(--a),var(--b))}"), []);
  assert.deepEqual(codes("demo.css", ".legend{/* @design-role quantitative-scale */background:linear-gradient(#fff,#000)}"), ["decorative-gradient", "raw-hex", "raw-hex"]);
  assert.deepEqual(codes("demo.css", ".sweep{/* @design-role loading-sweep */background:linear-gradient(90deg,transparent,var(--bg),transparent);animation:sweep 1s}@media(prefers-reduced-motion:reduce){.sweep{animation:none}}"), []);
  assert.deepEqual(codes("demo.css", ".sweep{/* @design-role loading-sweep */background:linear-gradient(90deg,transparent,var(--bg),transparent);animation:sweep 1s}"), ["decorative-gradient"]);
  assert.deepEqual(codes("demo.css", ".sweep{/* @design-role loading-sweep */background:linear-gradient(90deg,transparent,var(--bg),transparent);animation:sweep 1s}@media(prefers-reduced-motion:reduce){.other{animation:none}}"), ["decorative-gradient"]);
});

test("a semantic role does not exempt unrelated declarations", () => {
  assert.deepEqual(codes("demo.css", ".legend{/* @design-role quantitative-scale */background:linear-gradient(var(--a),var(--b));color:#fff;}"), ["raw-hex"]);
});

test("malformed style sources fail closed with an actionable diagnostic", () => {
  assert.deepEqual(codes("demo.css", ".x{color"), ["style-parse"]);
});

test("fixed framework palettes and oversized geometry cannot bypass semantic roles", () => {
  assert.deepEqual(codes("component.tsx", 'const x=<div className="bg-slate-900 text-slate-100 rounded-3xl shadow-2xl"/>;'), ["unapproved-utility", "unapproved-utility", "unapproved-utility", "unapproved-utility"]);
  assert.deepEqual(codes("component.tsx", 'const x=<div className={cn("bg-card text-foreground rounded-xl shadow-float", good ? "border-success" : "border-danger")}/>;'), []);
  assert.deepEqual(codes("component.css", ".x{@apply bg-slate-900 text-slate-100}"), ["unapproved-utility", "unapproved-utility"]);
  assert.deepEqual(codes("component.css", ".x{@apply bg-[hsl(var(--panel-strong)/0.96)]}"), []);
  assert.deepEqual(codes("component.tsx", 'const x=<div className="bg-[hsl(10_20%_30%)]"/>;'), ["unapproved-utility"]);
  assert.deepEqual(codes("component.tsx", 'const x=<div className="shadow-[0_2px_6px_rgba(0,0,0,.18)] bg-white text-black"/>;'), ["unapproved-utility", "unapproved-utility", "unapproved-utility"]);
  assert.deepEqual(codes("component.tsx", 'const x=<div className="shadow-[var(--shadow-menu)]"/>;'), []);
});

test("authored support and bootstrap basenames are audited rather than blanket-exempted", () => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "meridian-policy-"));
  try {
    fs.mkdirSync(path.join(directory, "components"));
    for (const name of ["support.js", "ds-base.js", "_ds_bundle.js"]) {
      fs.writeFileSync(path.join(directory, "components", name), 'const CSS=`.x{color:#fff}`;');
    }
    const files = walkSources(directory);
    assert.equal(files.length, 3);
    assert.deepEqual(analyzeFiles(files, directory).map((item) => item.code), ["raw-hex", "raw-hex", "raw-hex"]);
  } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});

test("vendor support requires the exact registered path, reviewed content and generated header", () => {
  const packageRoot = path.resolve(os.tmpdir(), "meridian-vendor-fixture");
  const source = '// GENERATED reviewed vendor\nconst vendor = "opaque upstream code";\n';
  const registry = { requiredHeader: "// GENERATED reviewed vendor", files: {
    "templates/example/support.js": createHash("sha256").update(source).digest("hex")
  } };
  const options = { packageRoot, registry };
  const registered = path.join(packageRoot, "templates/example/support.js");
  assert.deepEqual(vendorRuntimeStatus(registered, source, options), { skip: true });
  assert.deepEqual(vendorRuntimeStatus(registered, source.replace(/\n/g, "\r\n"), options), { skip: true });
  assert.equal(vendorRuntimeStatus(registered, source + '.x{color:#fff}', options).code, "vendor-runtime-integrity");
  assert.equal(vendorRuntimeStatus(path.join(packageRoot, "components/support.js"), source, options), null);
  const wrongHeader = { ...registry, requiredHeader: "// GENERATED another upstream" };
  assert.equal(vendorRuntimeStatus(registered, source, { packageRoot, registry: wrongHeader }).code, "vendor-runtime-integrity");
});

test("workstation HSL channels cannot be used as complete CSS colors", () => {
  const source = '.x{outline:2px solid var(--primary);border-color:var(--border,#fff);background:color-mix(in srgb,var(--accent) 10%,transparent)}';
  assert.deepEqual(analyzeSource("native.css", source, { channelTokens: true }).map((item) => item.code), ["channel-color-collision", "channel-color-collision", "channel-color-collision"]);
  assert.deepEqual(codes("package.css", source), []);
  assert.deepEqual(analyzeSource("native.css", '.x{color:hsl(var(--primary));background:hsl(var(--background)/.8);border-color:var(--ws-border)}', { channelTokens: true }), []);
  assert.deepEqual(analyzeSource("native.tsx", 'const x=<div className="outline-[var(--primary)] bg-[hsl(var(--background)/.8)]"/>;', { channelTokens: true }).map((item) => item.code), ["channel-color-collision"]);
});
