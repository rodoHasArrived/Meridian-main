import assert from "node:assert/strict";
import test from "node:test";
import { generateContracts } from "./generate-contracts.mjs";

test("contracts are generated from the repository root with an explicit interpreter", () => {
  const calls = [];
  generateContracts({
    root: "/a project",
    python: "/a Python executable",
    run: (...args) => { calls.push(args); return { status: 0 }; },
  });
  assert.deepEqual(calls.map(([command, args]) => [command, ...args]), [
    ["/a Python executable", "build/scripts/generate-ui-api-routes-ts.py"],
    ["/a Python executable", "build/scripts/generate-workspace-catalog-ts.py"],
  ]);
  assert.ok(calls.every(([, , options]) => options.cwd === "/a project"));
});

test("a missing default interpreter falls back once and retains the working interpreter", () => {
  const commands = [];
  generateContracts({
    python: "",
    platform: "linux",
    run: (command) => {
      commands.push(command);
      return command === "python3" ? { error: { code: "ENOENT" } } : { status: 0 };
    },
  });
  assert.deepEqual(commands, ["python3", "python", "python"]);
});

test("a generator failure stops before the other contract is generated", () => {
  let calls = 0;
  assert.throws(() => generateContracts({
    python: "python3",
    run: () => { calls += 1; return { status: 7 }; },
  }), /generate-ui-api-routes-ts.py failed \(7\)/);
  assert.equal(calls, 1);
});

test("an explicitly configured missing interpreter fails with actionable guidance", () => {
  let calls = 0;
  assert.throws(() => generateContracts({
    python: "/missing-python",
    run: () => { calls += 1; return { error: { code: "ENOENT" } }; },
  }), /set MERIDIAN_PYTHON/);
  assert.equal(calls, 1);
});
