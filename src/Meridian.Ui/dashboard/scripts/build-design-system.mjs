#!/usr/bin/env node

// The visual package is a React peer bundle, independent of the workstation's
// React version. This tool uses the dashboard's explicit TypeScript/PostCSS
// dependencies; generated files never depend on timestamps or local paths.
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import postcss from "postcss";
import ts from "typescript";

const scriptPath = fileURLToPath(import.meta.url);
export const defaultPackageRoot = path.resolve(path.dirname(scriptPath), "../../../../Meridian Design System");
const posix = (value) => value.split(path.sep).join("/");
const hash = (value) => createHash("sha256").update(value).digest("hex").slice(0, 12);
const compare = (left, right) => left < right ? -1 : left > right ? 1 : 0;
const sourceText = (value) => value.replace(/\r\n/g, "\n");

async function filesUnder(directory) {
  const entries = await fs.readdir(directory, { withFileTypes: true });
  const files = await Promise.all(entries.sort((a, b) => compare(a.name, b.name))
    .filter((entry) => !["node_modules", ".git", "__pycache__"].includes(entry.name))
    .map((entry) => entry.isDirectory() ? filesUnder(path.join(directory, entry.name)) : [path.join(directory, entry.name)]));
  return files.flat();
}

function parseModule(source, filename) {
  const parsed = ts.createSourceFile(filename, source, ts.ScriptTarget.ES2020, true,
    filename.endsWith(".jsx") ? ts.ScriptKind.JSX : ts.ScriptKind.JS);
  if (parsed.parseDiagnostics.length) throw new Error(ts.formatDiagnosticsWithColorAndContext(parsed.parseDiagnostics, {
    getCanonicalFileName: (name) => name, getCurrentDirectory: () => "", getNewLine: () => "\n"
  }));
  const imports = new Set();
  const exportedNames = new Set();
  const exported = (node) => node.modifiers?.some((modifier) => modifier.kind === ts.SyntaxKind.ExportKeyword);
  for (const statement of parsed.statements) {
    if ((ts.isImportDeclaration(statement) || ts.isExportDeclaration(statement)) && statement.moduleSpecifier) {
      imports.add(statement.moduleSpecifier.text);
    }
    if (ts.isExportDeclaration(statement) && statement.exportClause && ts.isNamedExports(statement.exportClause)) {
      for (const element of statement.exportClause.elements) exportedNames.add(element.name.text);
    }
    if (exported(statement)) {
      if (statement.name && ts.isIdentifier(statement.name)) exportedNames.add(statement.name.text);
      if (ts.isVariableStatement(statement)) {
        for (const declaration of statement.declarationList.declarations) {
          if (ts.isIdentifier(declaration.name)) exportedNames.add(declaration.name.text);
        }
      }
    }
  }
  return { imports: [...imports].sort(), exportedNames: [...exportedNames].sort() };
}

function cleanPrinterTrivia(output) {
  // TypeScript can emit a trailing space before an interleaved parameter
  // comment. Remove printer trivia, while preserving actual string/template
  // contents (including deliberate spaces in CSS or displayed text).
  const parsed = ts.createSourceFile("compiled.js", output, ts.ScriptTarget.ES2020, true, ts.ScriptKind.JS);
  const literals = [];
  const visit = (node) => {
    if (ts.isStringLiteralLike(node) || [ts.SyntaxKind.TemplateHead, ts.SyntaxKind.TemplateMiddle, ts.SyntaxKind.TemplateTail].includes(node.kind)) {
      literals.push([node.getStart(parsed), node.end]);
    }
    ts.forEachChild(node, visit);
  };
  visit(parsed);
  return output.replace(/[\t ]+$/gm, (trivia, offset) => literals.some(([start, end]) => start <= offset && offset < end) ? trivia : "");
}

async function resolveImport(root, importer, request) {
  if (request === "react") return "react";
  if (!request.startsWith(".")) throw new Error(`Unsupported external ${request} in ${importer}; the package has only a React peer`);
  const base = path.resolve(root, path.dirname(importer), request);
  for (const candidate of [base, `${base}.js`, `${base}.jsx`, path.join(base, "index.js"), path.join(base, "index.jsx")]) {
    const relative = posix(path.relative(root, candidate));
    if (relative.startsWith("../") || path.isAbsolute(relative)) continue;
    if ((await fs.stat(candidate).catch(() => null))?.isFile()) return relative;
  }
  throw new Error(`Missing import ${request} from ${importer}`);
}

async function buildGraph(root, components) {
  const modules = new Map();
  async function visit(filename) {
    if (modules.has(filename)) return;
    const source = sourceText(await fs.readFile(path.join(root, filename), "utf8"));
    const parsed = parseModule(source, filename);
    const record = { filename, source, ...parsed, dependencies: {} };
    modules.set(filename, record);
    for (const request of parsed.imports) {
      const dependency = await resolveImport(root, filename, request);
      record.dependencies[request] = dependency;
      if (dependency !== "react") await visit(dependency);
    }
  }
  for (const component of components) {
    await visit(component.sourcePath);
    if (!modules.get(component.sourcePath).exportedNames.includes(component.name)) {
      throw new Error(`Public export ${component.name} is absent from ${component.sourcePath}`);
    }
  }
  // Publication is explicit: a newly exported component must be classified in
  // the public inventory instead of silently disappearing from the bundle.
  const published = new Set(components.map((component) => `${component.sourcePath}:${component.name}`));
  for (const filename of await filesUnder(path.join(root, "components"))) {
    if (!/\.(?:jsx|js)$/.test(filename)) continue;
    const relative = posix(path.relative(root, filename));
    const parsed = modules.get(relative) ?? parseModule(await fs.readFile(filename, "utf8"), relative);
    for (const name of parsed.exportedNames) {
      if (/^[A-Z]/.test(name) && !published.has(`${relative}:${name}`)) {
        throw new Error(`Unclassified public export ${name} in ${relative}; update scripts/public-exports.json`);
      }
    }
  }
  return [...modules.values()].sort((a, b) => compare(a.filename, b.filename));
}

function compileBundle(namespace, components, graph) {
  const published = new Set(components.map((component) => `${component.sourcePath}:${component.name}`));
  const metadata = {
    format: 4, namespace, components,
    sourceHashes: Object.fromEntries(graph.map((record) => [record.filename, hash(record.source)])),
    inlinedExternals: [],
    unexposedExports: graph.flatMap((record) => record.exportedNames
      .filter((name) => !published.has(`${record.filename}:${name}`))
      .map((name) => ({ name, sourcePath: record.filename })))
      .sort((a, b) => compare(a.name, b.name) || compare(a.sourcePath, b.sourcePath))
  };
  const factories = graph.map((record) => {
    const compiled = ts.transpileModule(record.source, {
      fileName: record.filename, reportDiagnostics: true,
      compilerOptions: {
        target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS,
        jsx: ts.JsxEmit.React, esModuleInterop: true, newLine: ts.NewLineKind.LineFeed,
        removeComments: false, importHelpers: false
      }
    });
    const failures = compiled.diagnostics?.filter((diagnostic) => diagnostic.category === ts.DiagnosticCategory.Error) ?? [];
    if (failures.length) throw new Error(`Cannot compile ${record.filename}: ${failures.map((item) => ts.flattenDiagnosticMessageText(item.messageText, "\n")).join("; ")}`);
    return `${JSON.stringify(record.filename)}: function(module, exports, require) {\n${cleanPrinterTrivia(compiled.outputText)}\n}`;
  });
  const dependencies = Object.fromEntries(graph.map((record) => [record.filename, record.dependencies]));
  return `/* @ds-bundle: ${JSON.stringify(metadata)} */
// GENERATED by scripts/build-design-system.mjs. Do not edit.
(() => {
"use strict";
const namespace = (window[${JSON.stringify(namespace)}] = window[${JSON.stringify(namespace)}] || {});
namespace.__errors = namespace.__errors || [];
const factories = {
${factories.join(",\n")}
};
const dependencies = ${JSON.stringify(dependencies)};
const cache = Object.create(null);
function load(id) {
  if (id === "react") {
    if (!window.React) throw new Error("Meridian design system requires its React peer before a component is consumed");
    return window.React;
  }
  if (cache[id]) return cache[id].exports;
  if (!factories[id]) throw new Error("Unknown design-system module: " + id);
  const module = { exports: {} };
  cache[id] = module;
  try {
    factories[id](module, module.exports, (request) => {
      const target = dependencies[id][request];
      if (!target) throw new Error("Undeclared import " + request + " from " + id);
      return load(target);
    });
  } catch (error) {
    delete cache[id];
    namespace.__errors.push({ sourcePath: id, message: String(error && error.message || error) });
    throw error;
  }
  return module.exports;
}
for (const component of ${JSON.stringify(components)}) {
  Object.defineProperty(namespace, component.name, {
    configurable: true, enumerable: true,
    get() {
      const value = load(component.sourcePath)[component.name];
      if (value === undefined) throw new Error("Missing public export " + component.name);
      Object.defineProperty(namespace, component.name, { configurable: true, enumerable: true, writable: true, value });
      return value;
    },
    set(value) {
      Object.defineProperty(namespace, component.name, { configurable: true, enumerable: true, writable: true, value });
    }
  });
}
})();
`;
}

function attributes(text) {
  return Object.fromEntries([...text.matchAll(/([\w-]+)="([^"]*)"/g)].map((match) => [match[1], match[2]]));
}

async function cssMetadata(root) {
  const globalCssPaths = [];
  const cssTrees = new Map();
  async function visit(relative) {
    if (cssTrees.has(relative)) return;
    const tree = postcss.parse(await fs.readFile(path.join(root, relative), "utf8"), { from: relative });
    cssTrees.set(relative, tree);
    const imports = [];
    tree.walkAtRules("import", (rule) => {
      const imported = rule.params.match(/^["']([^"']+)["']/)?.[1];
      if (imported && !/^https?:/.test(imported)) imports.push(posix(path.normalize(path.join(path.dirname(relative), imported))));
    });
    for (const imported of imports) await visit(imported);
    globalCssPaths.push(relative);
  }
  await visit("styles.css");
  const tokens = new Map();
  const themes = new Map();
  const fonts = [];
  for (const relative of globalCssPaths) {
    const tree = cssTrees.get(relative);
    tree.walkDecls(/^--/, (declaration) => {
      const rule = declaration.parent;
      if (rule.type !== "rule") return;
      const media = [];
      for (let parent = rule.parent; parent; parent = parent.parent) {
        if (parent.type === "atrule") media.unshift(`@${parent.name} ${parent.params}`);
      }
      const scope = media.length || rule.selector !== ":root" ? [...media, rule.selector].join(" ") : undefined;
      const comment = declaration.next();
      const annotation = comment?.type === "comment" ? comment.text.match(/@kind\s+(\w+)/)?.[1] : undefined;
      const name = declaration.prop;
      const value = declaration.value;
      const kind = annotation || (/font|type-|text-size|line-height|letter-spacing/.test(name) ? "font"
        : /radius/.test(name) ? "radius" : /shadow/.test(name) ? "shadow"
          : /(?:spacing|space-|pad-|gap-|height|width)/.test(name) ? "spacing"
            : /#|rgba?\(|hsla?\(|color-mix|color|bg|border|accent|green|red|orange|purple|amber|cyan|chrome|series|drawdown|equity|crosshair/.test(`${name} ${value}`) ? "color" : "other");
      const token = { name, value, kind, definedIn: relative };
      if (scope) token.scope = scope;
      if (annotation) token.annotation = annotation;
      tokens.set(`${scope ?? ""}:${name}`, token);
      if (rule.selector.includes("data-")) {
        themes.set(rule.selector, {
          selector: rule.selector,
          label: rule.selector.replace(/[:\[\]="']/g, " ").replace(/data-/g, "").replace(/-/g, " ").replace(/\s+/g, " ").trim()
        });
      }
    });
    tree.walkAtRules("font-face", (rule) => {
      const declarations = Object.fromEntries(rule.nodes.filter((node) => node.type === "decl").map((node) => [node.prop, node.value]));
      fonts.push({ family: declarations["font-family"].replace(/^["']|["']$/g, ""), weight: declarations["font-weight"] ?? "normal", style: declarations["font-style"] ?? "normal", cssPath: relative,
        files: [...(declarations.src ?? "").matchAll(/url\(["']?([^)'" ]+)/g)].map((match) => match[1]) });
    });
  }
  return { globalCssPaths, tokens: [...tokens.values()], themes: [...themes.values()], fonts };
}

async function buildManifest(root, inventory) {
  const cards = [];
  const templates = [];
  const files = await filesUnder(root);
  for (const filename of files) {
    const relative = posix(path.relative(root, filename));
    if (filename.endsWith(".html")) {
      const text = await fs.readFile(filename, "utf8");
      const marker = text.match(/<!--\s*@dsCard\b([\s\S]*?)-->/);
      if (!marker && filename.endsWith(".card.html")) throw new Error(`Card ${relative} has no @dsCard metadata`);
      if (marker) {
        const card = attributes(marker[1]);
        if (!card.name || !card.group || !card.viewport) throw new Error(`Card ${relative} lacks name, group, or viewport`);
        cards.push({ path: relative, ...card });
      }
    }
    if (relative.startsWith("templates/") && filename.endsWith(".dc.html")) {
      const text = await fs.readFile(filename, "utf8");
      const marker = text.match(/<!--\s*@template\b([\s\S]*?)-->/);
      const metadata = marker ? attributes(marker[1]) : {};
      const folder = posix(path.dirname(relative));
      const record = { name: metadata.name ?? path.basename(filename, ".dc.html"), description: metadata.description ?? "", folder, entryPath: relative };
      if ((await fs.stat(path.join(root, folder, ".thumbnail")).catch(() => null))?.isFile()) record.thumbnail = { path: `${folder}/.thumbnail`, kind: "captured" };
      templates.push(record);
    }
  }
  const css = await cssMetadata(root);
  const brandFonts = css.fonts.map((font) => ({ family: font.family, status: font.files.length ? "bundled" : "no-file", tokens: inventory.brandFontTokens?.[font.family] ?? [], path: font.cssPath }));
  return { namespace: inventory.namespace, components: inventory.components, startingPoints: [], cards, templates,
    hasThumbnailHtml: files.some((filename) => path.basename(filename) === ".thumbnail.html"),
    ...css, brandFonts, source: "spa" };
}

export async function generateDesignSystem(root = defaultPackageRoot) {
  const inventory = JSON.parse(await fs.readFile(path.join(root, "scripts/public-exports.json"), "utf8"));
  if (inventory.schemaVersion !== 1 || !/^[A-Za-z_$][\w$]*$/.test(inventory.namespace)) throw new Error("Invalid public export inventory");
  const names = new Set();
  for (const entry of inventory.components) {
    if (!/^[A-Z][\w$]*$/.test(entry.name) || names.has(entry.name) || !/^components\/[\w./-]+\.(?:jsx|js)$/.test(entry.sourcePath) || entry.sourcePath.includes("..")) {
      throw new Error(`Invalid or duplicate public export ${entry.name}`);
    }
    names.add(entry.name);
  }
  const graph = await buildGraph(root, inventory.components);
  return {
    "_ds_bundle.js": compileBundle(inventory.namespace, inventory.components, graph),
    "_ds_manifest.json": `${JSON.stringify(await buildManifest(root, inventory), null, 2)}\n`
  };
}

export async function writeDesignSystem({ root = defaultPackageRoot, check = false, outputDirectory = root } = {}) {
  const outputs = await generateDesignSystem(root);
  const stale = [];
  for (const [filename, source] of Object.entries(outputs)) {
    const destination = path.join(outputDirectory, filename);
    if (check) {
      if (await fs.readFile(destination, "utf8").catch(() => null) !== source) stale.push(filename);
    } else {
      await fs.mkdir(outputDirectory, { recursive: true });
      await fs.writeFile(destination, source);
    }
  }
  if (stale.length) throw new Error(`Stale generated design-system artifacts: ${stale.join(", ")}. Run design:build.`);
  return Object.keys(outputs);
}

async function main() {
  const args = process.argv.slice(2);
  let root = defaultPackageRoot;
  let outputDirectory;
  let check = false;
  for (let index = 0; index < args.length; index += 1) {
    if (args[index] === "--check") check = true;
    else if (args[index] === "--root") root = path.resolve(args[++index]);
    else if (args[index] === "--out-dir") outputDirectory = path.resolve(args[++index]);
    else throw new Error(`Unknown argument: ${args[index]}`);
  }
  await writeDesignSystem({ root, check, outputDirectory: outputDirectory ?? root });
  process.stdout.write(`Design-system artifacts ${check ? "current" : "generated"}.\n`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === scriptPath) {
  main().catch((error) => { process.stderr.write(`${error.message}\n`); process.exitCode = 1; });
}
