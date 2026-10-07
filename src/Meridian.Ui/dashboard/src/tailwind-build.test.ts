// @vitest-environment node

import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { resolve } from "node:path";
import postcss, { type AcceptedPlugin, type Result, type Rule } from "postcss";
import { beforeAll, describe, expect, it } from "vitest";

const require = createRequire(import.meta.url);

describe("workstation stylesheet compilation", () => {
  let stylesheet: Result;

  beforeAll(async () => {
    // Use the real entry point and configured PostCSS pipeline: component tests
    // alone cannot detect a dependency upgrade that stops emitting usable CSS.
    const config = require("../postcss.config.cjs") as {
      plugins: Record<string, Record<string, unknown>>;
    };
    const plugins = Object.entries(config.plugins).map(([name, options]) => {
      const plugin = require(name) as (options: Record<string, unknown>) => AcceptedPlugin;
      return plugin(options);
    });
    const entry = resolve(process.cwd(), "src/styles/index.css");
    stylesheet = await postcss(plugins).process(readFileSync(entry, "utf8"), { from: entry });
  }, 30_000);

  function rulesFor(selector: string): Rule[] {
    const rules: Rule[] = [];
    stylesheet.root.walkRules((rule) => {
      if (rule.selectors.includes(selector)) rules.push(rule);
    });
    expect(rules, `Missing compiled workstation selector: ${selector}`).not.toHaveLength(0);
    return rules;
  }

  function valuesFor(selector: string, property: string): string[] {
    return rulesFor(selector).flatMap((rule) => {
      const values: string[] = [];
      rule.walkDecls(property, (declaration) => { values.push(declaration.value); });
      return values;
    });
  }

  it("compiles utilities and component @apply rules with Meridian theme tokens", () => {
    expect(stylesheet.warnings()).toEqual([]);
    expect(stylesheet.css).not.toMatch(/@(tailwind|apply|config|source)\b/);
    expect(valuesFor(".bg-card", "background-color")).toContain("hsl(var(--card) / 1)");
    expect(valuesFor(".rounded-xl", "border-radius")).toContain("var(--radius-xl)");
    expect(valuesFor(".panel-surface", "border-width")).toContain("1px");
    expect(valuesFor(".panel-surface", "background-color")).toContain("hsl(var(--card) / 1)");
    expect(valuesFor(".panel-surface", "--tw-shadow")).toContain("var(--shadow-workstation)");
    expect(valuesFor(".bg-primary\\/10", "background-color")).toContain(
      "color-mix(in oklab, hsl(var(--primary) / 1) 10%, transparent)"
    );
  });

  it("keeps workstation font stacks and responsive source utilities", () => {
    expect(valuesFor(".font-sans", "font-family").join()).toContain("Segoe UI Variable Text");
    expect(valuesFor(".font-display", "font-family").join()).toContain("Segoe UI Variable Display");
    expect(valuesFor(".font-mono", "font-family").join()).toContain("Cascadia Mono");
    const responsiveRule = rulesFor(".md\\:grid-cols-2")[0];
    expect(responsiveRule.parent?.type).toBe("atrule");
    expect(responsiveRule.parent?.toString()).toContain("(width >= 48rem)");
    expect(valuesFor(".shrink-0", "flex-shrink")).toContain("0");
  });

  it("preserves migrated subtle elevation, blur and forced-colors focus affordances", () => {
    expect(valuesFor(".shadow-xs", "--tw-shadow").join()).toContain("0 1px 2px 0");
    expect(valuesFor(".backdrop-blur-sm", "--tw-backdrop-blur")).toContain("blur(var(--blur-sm))");
    expect(stylesheet.css).toContain("--blur-sm: 8px");
    const focusRules = rulesFor(".focus-visible\\:outline-hidden:focus-visible");
    const forcedColors: string[] = [];
    focusRules.forEach((rule) => rule.walkAtRules("media", (media) => {
      if (media.params === "(forced-colors: active)") forcedColors.push(media.toString());
    }));
    expect(forcedColors.join()).toContain("outline: 2px solid transparent");
    expect(valuesFor(".focus-visible\\:ring-2:focus-visible", "--tw-ring-shadow").join())
      .toContain("calc(2px + var(--tw-ring-offset-width))");
  });

  it("keeps component text sizing overridable and existing control cursors", () => {
    const title = rulesFor(".card-title")[0];
    expect(title.parent?.type).toBe("atrule");
    expect(title.parent?.toString()).toMatch(/^@layer components\s*\{/);
    expect(valuesFor(".card-title", "font-size")).toContain("0.875rem");
    expect(valuesFor(".text-base", "font-size")).toContain("var(--text-base)");
    expect(valuesFor("button", "cursor")).toContain("pointer");
    expect(valuesFor(":disabled", "cursor")).toContain("default");
  });
});
