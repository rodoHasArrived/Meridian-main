import { readdir } from "node:fs/promises";
import path from "node:path";

// The one definition of "a dashboard test file" and of how files are dealt to shards.
// The runner selects with it and the shard verdict checks completeness against it, so the
// two cannot disagree about which files a complete run contains.
export async function findTestFiles(directory) {
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

export function applyShard(files, index, total) {
  return files.filter((_, fileIndex) => fileIndex % total === index - 1);
}

export function toPosixPath(value) {
  return value.replaceAll(path.sep, "/");
}
