// @vitest-environment node

import { readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { resolve } from "node:path";
import postcss, { type AcceptedPlugin, type Node, type Result, type Rule } from "postcss";
import { beforeAll, describe, expect, it } from "vitest";

const require = createRequire(import.meta.url);

describe("workstation print adapter", () => {
  let stylesheet: Result;

  beforeAll(async () => {
    const config = require("../postcss.config.cjs") as {
      plugins: Record<string, Record<string, unknown>>;
    };
    const plugins = Object.entries(config.plugins).map(([name, options]) =>
      (require(name) as (options: Record<string, unknown>) => AcceptedPlugin)(options)
    );
    const entry = resolve(process.cwd(), "src/styles/print.css");
    stylesheet = await postcss(plugins).process(readFileSync(entry, "utf8"), { from: entry });
  }, 30_000);

  function declarations(selector: string, property: string) {
    const values: string[] = [];
    stylesheet.root.walkRules((rule) => {
      if (rule.selectors.includes(selector)) {
        rule.walkDecls(property, (declaration) => { values.push(declaration.value); });
      }
    });
    return values;
  }

  it("ships through the workstation entry without changing screen styles", () => {
    expect(readFileSync(resolve(process.cwd(), "src/main.tsx"), "utf8"))
      .toContain('import "@/styles/print.css"');
    expect(stylesheet.warnings()).toEqual([]);
    stylesheet.root.walkRules((rule: Rule) => {
      let parent: Node["parent"] = rule.parent;
      while (parent && !(parent.type === "atrule" && "name" in parent && parent.name === "media" && "params" in parent && parent.params === "print")) {
        parent = parent.parent;
      }
      expect(parent, `Screen-visible print rule: ${rule.selector}`).toBeDefined();
    });
  });

  it("unclips mounted records and retains the selected inspector with repeating headers", () => {
    for (const selector of [".workstation-frame", ".workbench-scroll", ".dense-data-table-wrap", ".workspace-workbench-shell-inspector"]) {
      expect(declarations(selector, "max-height")).toContain("none");
      expect(declarations(selector, "overflow")).toContain("visible");
    }
    expect(declarations(".workspace-workbench-shell--switchable .workspace-workbench-shell-inspector", "display")).toContain("block");
    expect(declarations(".workbench thead", "display")).toContain("table-header-group");
    expect(declarations(".workbench tr", "break-inside")).toContain("avoid");
    expect(declarations(".workbench td", "white-space")).toContain("normal");
    expect(declarations(".workbench td .font-mono", "white-space")).toContain("nowrap");
    expect(declarations(".workstation-provenance", "display")).not.toContain("none");
    expect(declarations(".workstation-rail-desktop", "display")).toContain("none");
    expect(declarations(".workstation-session-card", "display")).toContain("flex");
    expect(declarations(".workstation-actions > :not(.workstation-session-card)", "display")).toContain("none");
  });

  it("pairs paper accents with light ink instead of retaining dark-theme fill text", () => {
    expect(declarations(":root[data-theme]", "--primary-foreground")).toContain("0 0% 100%");
    expect(declarations(":root[data-theme]", "--accent-foreground")).toContain("0 0% 100%");
  });
});
