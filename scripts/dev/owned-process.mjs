import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { createInterface } from "node:readline";
import { randomUUID } from "node:crypto";

const workerPath = fileURLToPath(import.meta.url);
const windowsKeeperPath = fileURLToPath(new URL("./owned-process-windows.ps1", import.meta.url));
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
  const result = new Promise((resolve) => { finish = (value) => {
    if (!settled) { settled = true; resolve(value); }
  }; });
  const exited = new Promise((resolve) => worker.once("exit", resolve));
  let windowsJob;
  let commandSent = false;
  let stopRequested = false;
  let stopping;
  worker.once("error", (error) => finish({ code: null, error: error.message }));
  worker.on("message", (message) => {
    if (message.type === "result" && !settled) finish(message.result);
  });
  worker.once("exit", (code, signal) => {
    if (!settled) finish({ code, signal, error: "Process supervisor exited unexpectedly" });
    if (process.platform === "win32" && !stopRequested) void stop().catch(reportFailure);
  });
  const reportFailure = (error) => finish({ code: null, error: error.message });
  worker.once("spawn", () => {
    if (stopRequested) return;
    if (process.platform !== "win32") {
      worker.send({ command, args });
      return;
    }
    // The keeper stays outside the job. Assign the idle anchor before it can
    // launch a command, so even descendants with exited parents remain owned.
    windowsJob = createWindowsJob(worker, { cwd, env, onFailure: (error) => {
      reportFailure(error);
      void stop().catch(reportFailure);
    } });
    void windowsJob.ready.then(() => {
      if (stopRequested || !worker.connected || worker.exitCode !== null || worker.signalCode !== null) return;
      commandSent = true;
      worker.send({ command, args }, (error) => {
        if (error) { reportFailure(error); void stop().catch(reportFailure); }
      });
    }).catch((error) => {
      if (!stopRequested) { reportFailure(error); void stop().catch(reportFailure); }
    });
  });

  function stop() {
    stopRequested = true;
    stopping ??= (async () => {
      if (!worker.pid) return;
      if (process.platform === "win32") {
        let cleanupError;
        try { await windowsJob?.close(); } catch (error) { cleanupError = error; }
        // A helper startup failure can leave an unassigned, idle anchor. Its
        // ChildProcess handle identifies exactly the process we created.
        if (!commandSent && worker.exitCode === null && worker.signalCode === null) worker.kill("SIGKILL");
        await withTimeout(exited, 5_000, "Windows ownership supervisor did not stop after closing its job.");
        if (cleanupError) throw cleanupError;
      } else {
        signalGroup(worker.pid, "SIGINT");
        // The command can exit before its descendants finish draining. Give the whole
        // group the grace period, not just its immediate child.
        await delay(5_000);
        signalGroup(worker.pid, "SIGKILL");
        await exited;
      }
    })();
    return stopping;
  }

  return { pid: worker.pid, result, stop };
}

function createWindowsJob(worker, { cwd, env, onFailure }) {
  const keeper = spawn("powershell.exe", ["-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
    "-File", windowsKeeperPath, "-OwnedPid", String(worker.pid)], {
    cwd, env, detached: true, windowsHide: true, stdio: ["pipe", "pipe", "pipe"]
  });
  let resolveReady;
  let rejectReady;
  let readySettled = false;
  let expectedExit = false;
  let keeperExited = false;
  let failure;
  let closing;
  let diagnostics = "";
  let probeSent = false;
  let assignmentRequested = false;
  const nonce = randomUUID();
  const ready = new Promise((resolve, reject) => { resolveReady = resolve; rejectReady = reject; });
  const settleReady = (error) => {
    if (readySettled) return;
    readySettled = true;
    clearTimeout(startupTimer);
    worker.off("message", confirmIdentity);
    if (error) rejectReady(error); else resolveReady();
  };
  const fail = (error) => {
    failure ??= error;
    settleReady(error);
    onFailure(error);
  };
  const startupTimer = setTimeout(() => fail(new Error("Timed out creating the Windows ownership job.")), 15_000);
  const confirmIdentity = (message) => {
    if (expectedExit || !probeSent || assignmentRequested || message.type !== "ownership-probe" || message.nonce !== nonce) return;
    assignmentRequested = true;
    keeper.stdin.write("ASSIGN\n");
  };
  worker.on("message", confirmIdentity);
  keeper.stderr.on("data", (chunk) => { diagnostics = (diagnostics + chunk).slice(-4_096); });
  createInterface({ input: keeper.stdout }).on("line", (line) => {
    if (expectedExit) return;
    if (line.trim() === "OPENED" && !probeSent) {
      // A numeric PID can be reused before PowerShell starts. The keeper first
      // pins that process object, then we authenticate our original anchor over
      // its private IPC channel before allowing assignment through that handle.
      probeSent = true;
      worker.send({ type: "ownership-probe", nonce }, (error) => {
        if (error && !expectedExit) fail(error);
      });
    } else if (line.trim() === "READY" && assignmentRequested) {
      settleReady();
    }
  });
  keeper.stdin.on("error", (error) => {
    if (!expectedExit) fail(new Error(`Windows ownership helper input failed: ${error.message}`));
  });
  const exited = new Promise((resolve) => {
    keeper.once("error", (error) => {
      keeperExited = true;
      resolve();
      fail(new Error(`Cannot start Windows PowerShell ownership helper: ${error.message}`));
    });
    keeper.once("exit", (code, signal) => {
      keeperExited = true;
      resolve();
      if (!expectedExit || (code !== 0 && signal === null)) {
        fail(new Error(`Windows ownership helper exited (${signal ?? code}): ${diagnostics.trim()}`));
      } else {
        settleReady(new Error("Windows ownership startup was cancelled."));
      }
    });
  });
  return {
    ready,
    close() {
      closing ??= (async () => {
        expectedExit = true;
        clearTimeout(startupTimer);
        // EOF closes the keeper's private, non-inheritable job handle. Parent
        // death produces the same EOF, without PID-based process-tree discovery.
        keeper.stdin.end();
        try {
          await withTimeout(exited, 5_000, "Windows ownership helper did not close its job.");
        } catch {
          if (!keeperExited) keeper.kill("SIGKILL");
          await withTimeout(exited, 5_000, "Windows ownership helper could not be stopped.");
        }
        if (failure) throw failure;
      })();
      return closing;
    }
  };
}

async function withTimeout(promise, timeoutMs, message) {
  let timer;
  try {
    return await Promise.race([promise, new Promise((_, reject) => {
      timer = setTimeout(() => reject(new Error(message)), timeoutMs);
    })]);
  } finally { clearTimeout(timer); }
}

function signalGroup(pid, signal) {
  try { process.kill(-pid, signal); } catch (error) { if (error.code !== "ESRCH") throw error; }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1] && process.argv[2] === "--worker") {
  // Keep the group leader alive until its parent has cleaned up the complete tree.
  process.on("SIGINT", () => {});
  process.on("SIGTERM", () => {});
  setInterval(() => {}, 60_000); // Keep the ownership anchor alive after the command exits.
  let disconnected = false;
  let commandStarted = false;
  process.on("message", ({ type, nonce, command, args }) => {
    if (disconnected) return;
    if (type === "ownership-probe" && !commandStarted) {
      process.send({ type, nonce });
      return;
    }
    if (commandStarted || !command) return;
    commandStarted = true;
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
      // The separate keeper receives EOF too and closes the job for every child.
      process.exit(1);
    } else {
      signalGroup(process.pid, "SIGINT");
      setTimeout(() => signalGroup(process.pid, "SIGKILL"), 5_000);
    }
  });
}
