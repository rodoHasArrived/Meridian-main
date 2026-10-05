import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { createInterface } from "node:readline";

const workerPath = fileURLToPath(import.meta.url);
const delay = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

/** Keep an owned group/tree leader alive even if the actual command exits first. */
export function startOwnedProcess({ label, command, args = [], cwd, env = process.env }) {
  const worker = spawn(process.execPath, [workerPath, "--worker"], {
    cwd, env, detached: true, windowsHide: true,
    stdio: ["ignore", "pipe", "pipe", "ipc"]
  });
  for (const [stream, destination] of [[worker.stdout, process.stdout], [worker.stderr, process.stderr]]) {
    createInterface({ input: stream }).on("line", (line) => destination.write(`[${label}] ${line}\n`));
  }
  let finish;
  let settled = false;
  const result = new Promise((resolve) => { finish = (value) => { settled = true; resolve(value); }; });
  const exited = new Promise((resolve) => worker.once("exit", resolve));
  worker.once("error", (error) => finish({ code: null, error: error.message }));
  worker.on("message", (message) => {
    if (message.type === "result" && !settled) finish(message.result);
  });
  worker.once("exit", (code, signal) => {
    if (!settled) finish({ code, signal, error: "Process supervisor exited unexpectedly" });
  });
  worker.once("spawn", () => worker.send({ command, args }));
  let stopping;
  return {
    pid: worker.pid,
    result,
    stop() {
      stopping ??= (async () => {
        if (!worker.pid) return;
        if (process.platform === "win32") {
          if (worker.exitCode !== null || worker.signalCode !== null) {
            throw new Error(`Ownership supervisor ${worker.pid} exited before its process tree could be stopped.`);
          }
          // The anchor is still alive, so /T includes descendants even after a command failure.
          await killWindowsTree(worker.pid);
        } else {
          signalGroup(worker.pid, "SIGINT");
          // The command can exit before its descendants finish draining. Give the whole
          // group the grace period, not just its immediate child.
          await delay(5_000);
          signalGroup(worker.pid, "SIGKILL");
        }
        await exited;
      })();
      return stopping;
    }
  };
}

function signalGroup(pid, signal) {
  try { process.kill(-pid, signal); } catch (error) { if (error.code !== "ESRCH") throw error; }
}

function killWindowsTree(pid) {
  return new Promise((resolve, reject) => {
    const killer = spawn("taskkill.exe", ["/pid", String(pid), "/T", "/F"], { windowsHide: true, stdio: "ignore" });
    killer.once("error", reject);
    killer.once("exit", (code) => code === 0 ? resolve() : reject(new Error(`taskkill failed for owned process ${pid} (${code})`)));
  });
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1] && process.argv[2] === "--worker") {
  // Keep the group leader alive until its parent has cleaned up the complete tree.
  process.on("SIGINT", () => {});
  process.on("SIGTERM", () => {});
  setInterval(() => {}, 60_000); // Keep the ownership anchor alive after the command exits.
  let disconnected = false;
  process.once("message", ({ command, args }) => {
    if (disconnected) return;
    const child = spawn(command, args, { stdio: ["ignore", "inherit", "inherit"], windowsHide: true });
    let reported = false;
    const report = (result) => {
      if (!reported && process.connected) {
        reported = true;
        process.send({ type: "result", result });
      }
    };
    child.once("error", (error) => report({ code: null, error: error.message }));
    child.once("exit", (code, signal) => report({ code, signal }));
  });
  process.once("disconnect", () => {
    disconnected = true;
    if (process.platform === "win32") {
      void killWindowsTree(process.pid).catch(() => process.exit(1));
    } else {
      signalGroup(process.pid, "SIGINT");
      setTimeout(() => signalGroup(process.pid, "SIGKILL"), 5_000);
    }
  });
}
