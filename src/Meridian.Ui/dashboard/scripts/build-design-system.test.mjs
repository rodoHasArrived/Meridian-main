import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import vm from "node:vm";
import { test } from "node:test";
import React from "react";
import { defaultPackageRoot, generateDesignSystem, writeDesignSystem } from "./build-design-system.mjs";

async function fixture(t, extra = {}) {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "meridian-design-build-"));
  t.after(() => fs.rm(root, { recursive: true, force: true }));
  const files = {
    "scripts/public-exports.json": JSON.stringify({ schemaVersion: 1, namespace: "TestDesignSystem", components: [
      { name: "Widget", sourcePath: "components/core/Widget.jsx" },
      { name: "Helpers", sourcePath: "components/core/helper.js" }
    ] }),
    "styles.css": '@import "./tokens/colors.css";\n',
    "tokens/colors.css": ':root{--bg:#FFFFFF;} @media(prefers-color-scheme:dark){:root{--bg:#121212;}}',
    "components/core/Widget.jsx": 'import React from "react"; import { Helpers } from "./helper";\nconst css = `.widget{color:var(--bg,#FFFFFF);}`;\nexport function Widget(){return <div>{Helpers.value}</div>;}',
    "components/core/helper.js": 'window.helperLoads = (window.helperLoads || 0) + 1; export const Helpers = {value:"sample"};',
    "components/core/widget.card.html": '<!-- @dsCard group="Core" viewport="900x640" name="Widget" subtitle="A representative component" -->',
    "tests/behavior.html": '<!-- @dsCard group="Testing" viewport="900x640" name="Behavior" subtitle="An executable catalog entry" -->',
    "templates/widget/Widget.dc.html": '<!-- @template name="Widget screen" description="Template example" -->',
    ...extra
  };
  for (const [filename, source] of Object.entries(files)) {
    await fs.mkdir(path.dirname(path.join(root, filename)), { recursive: true });
    await fs.writeFile(path.join(root, filename), source);
  }
  return root;
}

test("generates deterministic relative module graphs and preserves public namespace exports", async (t) => {
  const root = await fixture(t);
  const first = await generateDesignSystem(root);
  assert.deepEqual(await generateDesignSystem(root), first);
  const componentPath = path.join(root, "components/core/Widget.jsx");
  const component = await fs.readFile(componentPath, "utf8");
  await fs.writeFile(componentPath, component.replace(/\n/g, "\r\n"));
  assert.deepEqual(await generateDesignSystem(root), first, "Windows line endings do not alter generated artifacts");
  assert.ok(!first["_ds_bundle.js"].includes(root));
  assert.ok(first["_ds_bundle.js"].includes('.widget{color:var(--bg,#FFFFFF);}'));
  const window = { React };
  vm.runInNewContext(first["_ds_bundle.js"], { window });
  assert.deepEqual(Object.keys(window.TestDesignSystem).sort(), ["Helpers", "Widget", "__errors"]);
  assert.equal(window.TestDesignSystem.Widget().props.children, "sample");
  assert.equal(window.TestDesignSystem.Helpers.value, "sample");
  assert.equal(window.helperLoads, 1, "relative dependencies share one cached module identity");
  assert.equal(window.TestDesignSystem.__errors.length, 0);
  const replacement = () => "consumer override";
  window.TestDesignSystem.Widget = replacement;
  assert.equal(window.TestDesignSystem.Widget, replacement, "namespace export assignment remains compatible");
});

test("allows unused bundle loading without React but reports and throws actual peer failures", async (t) => {
  const output = await generateDesignSystem(await fixture(t));
  const window = {};
  vm.runInNewContext(output["_ds_bundle.js"], { window });
  assert.equal(window.TestDesignSystem.__errors.length, 0);
  assert.throws(() => window.TestDesignSystem.Widget, /requires its React peer/);
  assert.equal(window.TestDesignSystem.__errors.length, 1);
  window.React = React;
  assert.equal(window.TestDesignSystem.Widget().type, "div");
});

test("freshness mode detects source and output drift without rewriting artifacts", async (t) => {
  const root = await fixture(t);
  await writeDesignSystem({ root });
  await writeDesignSystem({ root, check: true });
  const original = await fs.readFile(path.join(root, "_ds_bundle.js"), "utf8");
  await fs.appendFile(path.join(root, "components/core/helper.js"), "\n// intentional source change\n");
  await assert.rejects(writeDesignSystem({ root, check: true }), /Stale generated/);
  assert.equal(await fs.readFile(path.join(root, "_ds_bundle.js"), "utf8"), original);
  await writeDesignSystem({ root });
  await writeDesignSystem({ root, check: true });
  assert.notEqual(await fs.readFile(path.join(root, "_ds_bundle.js"), "utf8"), original);
});

test("manifest derives current cards, templates and separately scoped theme tokens", async (t) => {
  const root = await fixture(t);
  const output = await generateDesignSystem(root);
  const manifest = JSON.parse(output["_ds_manifest.json"]);
  assert.equal(manifest.cards[0].name, "Widget");
  assert.ok(manifest.cards.some((card) => card.path === "tests/behavior.html"));
  assert.equal(manifest.templates[0].entryPath, "templates/widget/Widget.dc.html");
  assert.equal(manifest.tokens.find((token) => token.name === "--bg" && !token.scope).value, "#FFFFFF");
  assert.equal(manifest.tokens.find((token) => token.scope?.includes("prefers-color-scheme")).value, "#121212");
  assert.deepEqual(manifest.globalCssPaths, ["tokens/colors.css", "styles.css"]);
});

test("normalizes generated printer whitespace without changing literal contents", async (t) => {
  const root = await fixture(t, {
    "components/core/Widget.jsx": 'import React from "react";\nexport function Widget({first,\n// retained parameter comment\nsecond}={}) {return <div>{first}{second}</div>;}',
    "components/core/helper.js": 'const text = `retained trailing space \nnext line`; export const Helpers = { text };'
  });
  const outputs = await generateDesignSystem(root);
  assert.match(outputs["_ds_bundle.js"], /retained trailing space \nnext line/);
  assert.doesNotMatch(outputs["_ds_bundle.js"], /first, +\n/);
  const window = { React };
  vm.runInNewContext(outputs["_ds_bundle.js"], { window });
  assert.equal(window.TestDesignSystem.Helpers.text, "retained trailing space \nnext line");
});

test("fails on missing public exports, missing dependencies and invalid source syntax", async (t) => {
  const missing = await fixture(t, { "components/core/Widget.jsx": "export function Other() {}" });
  await assert.rejects(generateDesignSystem(missing), /Public export Widget is absent/);
  const dependency = await fixture(t, { "components/core/Widget.jsx": 'import {value} from "./missing"; export const Widget=value;' });
  await assert.rejects(generateDesignSystem(dependency), /Missing import/);
  const syntax = await fixture(t, { "components/core/Widget.jsx": "export function Widget( {" });
  await assert.rejects(generateDesignSystem(syntax), /expected|Unexpected/);
  const unpublished = await fixture(t, { "components/core/NewWidget.jsx": "export function NewWidget() {}" });
  await assert.rejects(generateDesignSystem(unpublished), /Unclassified public export NewWidget/);
});

test("real package preserves all published names and initializes without module errors", async () => {
  const outputs = await generateDesignSystem();
  const inventory = JSON.parse(await fs.readFile(path.join(defaultPackageRoot, "scripts/public-exports.json"), "utf8"));
  const manifest = JSON.parse(outputs["_ds_manifest.json"]);
  assert.equal(inventory.components.length, 160);
  assert.deepEqual(manifest.components, inventory.components);
  const window = { React };
  vm.runInNewContext(outputs["_ds_bundle.js"], { window, console, setTimeout, clearTimeout, setInterval, clearInterval });
  const namespace = window[inventory.namespace];
  for (const component of inventory.components) assert.notEqual(namespace[component.name], undefined, component.name);
  assert.equal(namespace.__errors.length, 0);
  assert.ok(manifest.templates.length >= 26);
});
