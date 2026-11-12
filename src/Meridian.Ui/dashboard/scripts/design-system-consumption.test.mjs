import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import ts from "typescript";
import { analyzeReachability } from "./design-system-consumption.mjs";

function fixture(files, action) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "meridian-consumption-"));
  try {
    for (const [name, source] of Object.entries(files)) {
      const file = path.join(root, name); fs.mkdirSync(path.dirname(file), { recursive: true }); fs.writeFileSync(file, source);
    }
    const program = ts.createProgram(Object.keys(files).map((name) => path.join(root, name)), {
      target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext, moduleResolution: ts.ModuleResolutionKind.Bundler,
      jsx: ts.JsxEmit.Preserve, lib: ["lib.es2022.d.ts"]
    });
    action(analyzeReachability(program, path.join(root, "main.tsx"), root));
  } finally { fs.rmSync(root, { recursive: true, force: true }); }
}

test("barrel exports and component metadata do not imply route integration", () => {
  fixture({
    "main.tsx": 'import { Used } from "./components/barrel"; const view = <Used />; const names = ["Available", "NestedUnused"];',
    "components/barrel.ts": 'export { Used, Available } from "./parts";',
    "components/parts.tsx": 'export function Used() { return <div />; } export function Available() { return <NestedUnused />; } export function NestedUnused() { return <div />; }'
  }, (graph) => {
    assert.equal(graph.candidates("Used")[0].reachable, true);
    assert.equal(graph.candidates("Available")[0].reachable, false);
    assert.equal(graph.candidates("NestedUnused")[0].reachable, false);
    assert.ok(graph.candidates("Used")[0].consumers.some((use) => use.source.endsWith("main.tsx")));
  });
});

test("lazy route exports and their rendered descendants are reachable", () => {
  fixture({
    "main.tsx": 'const Screen = lazy(() => import("./screen").then((module) => ({ default: module.Screen }))); const view = <Screen />;',
    "screen.tsx": 'import { Button } from "./components/button"; export function Screen() { return <Button />; } export function UnusedScreen() { return <Other />; }',
    "components/button.tsx": 'export function Button() { return <button />; } export function Other() { return <div />; }'
  }, (graph) => {
    assert.equal(graph.candidates("Button")[0].reachable, true);
    assert.equal(graph.candidates("Other")[0].reachable, false);
  });
});

test("test-only consumers never promote an adapter to integrated", () => {
  fixture({
    "main.tsx": 'const view = <div />;',
    "components/adapter.tsx": 'export function Adapter() { return <div />; }',
    "components/adapter.test.tsx": 'import { Adapter } from "./adapter"; const testView = <Adapter />;'
  }, (graph) => assert.equal(graph.candidates("Adapter")[0].reachable, false));
});
