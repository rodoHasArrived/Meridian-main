#!/usr/bin/env node

import { createHash } from "node:crypto";
import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const packageRoot = path.resolve(scriptDir, "..");
const repoRoot = path.resolve(packageRoot, "..");
const bundlePath = path.join(packageRoot, "_ds_bundle.js");
const manifestPath = path.join(packageRoot, "_ds_manifest.json");
const require = createRequire(import.meta.url);
const ts = require(path.join(repoRoot, "src/Meridian.Ui/dashboard/node_modules/typescript"));
const checkOnly = process.argv.includes("--check");

function sourceHash(content) {
  return createHash("sha256").update(content).digest("hex").slice(0, 12);
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function compileModule(sourcePath, source) {
  const compiled = ts.transpileModule(source, {
    fileName: sourcePath,
    compilerOptions: {
      allowJs: true,
      esModuleInterop: true,
      jsx: ts.JsxEmit.React,
      module: ts.ModuleKind.CommonJS,
      target: ts.ScriptTarget.ES2020
    }
  }).outputText.trim();

  return [
    `// ${sourcePath}`,
    "try { (() => {",
    "const __ds_module = { exports: {} };",
    "const module = __ds_module;",
    "const exports = __ds_module.exports;",
    "const require = (specifier) => {",
    "  if (specifier === \"react\") return { ...React, default: React };",
    "  if (specifier.startsWith(\".\")) return __ds_scope;",
    "  throw new Error(`Unsupported design-system module import: ${specifier}`);",
    "};",
    compiled,
    "Object.assign(__ds_scope, __ds_module.exports);",
    `})(); } catch (e) { __ds_ns.__errors.push({ path: "${sourcePath}", error: String((e && e.message) || e) }); }`
  ].join("\n");
}

function refreshBundle(currentBundle) {
  const firstLineEnd = currentBundle.indexOf("\n");
  const headerLine = currentBundle.slice(0, firstLineEnd);
  const match = headerLine.match(/^\/\* @ds-bundle: (.*) \*\/$/);
  if (!match) throw new Error("Design-system bundle metadata header is missing or malformed.");

  const metadata = JSON.parse(match[1]);
  let nextBundle = currentBundle;
  const driftedSources = [];

  for (const [sourcePath, recordedHash] of Object.entries(metadata.sourceHashes ?? {})) {
    const absoluteSourcePath = path.join(packageRoot, sourcePath);
    if (!fs.existsSync(absoluteSourcePath)) continue;
    const source = fs.readFileSync(absoluteSourcePath, "utf8");
    const currentHash = sourceHash(source);
    if (currentHash === recordedHash) continue;

    const escapedPath = escapeRegExp(sourcePath);
    const modulePattern = new RegExp(
      `// ${escapedPath}\\ntry \\{ \\(\\(\\) => \\{[\\s\\S]*?\\n\\}\\)\\(\\); \\} catch \\(e\\) \\{ __ds_ns\\.__errors\\.push\\(\\{ path: \"${escapedPath}\", error: String\\(\\(e && e\\.message\\) \\|\\| e\\) \\}\\); \\}`
    );
    if (!modulePattern.test(nextBundle)) {
      throw new Error(`Could not locate compiled module block for ${sourcePath}.`);
    }

    nextBundle = nextBundle.replace(modulePattern, compileModule(sourcePath, source));
    metadata.sourceHashes[sourcePath] = currentHash;
    driftedSources.push(sourcePath);
  }

  const nextHeader = `/* @ds-bundle: ${JSON.stringify(metadata)} */`;
  nextBundle = `${nextHeader}${nextBundle.slice(firstLineEnd)}`;
  return { content: nextBundle, driftedSources };
}

function cssRules(css) {
  return [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map((match) => ({
    selector: match[1].replace(/\/\*[\s\S]*?\*\//g, "").trim(),
    body: match[2]
  }));
}

function tokenValue(rules, token) {
  const declaration = new RegExp(`${escapeRegExp(token.name)}\\s*:\\s*([^;]+);`);
  const candidates = token.scope
    ? rules.filter((rule) => rule.selector.split(",").map((value) => value.trim()).includes(token.scope))
    : rules.filter((rule) => rule.selector.split(",").map((value) => value.trim()).includes(":root"));
  for (const candidate of candidates) {
    const match = candidate.body.match(declaration);
    if (match) return match[1].trim();
  }
  return null;
}

function refreshManifest(currentManifest) {
  const manifest = JSON.parse(currentManifest);
  const cssCache = new Map();
  const refreshedTokenFiles = new Set(["tokens/colors.css", "tokens/theme.css"]);

  for (const token of manifest.tokens ?? []) {
    if (!refreshedTokenFiles.has(token.definedIn)) continue;
    if (!cssCache.has(token.definedIn)) {
      const css = fs.readFileSync(path.join(packageRoot, token.definedIn), "utf8");
      cssCache.set(token.definedIn, cssRules(css));
    }
    const value = tokenValue(cssCache.get(token.definedIn), token);
    if (value !== null) token.value = value;
  }

  for (const card of manifest.cards ?? []) {
    if (!card.path) continue;
    const cardPath = path.join(packageRoot, card.path);
    if (!fs.existsSync(cardPath)) continue;
    const firstLine = fs.readFileSync(cardPath, "utf8").split(/\r?\n/, 1)[0];
    const annotation = firstLine.match(/<!--\s*@dsCard\s+([\s\S]*?)\s*-->/);
    if (!annotation) continue;
    const attributes = Object.fromEntries(
      [...annotation[1].matchAll(/([A-Za-z][A-Za-z0-9]*)="([^"]*)"/g)]
        .map((match) => [match[1], match[2]])
    );
    for (const field of ["group", "viewport", "subtitle", "name"]) {
      if (attributes[field] !== undefined) card[field] = attributes[field];
    }
  }

  return JSON.stringify(manifest);
}

const currentBundle = fs.readFileSync(bundlePath, "utf8");
const currentManifest = fs.readFileSync(manifestPath, "utf8");
const bundleResult = refreshBundle(currentBundle);
const nextManifest = refreshManifest(currentManifest);
const stale = bundleResult.content !== currentBundle || nextManifest !== currentManifest;

if (checkOnly) {
  if (stale) {
    console.error("Design-system generated artifacts are stale. Run: node \"Meridian Design System/scripts/sync_generated_artifacts.mjs\"");
    process.exitCode = 1;
  } else {
    console.log("Design-system generated artifacts are current.");
  }
} else {
  fs.writeFileSync(bundlePath, bundleResult.content);
  fs.writeFileSync(manifestPath, nextManifest);
  console.log(`Synchronized design-system artifacts (${bundleResult.driftedSources.length} compiled module(s)).`);
}
