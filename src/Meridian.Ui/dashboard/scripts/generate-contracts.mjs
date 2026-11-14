import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const repositoryRoot = fileURLToPath(new URL("../../../../", import.meta.url));
const generators = [
  "build/scripts/generate-ui-api-routes-ts.py",
  "build/scripts/generate-workspace-catalog-ts.py",
];

export function generateContracts({
  root = repositoryRoot,
  python = process.env.MERIDIAN_PYTHON,
  platform = process.platform,
  run = spawnSync,
} = {}) {
  const candidates = python ? [python] : platform === "win32" ? ["python", "python3"] : ["python3", "python"];
  let interpreter;
  for (const candidate of candidates) {
    const result = run(candidate, [generators[0]], { cwd: root, stdio: "inherit" });
    if (result.error?.code === "ENOENT") continue;
    if (result.error) throw result.error;
    if (result.status !== 0) throw new Error(`${generators[0]} failed (${result.status ?? result.signal}).`);
    interpreter = candidate;
    break;
  }
  if (!interpreter) throw new Error("Python 3 is required to generate browser contracts. Install it or set MERIDIAN_PYTHON.");
  for (const generator of generators.slice(1)) {
    const result = run(interpreter, [generator], { cwd: root, stdio: "inherit" });
    if (result.error) throw result.error;
    if (result.status !== 0) throw new Error(`${generator} failed (${result.status ?? result.signal}).`);
  }
}

if (process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url) {
  try {
    generateContracts();
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
