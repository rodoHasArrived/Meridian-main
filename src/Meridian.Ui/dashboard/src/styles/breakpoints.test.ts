import { readdirSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { describe, expect, it } from "vitest";

// The breakpoint tiers documented at the end of styles/index.css (UX Phase C).
const MAX_WIDTHS = new Set([640, 980, 1279, 1599]);
const MIN_WIDTHS = new Set([641, 981, 1280, 1600]);

function cssFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = join(dir, entry.name);
    return entry.isDirectory() ? cssFiles(full) : entry.name.endsWith(".css") ? [full] : [];
  });
}

describe("CSS breakpoints", () => {
  it("uses only the documented breakpoint tiers", () => {
    const root = resolve(process.cwd(), "src");
    const offenders: string[] = [];
    for (const file of cssFiles(root)) {
      const source = readFileSync(file, "utf8");
      for (const match of source.matchAll(/\((max|min)-width:\s*([0-9.]+)px\)/g)) {
        const width = Number(match[2]);
        const allowed = match[1] === "max" ? MAX_WIDTHS : MIN_WIDTHS;
        if (!allowed.has(width)) offenders.push(`${file.slice(root.length + 1)}: ${match[0]}`);
      }
    }
    expect(offenders).toEqual([]);
  });
});
