import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { checkProjectDependencies, createCheckPlan, runVerification } from "./check-design-system.mjs";

function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "meridian-check-deps-"));
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const manifest = { name: "test-project", version: "1.0.0", dependencies: { "@scope/tool": "^2.0.0" } };
  const lock = { lockfileVersion: 3, packages: { "": manifest, "node_modules/@scope/tool": { version: "2.1.0" } } };
  const write = (file, data) => {
    const target = path.join(root, file);
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, JSON.stringify(data));
  };
  write("package.json", manifest);
  write("package-lock.json", lock);
  write("node_modules/@scope/tool/package.json", { name: "@scope/tool", version: "2.1.0" });
  return { root, manifest, lock, write };
}

test("preflight accepts the installed lockfile version, including npm linked packages", (t) => {
  const project = fixture(t);
  assert.deepEqual(checkProjectDependencies(project.root), []);
  project.lock.packages["node_modules/@scope/tool"] = { link: true, resolved: "../local-tool" };
  project.lock.packages["../local-tool"] = { version: "2.1.0" };
  project.write("package-lock.json", project.lock);
  assert.deepEqual(checkProjectDependencies(project.root), []);
});

test("preflight rejects stale installed tools and unsynchronized manifest specifications", (t) => {
  const project = fixture(t);
  project.write("node_modules/@scope/tool/package.json", { version: "2.0.0" });
  assert.match(checkProjectDependencies(project.root).join("\n"), /installed 2\.0\.0, lockfile requires 2\.1\.0/);
  project.write("package.json", { ...project.manifest, dependencies: { "@scope/tool": "^3.0.0" } });
  assert.match(checkProjectDependencies(project.root).join("\n"), /dependencies differs from its lockfile/);
});

test("preflight fails closed for missing installs or malformed lockfiles", (t) => {
  const project = fixture(t);
  fs.rmSync(path.join(project.root, "node_modules"), { recursive: true });
  assert.match(checkProjectDependencies(project.root).join("\n"), /missing or unreadable installed package/);
  fs.writeFileSync(path.join(project.root, "package-lock.json"), "{");
  assert.notEqual(checkProjectDependencies(project.root).length, 0);
});

test("the verification plan uses separate arguments, read-only package gates and bounded source tests", () => {
  const plan = createCheckPlan({ dashboardRoot: path.resolve("/a project/src/Meridian.Ui/dashboard"), python: "/tools/Python executable" });
  const freshness = plan.stages.find((stage) => stage.name === "Generated package freshness");
  assert.deepEqual(freshness.args, ["scripts/build_package.mjs", "--check"]);
  const integrity = plan.stages.find((stage) => stage.name === "Export integrity");
  assert.equal(integrity.executable, "/tools/Python executable");
  assert.deepEqual(integrity.args, ["scripts/export_release.py", "--check"]);
  const contrast = plan.stages.find((stage) => stage.name === "Primary action contrast");
  assert.deepEqual(contrast.args, ["scripts/check_contrast.py", "--actions-only"]);
  const source = plan.stages.find((stage) => stage.name === "Focused workstation contracts");
  assert.ok(source.args.includes("src/vite-config.font-licenses.test.ts"));
  assert.ok(source.args.includes("src/design-system/design-system-ratchets.test.ts"));
  const tooling = plan.stages.find((stage) => stage.args.includes("scripts/design-system-smoke.test.mjs"));
  assert.equal(tooling.args[0], "--test");
  for (const stage of plan.stages) {
    assert.ok(path.isAbsolute(stage.cwd));
    assert.equal(stage.args.some((arg) => ["--write", "--update-baseline", "install", "ci"].includes(arg)), false);
    assert.equal(stage.args.some((arg) => /(?:smoke|release-browser|gallery-browser).*\.mjs$/.test(arg) && !arg.endsWith(".test.mjs")), false);
    assert.equal(stage.args.some((arg) => ["--design-system", "--expanded"].includes(arg)), false);
  }
});

function executionFixture() {
  const calls = [], errors = [], messages = [];
  return {
    calls, errors, messages,
    plan: { projects: ["dashboard", "package"], stages: ["first", "second", "third"].map((name) => ({ name, executable: "node", args: [name], cwd: name })) },
    logger: { log: (message) => messages.push(message), error: (message) => errors.push(message) },
    preflight: () => [],
    execute: (executable, args, options) => { calls.push({ executable, args, options }); return { status: 0 }; }
  };
}

test("failed preflight runs no commands or installers", () => {
  const fixture = executionFixture();
  fixture.preflight = (project) => project === "package" ? ["tool differs from lockfile"] : [];
  assert.equal(runVerification(fixture), 1);
  assert.equal(fixture.calls.length, 0);
  assert.match(fixture.errors.join(), /Dependency preflight failed in package/);
});

test("verification runs in order with a shell disabled and stops at the failed stage", () => {
  const fixture = executionFixture();
  fixture.execute = (executable, args, options) => {
    fixture.calls.push({ executable, args, options });
    return { status: args[0] === "second" ? 7 : 0 };
  };
  assert.equal(runVerification(fixture), 7);
  assert.deepEqual(fixture.calls.map((call) => call.args[0]), ["first", "second"]);
  assert.equal(fixture.calls.every((call) => call.options.shell === false), true);
  assert.match(fixture.errors.join(), /second failed: exit 7/);
});

test("unavailable executables and signaled children cannot produce a passing result", () => {
  for (const result of [{ status: null, error: new Error("ENOENT") }, { status: null, signal: "SIGTERM" }]) {
    const fixture = executionFixture();
    fixture.execute = () => result;
    assert.equal(runVerification(fixture), 1);
    assert.equal(fixture.messages.some((message) => message.includes("checks passed")), false);
  }
});

test("successful verification preserves stage order and reports browser verification separately", () => {
  const fixture = executionFixture();
  assert.equal(runVerification(fixture), 0);
  assert.deepEqual(fixture.calls.map((call) => call.args[0]), ["first", "second", "third"]);
  assert.match(fixture.messages.at(-1), /Browser validation remains a separate/);
});


test("root forwarders preserve the npm argument separator for both public commands", () => {
  const root = JSON.parse(fs.readFileSync(new URL("../../../../package.json", import.meta.url), "utf8"));
  for (const command of ["check:design-system", "check:design-system:browser"]) {
    assert.equal(root.scripts[command], `npm --prefix src/Meridian.Ui/dashboard run ${command} --`);
  }
});

test("the fast check rejects browser or other unexpected CLI flags before preflight", () => {
  const result = spawnSync(process.execPath, [fileURLToPath(new URL("./check-design-system.mjs", import.meta.url)), "--url", "http://example.invalid"], { encoding: "utf8" });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /Usage: node scripts\/check-design-system.mjs/);
  assert.equal(result.stdout, "");
});
