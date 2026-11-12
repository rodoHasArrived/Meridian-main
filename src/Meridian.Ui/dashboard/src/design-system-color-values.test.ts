// @vitest-environment node
import { readFileSync, readdirSync } from "node:fs";
import { join, relative } from "node:path";
import { expect, it } from "vitest";

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name);
    return entry.isDirectory() ? sourceFiles(path)
      : /\.(?:css|tsx?)$/.test(entry.name) && !entry.name.includes(".test.") ? [path] : [];
  });
}

function rawChannelUses(source: string, channelNames: Set<string>) {
  return [...source.matchAll(/var\(\s*--([\w-]+)(?=\s*[,)])/g)].filter((match) => {
    if (!channelNames.has(match[1])) return false;
    const prefix = source.slice(Math.max(0, match.index - 160), match.index);
    if (/hsla?\(\s*$/.test(prefix)) return false;
    const assignment = prefix.match(/--([\w-]+):\s*$/);
    return !assignment || !channelNames.has(assignment[1]);
  });
}

it("allows channel aliases while rejecting raw channel colors", () => {
  const names = new Set(["border", "muted-foreground", "selected-muted-foreground"]);
  expect(rawChannelUses("--muted-foreground: var(--selected-muted-foreground); color: hsl(var(--muted-foreground));", names)).toHaveLength(0);
  expect(rawChannelUses("border: 1px solid var(--border); --complete-color: var(--border);", names)).toHaveLength(2);
});

it("wraps channel-only HSL tokens before using them as CSS colors", () => {
  const src = join(process.cwd(), "src");
  const tokens = readFileSync(join(src, "styles/index.css"), "utf8");
  // Track A stores channels for Tailwind opacity, rather than complete colors.
  const channelNames = new Set([...tokens.matchAll(/--([\w-]+):\s*\d+(?:\.\d+)?\s+\d+(?:\.\d+)?%\s+\d+(?:\.\d+)?%\s*;/g)].map((match) => match[1]));
  expect(channelNames.has("border")).toBe(true);
  const invalid: Array<{ file: string; line: number; token: string }> = [];
  for (const file of sourceFiles(src)) {
    const source = readFileSync(file, "utf8");
    for (const match of rawChannelUses(source, channelNames)) {
      invalid.push({ file: relative(src, file), line: source.slice(0, match.index).split("\n").length, token: match[1] });
    }
  }
  expect(invalid, "Use hsl(var(--role)) or the corresponding complete-color workstation alias").toEqual([]);
});
