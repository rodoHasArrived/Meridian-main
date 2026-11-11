// @vitest-environment node

import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import postcss, { type Rule } from "postcss";
import { describe, expect, it } from "vitest";

type Tokens = Map<string, string>;
type Rgb = [number, number, number];

const styles = postcss.parse(readFileSync(resolve(process.cwd(), "src/styles/index.css"), "utf8"));
const tableStyles = postcss.parse(readFileSync(resolve(process.cwd(), "src/styles/ui-kit-primitives.css"), "utf8"));
const buttonSource = readFileSync(resolve(process.cwd(), "src/design-system/button.tsx"), "utf8");
const semanticText = [
  { utility: "text-danger", role: "--ws-row-selected-danger-text", shade: "--red-dim" },
  { utility: "text-warning", role: "--ws-row-selected-warning-text", shade: "--orange-dim" },
  { utility: "text-success", role: "--ws-row-selected-success-text", shade: "--green-dim" }
];

function declarations(rule: Rule): Tokens {
  const result: Tokens = new Map();
  rule.each((node) => {
    if (node.type === "decl") result.set(node.prop, node.value);
  });
  return result;
}

function rgb(value: string, tokens: Tokens, depth = 0): Rgb {
  if (depth > 12) throw new Error(`Cyclic color token: ${value}`);
  const variable = value.match(/^var\((--[\w-]+)\)$/);
  if (variable) {
    const replacement = tokens.get(variable[1]);
    if (!replacement) throw new Error(`Missing color token: ${variable[1]}`);
    return rgb(replacement, tokens, depth + 1);
  }
  if (/^#[\da-f]{6}$/i.test(value)) {
    return [1, 3, 5].map((offset) => Number.parseInt(value.slice(offset, offset + 2), 16)) as Rgb;
  }
  const mix = value.match(/^color-mix\(in srgb,\s*(var\(--[\w-]+\)|#[\da-f]{6})\s+([\d.]+)%,\s*(var\(--[\w-]+\)|#[\da-f]{6})\)$/i);
  if (mix) return blend(rgb(mix[1], tokens, depth + 1), rgb(mix[3], tokens, depth + 1), Number(mix[2]) / 100);
  const hsl = value.match(/^(?:hsl\()?([\d.]+)\s+([\d.]+)%\s+([\d.]+)%\)?$/);
  if (!hsl) throw new Error(`Unsupported color in selected-surface contract: ${value}`);
  const hue = Number(hsl[1]) / 60;
  const saturation = Number(hsl[2]) / 100;
  const lightness = Number(hsl[3]) / 100;
  const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation;
  const secondary = chroma * (1 - Math.abs(hue % 2 - 1));
  const sectors: Rgb[] = [[chroma, secondary, 0], [secondary, chroma, 0], [0, chroma, secondary], [0, secondary, chroma], [secondary, 0, chroma], [chroma, 0, secondary]];
  return sectors[Math.floor(hue) % 6].map((channel) => (channel + lightness - chroma / 2) * 255) as Rgb;
}

function blend(tint: Rgb, surface: Rgb, opacity: number): Rgb {
  return surface.map((channel, index) => channel * (1 - opacity) + tint[index] * opacity) as Rgb;
}

function contrast(foreground: Rgb, background: Rgb): number {
  const luminance = (color: Rgb) => color.map((channel) => {
    const normalized = channel / 255;
    return normalized <= 0.04045 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4;
  }).reduce((sum, channel, index) => sum + channel * [0.2126, 0.7152, 0.0722][index], 0);
  const [high, low] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return (high + 0.05) / (low + 0.05);
}

const scopes: Array<{ name: string; tokens: Tokens }> = [];
styles.walkRules((rule) => {
  const tokens = declarations(rule);
  if (!tokens.has("--ws-row-selected")) return;
  const automaticDark = rule.parent?.type === "atrule" && rule.parent.name === "media";
  scopes.push({ name: `${automaticDark ? "system dark " : ""}${rule.selector}`, tokens });
});

let selected: Tokens = new Map();
tableStyles.walkRules((rule) => {
  if (rule.selector === ".dense-data-table tbody tr.selected") selected = declarations(rule);
});

describe("selected and tinted workstation text", () => {
  it("owns readable metadata roles in all five appearance scopes", () => {
    expect(scopes).toHaveLength(5);
    for (const { name, tokens } of scopes) {
      for (const role of ["--ws-row-selected-text-secondary", "--ws-row-selected-muted-foreground", "--ws-header-text-secondary", "--ws-tinted-text-secondary"]) {
        expect(tokens.has(role), `${name} must own ${role}, including nested light panels`).toBe(true);
      }
      for (const { role, shade } of semanticText) {
        expect(tokens.get(role), `${name} must retain its semantic text shade`).toBe(`var(${shade})`);
      }
    }
  });

  for (const { name, tokens } of scopes) {
    it(`keeps selected metadata, headers, and tinted-note text above AA in ${name}`, () => {
      const rowTokens = new Map([...tokens, ...selected]);
      const selectedSurface = rgb(tokens.get("--ws-row-selected")!, tokens);
      expect(contrast(rgb("var(--ws-text-muted)", rowTokens), selectedSurface)).toBeGreaterThanOrEqual(4.5);
      expect(contrast(rgb("var(--text-muted)", rowTokens), selectedSurface)).toBeGreaterThanOrEqual(4.5);
      expect(contrast(rgb("var(--muted-foreground)", rowTokens), selectedSurface)).toBeGreaterThanOrEqual(4.5);
      expect(contrast(rgb("var(--ws-header-text-secondary)", tokens), rgb("var(--ws-surface-subtle)", tokens))).toBeGreaterThanOrEqual(4.5);
      for (const { role } of semanticText) {
        expect(contrast(rgb(`var(${role})`, tokens), selectedSurface), role).toBeGreaterThanOrEqual(4.5);
      }

      // FigureDifferenceNote applies a 10% primary/warning wash to the canvas.
      for (const hue of ["--primary", "--warning"]) {
        const tint = rgb(`var(${hue})`, tokens);
        const canvas = rgb("var(--ws-page-bg)", tokens);
        const surface = blend(tint, canvas, 0.1);
        expect(contrast(rgb("var(--ws-tinted-text-secondary)", tokens), surface)).toBeGreaterThanOrEqual(4.5);
      }

      // Destructive buttons tint their containing canvas/panel in normal and hover states.
      for (const surface of ["--ws-page-bg", "--ws-surface", "--ws-surface-subtle", "--ws-surface-raised"]) {
        for (const opacity of [0.1, 0.15]) {
          const background = blend(rgb("var(--danger)", tokens), rgb(`var(${surface})`, tokens), opacity);
          expect(contrast(rgb("var(--red-dim)", tokens), background), `destructive button on ${surface} at ${opacity}`).toBeGreaterThanOrEqual(4.5);
        }
      }
    });
  }

  it("keeps selection indicators blue without overriding financial status colors", () => {
    expect(selected.get("box-shadow")).toBe("inset 3px 0 0 var(--interaction-selected)");
    for (const semantic of ["--green", "--red", "--orange", "--purple", "--success", "--danger"]) expect(selected.has(semantic)).toBe(false);
    let globalSelection = "";
    styles.walkRules((rule) => {
      if (rule.selector === '[aria-selected="true"]') globalSelection = declarations(rule).get("box-shadow") ?? "";
    });
    expect(globalSelection).toBe("inset 3px 0 0 var(--interaction-selected)");
  });

  it("applies semantic text shades to selected cells and destructive buttons", () => {
    for (const { utility, role } of semanticText) {
      let color: string | undefined;
      tableStyles.walkRules((rule) => {
        if (rule.selector === `.dense-data-table tbody tr.selected .${utility}`) color = declarations(rule).get("color");
      });
      expect(color, utility).toBe(`var(${role})`);
    }
    expect(buttonSource).toContain('destructive: "mds-btn--danger border-danger/60 bg-danger/10 text-[var(--red-dim)] hover:bg-danger/15"');
  });
});
