#!/usr/bin/env node
/** Context-aware visual policy for package references and the real workstation.
 * Prose, financial values and identifiers are not CSS. Only parsed declarations,
 * presentation attributes, style objects and canvas paint assignments are audited.
 */
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { createHash } from "node:crypto";
import postcss from "postcss";
import ts from "typescript";

const colorProperties = /^(?:color|background(?:-color|-image)?|border(?:-(?:top|right|bottom|left))?(?:-color)?|outline(?:-color)?|box-shadow|text-shadow|fill|stroke|stop-color|flood-color|caret-color|accent-color|text-decoration-color|fill-style|stroke-style)$/i;
const namedColorPattern = /\b(?:white|black|red|green|blue|yellow|orange|purple|gray|grey)\b/gi;
const sourceExtensions = new Set([".css", ".html", ".jsx", ".js", ".tsx", ".ts"]);
const ignoredDirectories = new Set(["node_modules", ".git", "__pycache__", "__tests__"]);
const testName = /\.(?:test|spec)\.[cm]?[jt]sx?$/i;
const canonicalPackageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../../Meridian Design System");
const canonicalRuntimeRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../src");
const vendorRuntimeRegistry = JSON.parse(fs.readFileSync(path.join(canonicalPackageRoot, "scripts/vendor-runtime-integrity.json"), "utf8"));

/** Vendored code is integrity-pinned, not claimed reproducible from absent upstream sources. */
export function vendorRuntimeStatus(filename, source, { packageRoot = canonicalPackageRoot, registry = vendorRuntimeRegistry } = {}) {
  const relative = path.relative(packageRoot, path.resolve(filename)).split(path.sep).join("/");
  const expected = registry.files[relative];
  if (!expected) return null;
  const normalized = source.replace(/\r\n/g, "\n");
  const actual = createHash("sha256").update(normalized).digest("hex");
  return actual === expected && normalized.startsWith(`${registry.requiredHeader}\n`)
    ? { skip: true }
    : { code: "vendor-runtime-integrity", path: relative, line: 1, detail: "Vendored dc-runtime bytes/header differ from the reviewed integrity pin; review the upstream change before updating the pin" };
}

export function walkSources(root) {
  if (!fs.existsSync(root)) return [];
  if (fs.statSync(root).isFile()) return [root];
  return fs.readdirSync(root, { withFileTypes: true }).sort((a, b) => a.name.localeCompare(b.name)).flatMap((entry) => {
    if (ignoredDirectories.has(entry.name)) return [];
    const candidate = path.join(root, entry.name);
    if (entry.isDirectory()) return walkSources(candidate);
    if (!sourceExtensions.has(path.extname(entry.name)) || testName.test(entry.name) || entry.name.endsWith(".d.ts")) return [];
    return [candidate];
  });
}

function kebab(value) { return value.replace(/[A-Z]/g, (letter) => `-${letter.toLowerCase()}`); }
function withoutComments(value) { return value.replace(/\/\*[\s\S]*?\*\//g, (comment) => " ".repeat(comment.length)); }

/** Remove complete var() calls, including nested fallbacks, from literal checks. */
function unguarded(value) {
  let output = "";
  for (let index = 0; index < value.length;) {
    if (value.startsWith("var(", index)) {
      let depth = 1;
      index += 4;
      while (index < value.length && depth) {
        if (value[index] === "(") depth++;
        if (value[index] === ")") depth--;
        index++;
      }
    } else output += value[index++];
  }
  return output;
}

function literalColors(value) {
  const plain = unguarded(withoutComments(value)).replace(/url\([^)]*\)/g, "");
  const result = [...plain.matchAll(/#[\da-f]{3,8}\b|\b(?:rgba?|hsla?|oklch|oklab|lch|lab)\(\s*[+\-]?(?:\d|\.\d)[^)]*\)/gi)].map((match) => match[0]);
  result.push(...[...plain.matchAll(namedColorPattern)].map((match) => match[0]));
  return result;
}

function semanticGradient(value, context, reducedMotionSafe) {
  // A semantic scale is not a decorative surface. The annotation is scoped to
  // one declaration/rule and cannot exempt un-tokenized endpoints.
  const role = /@design-role\s+(quantitative-scale|loading-sweep)\b/.exec(context)?.[1];
  return Boolean(role && (role !== "loading-sweep" || reducedMotionSafe) && /var\(--/.test(value) && literalColors(value).length === 0);
}

function circularRadius(value, context) {
  const width = /\bwidth\s*:\s*["']?([\d.]+(?:px|rem|em|%)?)/.exec(context)?.[1];
  const height = /\bheight\s*:\s*["']?([\d.]+(?:px|rem|em|%)?)/.exec(context)?.[1];
  const square = Boolean(width && height && width === height);
  return value.trim() === "50%" || square && /^(?:999(?:9)?px|calc\(infinity\s*\*\s*1px\))$/.test(value.trim()) ||
    /@design-role\s+(?:circular-indicator|control-thumb)\b/.test(context) && /^(?:[\d.]+px|[\d.]+rem)$/.test(value.trim());
}

function bareChannelTokens(value) {
  const channels = /^(?:--(?:background|foreground|card(?:-foreground)?|popover(?:-foreground)?|primary(?:-foreground)?|secondary(?:-foreground)?|muted(?:-foreground)?|accent(?:-foreground)?|border|input|ring|paper|live(?:-env)?|warning|success|danger|panel-(?:strong|soft)|surface-raise)|--sidebar(?:-(?:foreground|primary(?:-foreground)?|accent(?:-foreground)?|border|ring))?|--chart-[1-5])$/;
  const stack = []; const violations = [];
  value = withoutComments(value);
  for (let index = 0; index < value.length;) {
    const call = /^([a-z-]+)\(/i.exec(value.slice(index));
    if (call) {
      const name = call[1].toLowerCase();
      if (name === "var" && !stack.some((parent) => ["hsl", "hsla", "url"].includes(parent))) {
        const token = /^\s*(--[\w-]+)/.exec(value.slice(index + call[0].length))?.[1];
        if (token && channels.test(token)) violations.push(token);
      }
      stack.push(name); index += call[0].length;
    } else {
      if (value[index] === ")") stack.pop();
      index++;
    }
  }
  return [...new Set(violations)];
}

export function analyzeSource(filename, source, { displayPath = filename, channelTokens = false } = {}) {
  const diagnostics = [];
  const seen = new Set();
  function add(code, position, detail) {
    const line = source.slice(0, Math.max(0, position)).split("\n").length;
    const key = `${code}:${position}:${detail}`;
    if (!seen.has(key)) diagnostics.push({ code, path: displayPath, line, detail });
    seen.add(key);
  }
  function utilities(value, position) {
    if (channelTokens) for (const match of value.matchAll(/(?:^|\s)(?:[\w-]+:)*((?:bg|text|border(?:-[trblxy])?|ring|fill|stroke|outline|shadow)-\[([^\]]+)\])/g)) {
      for (const token of bareChannelTokens(match[2].replace(/_/g, " "))) {
        add("channel-color-collision", position + match.index, `${token} contains HSL channels; use hsl(...) or a complete --ws-* color in ${match[1]}`);
      }
    }
    for (const match of value.matchAll(/(?:^|\s)(?:[\w-]+:)*((?:bg|text|border(?:-[trblxy])?|ring|fill|stroke|outline|divide|decoration|from|via|to)-(?:slate|zinc|neutral|gray|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d+(?:\/[\d.]+)?|(?:bg|text|border(?:-[trblxy])?|ring|fill|stroke)-(?:white|black)(?:\/[\d.]+)?|(?:bg|text|border|ring|fill|stroke)-\[(?:#[\da-f]+|(?:rgb|hsl)a?\([^\]]*)\]|shadow-\[[^\]]+\]|rounded-(?:2xl|3xl|4xl)|shadow-(?:sm|md|lg|xl|2xl))(?![\w-])/gi)) {
      const arbitraryColor = /^[\w-]+-\[(.*)\]$/.exec(match[1])?.[1];
      if (arbitraryColor && literalColors(arbitraryColor.replace(/_/g, " ")).length === 0) continue;
      add("unapproved-utility", position + match.index, `Use a design-system semantic role instead of ${match[1]}`);
    }
  }
  function declaration(property, value, position, context = "", reducedMotionSafe = false) {
    property = kebab(property);
    if (property.startsWith("--")) return;
    if (colorProperties.test(property)) {
      for (const literal of literalColors(value)) {
        add(literal.startsWith("#") ? "raw-hex" : "raw-color", position, `Use a semantic token for ${property}: ${literal}`);
      }
      if (channelTokens) for (const token of bareChannelTokens(value)) {
        add("channel-color-collision", position, `${token} contains HSL channels in the workstation; wrap it in hsl(...) or use its complete --ws-* color role for ${property}`);
      }
    }
    if (/^border(?:-(?:top-left|top-right|bottom-left|bottom-right))?-radius$/.test(property) && !circularRadius(value, context)) {
      for (const match of unguarded(value).matchAll(/([\d.]+)(px|rem)\b/g)) {
        const pixels = Number(match[1]) * (match[2] === "rem" ? 16 : 1);
        if (pixels > 6) add("large-radius", position, `Surface radius ${match[0]} exceeds the 6px named-scale ceiling`);
      }
    }
    if (/\b(?:linear|radial|conic)-gradient\(/.test(value) && !semanticGradient(value, context, reducedMotionSafe)) {
      add("decorative-gradient", position, "Use flat surfaces; semantic gradients require token endpoints and a scoped @design-role (loading sweeps also require reduced-motion animation:none)");
    }
  }
  function css(text, offset = 0, inline = false) {
    text = text.replace(/\$\{[^}]*\}/g, (hole) => "x".padEnd(hole.length));
    const prefix = inline ? "x{" : "";
    try {
      const tree = postcss.parse(`${prefix}${text}${inline ? "}" : ""}`);
      tree.walkAtRules("apply", (node) => utilities(node.params, offset + (node.source?.start?.offset ?? 0)));
      tree.walkDecls((node) => {
        const position = offset + Math.max(0, (node.source?.start?.offset ?? 0) - prefix.length);
        const context = `${node.parent?.toString() ?? ""}\n${node.prev()?.type === "comment" ? node.prev().text : ""}`;
        let reducedMotionSafe = false;
        const selector = node.parent?.type === "rule" ? node.parent.selector : null;
        if (selector) tree.walkAtRules("media", (media) => {
          if (!/prefers-reduced-motion\s*:\s*reduce/.test(media.params)) return;
          media.walkRules((rule) => {
            if (!rule.selectors.includes(selector)) return;
            rule.walkDecls(/^(?:animation|animation-name)$/, (animation) => { if (animation.value === "none") reducedMotionSafe = true; });
          });
        });
        declaration(node.prop, node.value, position, context, reducedMotionSafe);
      });
    } catch (error) {
      add("style-parse", offset, `Cannot parse rendered style: ${error.reason ?? error.message}`);
    }
  }
  function script(text, offset = 0, jsx = true) {
    const tree = ts.createSourceFile(filename, text, ts.ScriptTarget.Latest, true, jsx ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
    const literals = (node) => {
      if (ts.isStringLiteralLike(node)) return [{ value: node.text, position: offset + node.getStart(tree) + 1 }];
      if (ts.isNumericLiteral(node)) return [{ value: `${node.text}px`, position: offset + node.getStart(tree) }];
      if (ts.isConditionalExpression(node)) return [...literals(node.whenTrue), ...literals(node.whenFalse)];
      if (ts.isParenthesizedExpression(node) || ts.isAsExpression(node)) return literals(node.expression);
      if (ts.isTemplateExpression(node)) return [{
        value: node.getText(tree).slice(1, -1).replace(/\$\{[^}]*\}/g, (hole) => "x".padEnd(hole.length)),
        position: offset + node.getStart(tree) + 1
      }];
      return [];
    };
    const nameOf = (node) => node && (ts.isIdentifier(node) || ts.isStringLiteral(node) ? node.text : ts.isPropertyAccessExpression(node) ? node.name.text : "");
    function isStyleObject(node) {
      for (let ancestor = node.parent; ancestor; ancestor = ancestor.parent) {
        if (ts.isJsxAttribute(ancestor)) return ancestor.name.getText(tree) === "style";
        if (ts.isPropertyAssignment(ancestor) && /^(?:style|styles)$/.test(nameOf(ancestor.name))) return true;
        if (ts.isVariableDeclaration(ancestor)) return /(?:styles?|Styles?|CSS|Css)$/.test(nameOf(ancestor.name));
        if (ts.isCallExpression(ancestor) && /(?:createElement|jsx|jsxs|jsxDEV)$/.test(nameOf(ancestor.expression))) {
          return Boolean(ancestor.arguments[0] && ts.isStringLiteral(ancestor.arguments[0]));
        }
      }
      return false;
    }
    function visit(node) {
      if (ts.isStringLiteralLike(node) || ts.isTemplateHead(node) || ts.isTemplateMiddle(node) || ts.isTemplateTail(node)) {
        utilities(node.text, offset + node.getStart(tree) + 1);
      }
      if (ts.isJsxAttribute(node) && node.initializer) {
        const property = node.name.getText(tree);
        const initializer = ts.isJsxExpression(node.initializer) ? node.initializer.expression : node.initializer;
        if (initializer && colorProperties.test(kebab(property))) {
          for (const literal of literals(initializer)) declaration(property, literal.value, literal.position);
        }
      }
      if (ts.isPropertyAssignment(node)) {
        const property = nameOf(node.name);
        if (isStyleObject(node) && (colorProperties.test(kebab(property)) || /radius/i.test(property))) {
          for (const literal of literals(node.initializer)) declaration(property, literal.value, literal.position, node.parent.getFullText(tree));
        }
      }
      if (ts.isBinaryExpression(node) && node.operatorToken.kind === ts.SyntaxKind.EqualsToken && ts.isPropertyAccessExpression(node.left)) {
        const property = node.left.name.text;
        if (colorProperties.test(kebab(property))) {
          for (const literal of literals(node.right)) declaration(property, literal.value, literal.position);
        }
      }
      if (ts.isVariableDeclaration(node) && node.initializer) {
        const name = nameOf(node.name);
        if (/^(?:css|styles?|.*CSS|.*Css)$/.test(name)) {
          // CSS injection templates have ${...} holes. Replace them with a
          // valid CSS identifier before parsing without treating JS as CSS.
          const body = node.initializer.getText(tree);
          if (body.startsWith("`") && /[{};]/.test(body)) {
            css(body.slice(1, -1).replace(/\$\{[^}]*\}/g, (hole) => "x".padEnd(hole.length)), offset + node.initializer.getStart(tree) + 1);
          }
        } else if (/^(?:PLOT|GRID|AXIS|BORDER|CROSSHAIR|DRAWDOWN|FILL|UP|DOWN|DN|[\w]*[Cc]olou?r)$/.test(name)) {
          for (const literal of literals(node.initializer)) declaration("color", literal.value, literal.position);
        }
      }
      // Literal HTML snippets assigned to innerHTML still render styles.
      if (ts.isBinaryExpression(node) && ts.isPropertyAccessExpression(node.left) && node.left.name.text === "innerHTML") {
        for (const literal of literals(node.right)) html(literal.value, literal.position, false);
      }
      ts.forEachChild(node, visit);
    }
    visit(tree);
  }
  function html(text, offset = 0, inspectScripts = true) {
    const masked = text.replace(/<!--[\s\S]*?-->/g, (comment) => " ".repeat(comment.length));
    for (const match of masked.matchAll(/<style\b[^>]*>([\s\S]*?)<\/style\s*>/gi)) {
      css(match[1], offset + match.index + match[0].indexOf(match[1]));
    }
    for (const match of masked.matchAll(/<(?!script\b|style\b)[a-z][^>]*>/gi)) {
      const tag = match[0];
      for (const attr of tag.matchAll(/\b(style|fill|stroke|stop-color|flood-color)\s*=\s*(["'])(.*?)\2/gi)) {
        const position = offset + match.index + attr.index;
        if (attr[1].toLowerCase() === "style") css(attr[3], position + attr[0].indexOf(attr[3]), true);
        else declaration(attr[1], attr[3], position);
      }
    }
    if (inspectScripts) {
      for (const match of masked.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script\s*>/gi)) {
        if (/\bsrc\s*=/.test(match[1]) || /(?:application\/json|application\/ld\+json)/.test(match[1])) continue;
        script(match[2], offset + match.index + match[0].indexOf(match[2]));
      }
    }
  }
  const extension = path.extname(filename).toLowerCase();
  if (extension === ".css") css(source);
  else if (extension === ".html") html(source);
  else if ([".ts", ".tsx", ".js", ".jsx"].includes(extension)) script(source);
  return diagnostics.sort((a, b) => a.line - b.line || a.code.localeCompare(b.code));
}

export function analyzeFiles(files, base) {
  const diagnostics = [];
  if (path.resolve(base) === canonicalPackageRoot) {
    for (const relative of Object.keys(vendorRuntimeRegistry.files)) {
      if (!fs.existsSync(path.join(canonicalPackageRoot, relative))) diagnostics.push({ code: "vendor-runtime-integrity", path: relative, line: 1, detail: "Registered vendored dc-runtime file is missing" });
    }
  }
  for (const file of files) {
    // Only this exact generated artifact is validated by the package producer's
    // freshness check. An application file sharing its basename is ordinary code.
    if (path.resolve(file) === path.join(canonicalPackageRoot, "_ds_bundle.js")) continue;
    const source = fs.readFileSync(file, "utf8");
    const vendor = vendorRuntimeStatus(file, source);
    if (vendor?.skip) continue;
    if (vendor) { diagnostics.push(vendor); continue; }
    diagnostics.push(...analyzeSource(file, source, {
      displayPath: path.relative(base, file).split(path.sep).join("/"),
      channelTokens: path.resolve(file).startsWith(`${canonicalRuntimeRoot}${path.sep}`)
    }));
  }
  return diagnostics;
}

export function runCli(args = process.argv.slice(2)) {
  let files; let base; const json = args.includes("--json");
  const argument = (name) => args.includes(name) ? args[args.indexOf(name) + 1] : undefined;
  if (args.includes("--stdin")) {
    const input = JSON.parse(fs.readFileSync(0, "utf8")); files = input.files; base = input.base;
  } else {
    const root = path.resolve(argument("--root") ?? path.join(path.dirname(fileURLToPath(import.meta.url)), "../src"));
    base = root; files = walkSources(root);
  }
  const diagnostics = analyzeFiles(files, base);
  if (json) process.stdout.write(`${JSON.stringify(diagnostics)}\n`);
  else {
    for (const diagnostic of diagnostics) process.stdout.write(`${diagnostic.code}: ${diagnostic.path}:${diagnostic.line}: ${diagnostic.detail}\n`);
    process.stdout.write(`Design visual policy: ${diagnostics.length ? "FAIL" : "PASS"} (${files.length} sources, ${diagnostics.length} findings)\n`);
  }
  return diagnostics.length ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) process.exitCode = runCli();
