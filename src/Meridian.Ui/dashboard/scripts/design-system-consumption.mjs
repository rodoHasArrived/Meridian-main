#!/usr/bin/env node
/** Static symbol reachability is integration evidence, never rendered acceptance. */
import fs from "node:fs";
import process from "node:process";
import console from "node:console";
import path from "node:path";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const dashboard = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repository = path.resolve(dashboard, "../../..");
const packageRoot = path.join(repository, "Meridian Design System");
const outputPath = path.join(packageRoot, "design-system-consumption.json");
const runtimeSource = (filename) => !/\.(?:test|spec)\.[cm]?[jt]sx?$/.test(filename) && !/[\\/](?:__tests__|test)[\\/]/.test(filename);
const posix = (filename) => filename.split(path.sep).join("/");

export function analyzeReachability(program, entryPath, sourceRoot) {
  const checker = program.getTypeChecker();
  const normalizedRoot = posix(path.resolve(sourceRoot)) + "/";
  const inside = (file) => posix(file.fileName).startsWith(normalizedRoot) && runtimeSource(file.fileName);
  const key = (node) => `${node.getSourceFile().fileName}:${node.pos}:${node.end}`;
  const reached = new Map();
  const references = new Map();
  const queue = [];
  const unwrap = (symbol) => {
    const seen = new Set();
    while (symbol && (symbol.flags & ts.SymbolFlags.Alias) && !seen.has(symbol)) {
      seen.add(symbol); symbol = checker.getAliasedSymbol(symbol);
    }
    return symbol;
  };
  function eligible(node) {
    return ts.isFunctionDeclaration(node) || ts.isVariableDeclaration(node) || ts.isClassDeclaration(node) || ts.isEnumDeclaration(node);
  }
  function enqueue(node) {
    if (!inside(node.getSourceFile()) || reached.has(key(node))) return;
    reached.set(key(node), node); queue.push(node);
  }
  const entry = program.getSourceFile(entryPath);
  if (!entry) throw new Error(`Missing runtime entry: ${entryPath}`);
  enqueue(entry);
  for (let index = 0; index < queue.length; index++) {
    const caller = queue[index];
    function visit(node) {
      if (ts.isTypeNode(node) || ts.isImportDeclaration(node) || ts.isExportDeclaration(node) || ts.isInterfaceDeclaration(node) || ts.isTypeAliasDeclaration(node)) return;
      if (ts.isIdentifier(node)) {
        const symbol = unwrap(checker.getSymbolAtLocation(node));
        for (const declaration of symbol?.declarations ?? []) {
          if (!eligible(declaration) || !inside(declaration.getSourceFile())) continue;
          // A symbol's declaration is not a caller of itself.
          if (node === declaration.name || declaration === caller) continue;
          const location = node.getSourceFile().getLineAndCharacterOfPosition(node.getStart());
          const callers = references.get(key(declaration)) ?? new Map();
          callers.set(`${node.getSourceFile().fileName}:${node.pos}`, {
            source: node.getSourceFile().fileName,
            line: location.line + 1
          });
          references.set(key(declaration), callers);
          enqueue(declaration);
        }
      }
      ts.forEachChild(node, visit);
    }
    visit(caller);
  }
  function exportedDeclarations(file, exportName) {
    const source = program.getSourceFile(file);
    if (!source) return [];
    const module = checker.getSymbolAtLocation(source);
    if (!module) return [];
    const symbol = unwrap(checker.getExportsOfModule(module).find((item) => item.name === exportName));
    return (symbol?.declarations ?? []).filter(eligible);
  }
  return {
    candidates(exportName) {
      const found = [];
      for (const source of program.getSourceFiles()) {
        if (!inside(source) || !/[\\/](?:components|design-system)[\\/]/.test(source.fileName)) continue;
        for (const declaration of exportedDeclarations(source.fileName, exportName)) {
          const identity = key(declaration);
          if (found.some((item) => item.identity === identity)) continue;
          found.push({
            identity,
            symbol: exportName,
            source: declaration.getSourceFile().fileName,
            reachable: reached.has(identity),
            consumers: [...(references.get(identity)?.values() ?? [])]
          });
        }
      }
      return found;
    }
  };
}

const aliases = {
  Badge: ["Badge", "DesignSystemBadge"],
  Button: ["Button", "DesignSystemButton"],
  NavRail: ["DesignSystemNavRail", "WorkspaceNav"],
  WorkstationTopbar: ["DesignSystemMasthead", "WorkstationTopbar"],
  StatusBar: ["WorkstationStatusBar"],
  TrustStrip: ["TrustStrip", "DesignSystemTrustStrip"],
  TrialBalance: ["TrialBalanceTable"],
  ReconciliationPanel: ["ReconciliationPanel", "ReconciliationComparisonPanel"],
  Toolbar: ["Toolbar", "ToolbarStrip"],
  Tabs: ["Tabs", "WorkspaceTabStrip"],
  TabPanel: ["TabPanel", "WorkspaceTabPanel"],
  Drawer: ["Drawer", "Sheet"],
  DrawerHeader: ["DrawerHeader", "SheetHeader"],
  DrawerBody: ["DrawerBody", "SheetContent"],
  Modal: ["Modal", "Dialog"],
  ModalHeader: ["ModalHeader", "DialogHeader"],
  ModalBody: ["ModalBody", "DialogContent"],
  ModalFooter: ["ModalFooter", "DialogFooter"],
  ModalForm: ["ModalForm"],
  ChartCursorSync: ["ChartSyncProvider"],
  ChartCursor: ["useChartSync", "useChartCrosshairSync"]
};
const differences = {
  DenseDataTable: "The native adapter caps rich rows at 100 with Show all and uses explicit virtualization; the reference's automatic virtualization is not a product requirement.",
  AmountCell: "The native adapter exposes only options its callers use; reference ISO-decimal and Dr/Cr modes are not implied by shared naming.",
  Toast: "Native notifications expose current workflow callbacks; reference-only Undo capability is not automatically integrated.",
  ToastProvider: "Native notifications expose current workflow callbacks; reference-only Undo capability is not automatically integrated.",
  WorkstationTopbar: "The shell renders DesignSystemMasthead directly; WorkstationTopbar is a compatibility wrapper.",
  NavRail: "WorkspaceNav and DesignSystemNavRail implement the browser's seven-workspace navigation over the reference visual contract."
};

export function createConsumptionInventory() {
  const configPath = path.join(dashboard, "tsconfig.json");
  const config = ts.readConfigFile(configPath, ts.sys.readFile);
  if (config.error) throw new Error(ts.flattenDiagnosticMessageText(config.error.messageText, "\n"));
  const parsed = ts.parseJsonConfigFileContent(config.config, ts.sys, dashboard);
  const program = ts.createProgram(parsed.fileNames.filter(runtimeSource), parsed.options);
  const graph = analyzeReachability(program, path.join(dashboard, "src/main.tsx"), path.join(dashboard, "src"));
  const manifest = JSON.parse(fs.readFileSync(path.join(packageRoot, "_ds_manifest.json"), "utf8"));
  const records = manifest.components.map((component) => {
    const symbols = aliases[component.name] ?? [component.name];
    const candidates = symbols.flatMap((symbol) => graph.candidates(symbol));
    const seen = new Set();
    const adapters = candidates.filter((item) => {
      if (seen.has(item.identity)) return false; seen.add(item.identity); return true;
    }).map(({ source, symbol, reachable, consumers }) => ({
      source: posix(path.relative(repository, source)), symbol, reachableFromBrowserEntry: reachable,
      consumers: consumers.map((item) => ({ source: posix(path.relative(repository, item.source)), line: item.line }))
        .sort((a, b) => a.source.localeCompare(b.source) || a.line - b.line)
    })).sort((a, b) => a.source.localeCompare(b.source) || a.symbol.localeCompare(b.symbol));
    const integrated = adapters.some((adapter) => adapter.reachableFromBrowserEntry);
    return {
      component: component.name,
      referenceSource: component.sourcePath,
      classification: integrated ? "route-integrated" : adapters.length ? "available-adapter" : "reference-only",
      nativeAdapters: adapters,
      ...(differences[component.name] ? { intentionalDivergence: differences[component.name] } : {})
    };
  }).sort((a, b) => a.component.localeCompare(b.component));
  return {
    schemaVersion: 1,
    namespace: manifest.namespace,
    evidence: "Static TypeScript symbol reachability from browser main.tsx, including lazy route exports. This establishes code integration, not rendered behavior or API equivalence. Browser acceptance is separate.",
    mappingPolicy: "Exact exported component names plus explicit reviewed aliases; helpers carried only on the package namespace remain reference-only unless mapped. Imports, barrel exports, tests, and metadata alone are not integration evidence.",
    classifications: {
      "route-integrated": "A native adapter symbol is reached from the browser entry through runtime references.",
      "available-adapter": "A native adapter is exported but has no runtime reference reachable from the browser entry.",
      "reference-only": "No reviewed native adapter symbol exists; the component remains available in the reference package."
    },
    components: records
  };
}

function main() {
  const args = new Set(process.argv.slice(2));
  if ([...args].some((argument) => !["--check", "--write", "--json"].includes(argument))) throw new Error("Use --check, --write, or --json");
  const inventory = createConsumptionInventory();
  const rendered = `${JSON.stringify(inventory, null, 2)}\n`;
  if (args.has("--json")) { process.stdout.write(rendered); return; }
  if (args.has("--write")) fs.writeFileSync(outputPath, rendered);
  else if (!fs.existsSync(outputPath) || fs.readFileSync(outputPath, "utf8") !== rendered) {
    throw new Error("Design-system consumption inventory is stale; run design-system-consumption.mjs --write");
  }
  const counts = Object.fromEntries(Object.keys(inventory.classifications).map((value) => [value, inventory.components.filter((item) => item.classification === value).length]));
  console.log(`Design-system consumption: ${inventory.components.length} references; ${JSON.stringify(counts)}`);
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try { main(); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
