#!/usr/bin/env node
import { spawn } from "node:child_process";
import { readdir } from "node:fs/promises";
import path from "node:path";
import process from "node:process";

const root = process.cwd();
const sourceRoot = path.join(root, "src");
const vitestEntry = path.join(root, "node_modules", "vitest", "vitest.mjs");
const args = process.argv.slice(2);
const defaultBatchSize = 8;
const requestedBatchSize = Number.parseInt(process.env.DASHBOARD_VITEST_BATCH_SIZE ?? "", 10);
const batchSize = Number.isFinite(requestedBatchSize) && requestedBatchSize > 0
  ? requestedBatchSize
  : defaultBatchSize;

const { passthroughArgs, shard } = parseRunnerArgs(args);

if (hasExplicitFileFilter(args)) {
  process.exit(await runVitest(["run", ...args]));
}

const allFiles = await findTestFiles(sourceRoot);
const selectedFiles = shard ? applyShard(allFiles, shard.index, shard.total) : allFiles;

if (selectedFiles.length === 0) {
  console.log("[dashboard-test-runner] No dashboard test files matched.");
  process.exit(0);
}

const batches = chunk(selectedFiles, batchSize);
console.log(
  `[dashboard-test-runner] Running ${selectedFiles.length} test files in ${batches.length} batch(es).`
);

if (shard) {
  console.log(`[dashboard-test-runner] Stable shard ${shard.index}/${shard.total} selected.`);
}

for (const [index, batch] of batches.entries()) {
  const files = batch.map((file) => toPosixPath(path.relative(root, file)));
  console.log(
    `\n[dashboard-test-runner] Batch ${index + 1}/${batches.length}: ${files.join(", ")}`
  );

  const code = await runVitest(["run", ...passthroughArgs, ...files]);
  if (code !== 0) {
    process.exit(code);
  }
}

function parseRunnerArgs(rawArgs) {
  const passthroughArgs = [];
  let shard;

  for (let index = 0; index < rawArgs.length; index += 1) {
    const arg = rawArgs[index];

    if (arg === "--shard") {
      const value = rawArgs[index + 1];
      if (!value) {
        throw new Error("--shard requires a value like 1/4.");
      }

      shard = parseShard(value);
      index += 1;
      continue;
    }

    if (arg.startsWith("--shard=")) {
      shard = parseShard(arg.slice("--shard=".length));
      continue;
    }

    passthroughArgs.push(arg);
  }

  return { passthroughArgs, shard };
}

function parseShard(value) {
  const match = /^(\d+)\/(\d+)$/.exec(value);
  if (!match) {
    throw new Error(`Invalid shard value "${value}". Expected a value like 1/4.`);
  }

  const index = Number.parseInt(match[1], 10);
  const total = Number.parseInt(match[2], 10);
  if (index < 1 || total < 1 || index > total) {
    throw new Error(`Invalid shard value "${value}". Shard index must be between 1 and total.`);
  }

  return { index, total };
}

function hasExplicitFileFilter(rawArgs) {
  for (let index = 0; index < rawArgs.length; index += 1) {
    const arg = rawArgs[index];

    if (arg === "--shard") {
      index += 1;
      continue;
    }

    if (!arg.startsWith("-")) {
      return true;
    }
  }

  return false;
}

async function findTestFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await findTestFiles(fullPath));
      continue;
    }

    if (/\.(test|spec)\.(ts|tsx)$/.test(entry.name)) {
      files.push(fullPath);
    }
  }

  return files.sort((left, right) => toPosixPath(left).localeCompare(toPosixPath(right)));
}

function applyShard(files, index, total) {
  return files.filter((_, fileIndex) => fileIndex % total === index - 1);
}

function chunk(files, size) {
  const batches = [];
  for (let index = 0; index < files.length; index += size) {
    batches.push(files.slice(index, index + size));
  }

  return batches;
}

function toPosixPath(value) {
  return value.replaceAll(path.sep, "/");
}

function runVitest(vitestArgs) {
  return new Promise((resolve) => {
    const child = spawn(process.execPath, [vitestEntry, ...vitestArgs], {
      cwd: root,
      env: {
        ...process.env,
        NODE_OPTIONS: appendNodeOption(process.env.NODE_OPTIONS, "--max-old-space-size=4096")
      },
      shell: false,
      stdio: "inherit",
      windowsHide: true
    });

    child.on("exit", (code, signal) => {
      if (signal) {
        console.error(`[dashboard-test-runner] Vitest exited from signal ${signal}.`);
        resolve(1);
        return;
      }

      resolve(code ?? 1);
    });

    child.on("error", (error) => {
      console.error(`[dashboard-test-runner] Failed to start Vitest: ${error.message}`);
      resolve(1);
    });
  });
}

function appendNodeOption(existing, option) {
  if (!existing) {
    return option;
  }

  if (existing.includes(option)) {
    return existing;
  }

  return `${existing} ${option}`;
}
