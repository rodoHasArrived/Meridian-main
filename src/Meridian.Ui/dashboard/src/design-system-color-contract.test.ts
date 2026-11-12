// @vitest-environment node

import { readFileSync, readdirSync } from "node:fs";
import { resolve } from "node:path";
import postcss, { type Node } from "postcss";
import { describe, expect, it } from "vitest";

const sourceRoot = resolve(process.cwd(), "src");
const stylesheet = postcss.parse(readFileSync(resolve(sourceRoot, "styles/index.css"), "utf8"));

function themeColors(theme: "light" | "dark" | null, systemDark: boolean) {
  const tokens = new Map<string, string>();
  stylesheet.walkRules((rule) => {
    if (![":root", ':root[data-theme="light"]', ':root[data-theme="dark"]'].includes(rule.selector)) return;
    if (rule.selector !== ":root" && rule.selector !== `:root[data-theme="${theme}"]`) return;
    let parent: Node["parent"] = rule.parent;
    while (parent) {
      if (parent.type === "atrule" && "params" in parent && typeof parent.params === "string" && parent.params.includes("prefers-color-scheme: dark") && !systemDark) return;
      parent = parent.parent;
    }
    rule.walkDecls(/^--/, (declaration) => { tokens.set(declaration.prop, declaration.value); });
  });

  function color(token: string, seen = new Set<string>()): string {
    if (seen.has(token)) throw new Error(`Cyclic color token: ${token}`);
    const value = tokens.get(token);
    if (!value) throw new Error(`Missing color token: ${token}`);
    return value.replace(/var\((--[\w-]+)\)/g, (_, dependency: string) => color(dependency, new Set([...seen, token])));
  }
  return color;
}

function componentSources(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = resolve(directory, entry.name);
    if (entry.isDirectory()) return componentSources(path);
    return /\.(tsx?|css)$/.test(entry.name) && !entry.name.includes(".test.") ? [path] : [];
  });
}

function contrast(foreground: string, background: string): number {
  const luminance = (hex: string) => {
    if (!/^#[0-9a-f]{6}$/i.test(hex)) throw new Error(`Expected a resolved hex color: ${hex}`);
    const channels = [1, 3, 5].map((offset) => {
      const channel = Number.parseInt(hex.slice(offset, offset + 2), 16) / 255;
      return channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
    });
    return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
  };
  const values = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return (values[0] + 0.05) / (values[1] + 0.05);
}

describe("native design-system color integration", () => {
  it.each(["light", "dark"] as const)("keeps dense table headers, alternating rows and selected evidence readable in %s appearance", (theme) => {
    const colors = themeColors(theme, theme === "dark");
    const styles = postcss.parse(readFileSync(resolve(sourceRoot, "styles/ui-kit-primitives.css"), "utf8"));
    const backgrounds = new Map<string, string>();
    styles.walkRules((rule) => {
      rule.walkDecls("background", (declaration) => { backgrounds.set(rule.selector, declaration.value); });
    });
    for (const [selector, foreground] of [
      [".dense-data-table thead", "--ws-text-muted"],
      [".dense-data-table tbody tr:nth-child(even)", "--ws-text"],
      [".dense-data-table tbody tr.selected", "--ws-text-secondary"]
    ]) {
      const background = backgrounds.get(selector);
      expect(background, selector).toBeDefined();
      const resolved = background!.replace(/var\((--[\w-]+)\)/g, (_, token: string) => colors(token));
      expect(contrast(colors(foreground), resolved), selector).toBeGreaterThanOrEqual(4.5);
    }
  });

  it.each([
    { theme: null, systemDark: false, dark: false },
    { theme: null, systemDark: true, dark: true },
    { theme: "dark" as const, systemDark: false, dark: true },
    { theme: "light" as const, systemDark: true, dark: false }
  ])("resolves chart and surface roles for $theme with system dark=$systemDark", ({ theme, systemDark, dark }) => {
    const color = themeColors(theme, systemDark);
    const expected = {
      "--chart-plot": dark ? "#201D19" : "#FBFAF8",
      "--chart-axis": dark ? "#9A9289" : "#5E666F",
      "--chart-border": dark ? "#736B60" : "#AFABA1",
      "--chart-equity": dark ? "#5FA37C" : "#3A7A56",
      "--chart-drawdown": dark ? "#CE7068" : "#A8443C",
      "--chart-primary": dark ? "#D98A64" : "#A85436",
      "--chart-warning": dark ? "#C29A4A" : "#8A5C12",
      "--card-surface": dark ? "#201D19" : "#FBFAF8",
      "--card-surface-raised": dark ? "#252119" : "#F6F4F0",
      "--text-on-fill": dark ? "hsl(26 20% 9%)" : "hsl(0 0% 100%)"
    };
    for (const [token, value] of Object.entries(expected)) expect(color(token), token).toBe(value);
  });

  it("keeps plain CSS colors separate from Tailwind's reserved HSL channel tokens", () => {
    const directories = ["accounting", "charts", "data", "operations"];
    const offenders = directories.flatMap((directory) =>
      componentSources(resolve(sourceRoot, "components", directory)).flatMap((path) => {
        const source = readFileSync(path, "utf8");
        // These adapters consume complete CSS colors. A fallback cannot repair
        // a defined token containing raw HSL channels: it invalidates the rule.
        return /var\(--(?:border|accent)\s*,/.test(source) ? [path] : [];
      })
    );
    expect(offenders).toEqual([]);
    expect(readFileSync(resolve(sourceRoot, "styles/ui-kit-primitives.css"), "utf8"))
      .not.toMatch(/var\(--accent\s*,/);
  });
});
