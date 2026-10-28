import { execFile } from "node:child_process";
import { access, readFile } from "node:fs/promises";
import net from "node:net";
import path from "node:path";
import { promisify } from "node:util";

const execute = promisify(execFile);
const installDependencies = "npm --prefix src/Meridian.Ui/dashboard ci --include=optional";

export async function assertPortAvailable(port, { label = "port" } = {}) {
  for (const host of ["127.0.0.1", "::1"]) {
    await new Promise((resolve, reject) => {
      const server = net.createServer();
      server.once("error", (error) => {
        if (host === "::1" && ["EAFNOSUPPORT", "EADDRNOTAVAIL"].includes(error.code)) return resolve();
        reject(new Error(`${label} ${host}:${port} is unavailable (${error.code}). Choose another port; no existing process was stopped.`));
      });
      server.listen({ host, port, exclusive: true }, () => server.close(resolve));
    });
  }
}

export async function assertDashboardDependencies(dashboard) {
  await access(path.join(dashboard, "node_modules/vite/bin/vite.js")).catch(() => {
    throw new Error(`Install dashboard dependencies first: ${installDependencies}. Run npm run dev:doctor for setup diagnostics.`);
  });
}

function versionParts(version) {
  const match = /^(\d+)\.(\d+)\.(\d+)(?:-[\w.-]+)?$/.exec(version);
  if (!match) throw new Error(`Unrecognized version: ${version}`);
  return match.slice(1).map(Number);
}

function compareVersions(left, right) {
  for (let index = 0; index < 3; index++) {
    if (left[index] !== right[index]) return Math.sign(left[index] - right[index]);
  }
  return 0;
}

// Runtime manifests use explicit caret/minimum alternatives. Fail closed if
// a future lockfile adopts a different grammar instead of silently accepting it.
export function supportsNode(version, range) {
  const current = versionParts(version);
  // npm engine ranges without explicit prerelease comparators exclude previews.
  if (version.includes("-")) return false;
  const alternatives = range.split("||").map((entry) => entry.trim());
  const constraints = alternatives.map((entry) => {
    const match = /^(\^|>=)(\d+\.\d+\.\d+)$/.exec(entry);
    if (!match) throw new Error(`Unsupported Node engine range: ${range}. Update the doctor range parser.`);
    return { operator: match[1], minimum: versionParts(match[2]) };
  });
  return constraints.some(({ operator, minimum }) => compareVersions(current, minimum) >= 0 &&
    (operator === ">=" || (minimum[0] > 0 && current[0] === minimum[0])));
}

export async function diagnoseWeb(options, {
  repoRoot,
  nodeVersion = process.versions.node,
  runCommand = execute,
  runVite = execute,
  checkPort = assertPortAvailable,
} = {}) {
  const dashboard = path.join(repoRoot, "src/Meridian.Ui/dashboard");
  const checks = [];
  const check = async (id, inspect, remedy) => {
    try { checks.push({ id, status: "pass", detail: await inspect() }); }
    catch (error) { checks.push({ id, status: "fail", detail: error.message, remedy }); }
  };

  await check("node", async () => {
    const lock = JSON.parse(await readFile(path.join(dashboard, "package-lock.json"), "utf8"));
    const range = lock.packages?.["node_modules/vite"]?.engines?.node;
    if (!range) throw new Error("The dashboard lockfile does not declare Vite's Node requirement.");
    if (!supportsNode(nodeVersion, range)) throw new Error(`Node ${nodeVersion} does not satisfy locked Vite requirement ${range}.`);
    const manifest = JSON.parse(await readFile(path.join(dashboard, "package.json"), "utf8"));
    const runtimeRange = manifest.engines?.node;
    if (runtimeRange !== undefined && !supportsNode(nodeVersion, runtimeRange)) {
      throw new Error(`Node ${nodeVersion} does not satisfy dashboard runtime requirement ${runtimeRange}.`);
    }
    return `Node ${nodeVersion} satisfies locked Vite requirement ${range}` +
      (runtimeRange === undefined ? "." : ` and dashboard runtime requirement ${runtimeRange}.`);
  }, "Install Node.js 24 satisfying dashboard package.json engines.node; restore a valid dashboard manifest and lockfile if missing.");

  await check("dashboard-dependencies", async () => {
    await assertDashboardDependencies(dashboard);
    let result;
    try {
      result = await runVite(process.execPath, [path.join(dashboard, "node_modules/vite/bin/vite.js"), "--version"], {
        cwd: dashboard, timeout: 10_000, maxBuffer: 64 * 1024, windowsHide: true,
        // Vite enables Node's compile cache even for --version. Disable that
        // cache explicitly so diagnosis does not create files in the user profile.
        env: { ...process.env, NODE_DISABLE_COMPILE_CACHE: "1" },
      });
    } catch {
      throw new Error("Installed Vite could not load on this OS (--version failed or timed out); native/optional packages may be missing.");
    }
    const version = /^vite\/(\d+\.\d+\.\d+(?:-[\w.-]+)?)(?:\s|$)/m.exec(result.stdout);
    if (!version) throw new Error("Installed Vite returned an unrecognized --version response.");
    return `Vite ${version[1]} CLI loads on this OS (dependency freshness is not verified).`;
  }, `Run ${installDependencies} on this OS. For full environment restore, use buildctl environment --restore.`);

  if (options.mode === "backend-connected") {
    await check("dotnet-sdk", async () => {
      const config = JSON.parse(await readFile(path.join(repoRoot, "global.json"), "utf8"));
      const required = config.sdk?.version;
      if (!required) throw new Error("global.json does not declare an SDK version.");
      let result;
      try {
        result = await runCommand("dotnet", ["--version"], {
          cwd: repoRoot, timeout: 10_000, maxBuffer: 64 * 1024, windowsHide: true,
          env: { ...process.env, DOTNET_CLI_TELEMETRY_OPTOUT: "1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1" },
        });
      } catch {
        throw new Error(`No usable .NET SDK selected by global.json (${required}, rollForward=${config.sdk.rollForward ?? "patch"}).`);
      }
      // dotnet resolves the full SDK policy from this working directory. Check the
      // returned shape too, so a broken shim is not reported as a usable SDK.
      const selected = result.stdout.trim();
      const current = versionParts(selected);
      const minimum = versionParts(required);
      const allowMajor = ["major", "latestMajor"].includes(config.sdk.rollForward);
      if (compareVersions(current, minimum) < 0 || (!allowMajor && current[0] !== minimum[0])) {
        throw new Error(`Selected SDK ${selected} is incompatible with global.json (${required}).`);
      }
      return `.NET SDK ${selected} selected by global.json (${required}, rollForward=${config.sdk.rollForward ?? "patch"}).`;
    }, "Install the SDK required by global.json and run dotnet --version from the repository root, or use npm run dev:fixtures.");
  }

  await check("vite-port", async () => {
    await checkPort(options.port, { label: "Vite port" });
    return `Loopback port ${options.port} is available.`;
  }, "Stop the owning development session with Ctrl+C or choose --port <free-port>.");
  if (options.mode === "backend-connected") {
    await check("backend-port", async () => {
      await checkPort(options.backendPort, { label: "Backend port" });
      return `Loopback port ${options.backendPort} is available.`;
    }, "Stop the owning development session with Ctrl+C or choose --backend-port <free-port>.");
  }
  return { mode: options.mode, ok: checks.every(({ status }) => status === "pass"), checks };
}

export function formatDiagnosis(report) {
  return [
    `[doctor] ${report.mode}: ${report.ok ? "prerequisites available" : "setup needs attention"}`,
    ...report.checks.flatMap(({ id, status, detail, remedy }) => [
      `[${status === "pass" ? "PASS" : "FAIL"}] ${id}: ${detail}`,
      ...(remedy ? [`  Fix: ${remedy}`] : []),
    ]),
    "[doctor] No dependencies installed, data seeded, configuration written, or services started. Port availability is a point-in-time check.",
  ].join("\n");
}
