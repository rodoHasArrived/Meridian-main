import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { fileURLToPath } from "node:url";
import ts from "typescript";

const dashboard = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const baselinePath = path.join(dashboard, "scripts/workstation-governance-baseline.json");
const statementModules = new Set([
  "src/screens/reporting-income-comparison.tsx",
  "src/screens/reporting-retained-amounts.tsx",
  "src/screens/reporting-screen.report-writer.tsx",
  "src/screens/accounting-screen.factor-paydown-panel.tsx",
  "src/screens/accounting-screen.consolidation-results.tsx",
  "src/screens/accounting-screen.onboarding-panel.tsx",
  "src/screens/statement-import-preview.tsx"
]);
const statementOwners = {
  "src/screens/data-screen.corporate-action-inbox.tsx": {
    BasisComparison: "Financial basis treatment matrix; its columns retain accounting meaning.",
    JournalPreview: "Balanced debit/credit posting preview; retains its financial statement layout."
  },
  "src/screens/accounting-screen.prepare-next-period-calendar.tsx": {
    ClosePreparationCalendarForm: "Editable deadline-rule worksheet, not a record browse table."
  }
};
const statementTables = [
  { file: "src/screens/accounting-screen.manual-journal-panel.tsx", attribute: "className", value: "accounting-journal-table",
    reason: "Editable journal source lines and financial balance-impact preview preserve debit/credit layout." },
  { file: "src/screens/accounting-screen.close-cockpit-panels.tsx", attribute: "aria-label", value: "Scoped temporary-account balances",
    reason: "Financial closing-entry balance statement bound to the exact close preview." }
];

function approvedStatement(filename, opening) {
  return statementTables.some(rule => rule.file === filename && opening.attributes.properties.some(attribute =>
    ts.isJsxAttribute(attribute) && attribute.name.getText() === rule.attribute &&
    attribute.initializer && ts.isStringLiteral(attribute.initializer) && attribute.initializer.text === rule.value));
}

function ownerName(node) {
  for (let parent = node.parent; parent; parent = parent.parent) {
    if (ts.isFunctionDeclaration(parent) && parent.name) return parent.name.text;
    if (ts.isVariableDeclaration(parent) && ts.isIdentifier(parent.name)) return parent.name.text;
  }
  return "module";
}

function digest(value) { return crypto.createHash("sha256").update(value.replace(/\s+/g, " ").trim()).digest("hex").slice(0, 16); }

/** Inspect JSX display code, not formatter implementations, prose, IDs or chart calculations. */
export function scanSource(filename, text) {
  const source = ts.createSourceFile(filename, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const violations = [];
  function add(rule, node, identity) {
    violations.push({ id: `${rule}:${filename}:${ownerName(node)}:${digest(identity)}`,
      rule, file: filename, line: source.getLineAndCharacterOfPosition(node.getStart(source)).line + 1 });
  }
  function visit(node) {
    if (ts.isJsxElement(node)) {
      const name = node.openingElement.tagName.getText(source);
      if (name === "table" && !statementModules.has(filename) && !statementOwners[filename]?.[ownerName(node)]
        && !approvedStatement(filename, node.openingElement)) {
        // Header and accessible name identify a table independently of line shifts and row data.
        const headings = node.children.filter(child => ts.isJsxElement(child) && child.openingElement.tagName.getText(source) === "thead");
        add("rawBrowseTable", node, node.openingElement.getText(source) + headings.map(header => header.getText(source)).join(""));
      }
      if (name === "Badge") {
        const label = node.children.filter(ts.isJsxText).map(child => child.text).join(" ").trim();
        if (/^(ready|review|blocked|unavailable|awaiting approval|needs review|failed|healthy|stale)$/i.test(label))
          add("adHocStatusBadge", node, node.getText(source));
      }
    }
    if (ts.isCallExpression(node) && ts.isPropertyAccessExpression(node.expression)
      && ["toFixed", "toLocaleString"].includes(node.expression.name.text)) {
      let inDisplay = false;
      for (let parent = node.parent; parent; parent = parent.parent) {
        if (ts.isJsxExpression(parent)) { inDisplay = true; break; }
        if (ts.isFunctionDeclaration(parent)) break;
      }
      if (inDisplay) add("localDisplayFormatter", node, node.getText(source));
    }
    ts.forEachChild(node, visit);
  }
  visit(source);
  return violations;
}

function walk(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const candidate = path.join(directory, entry.name);
    if (entry.isDirectory()) return walk(candidate);
    return entry.name.endsWith(".tsx") && !/\.test\./.test(entry.name) ? [candidate] : [];
  });
}

export function currentViolations() {
  return walk(path.join(dashboard, "src/screens")).flatMap(file =>
    scanSource(path.relative(dashboard, file).split(path.sep).join("/"), fs.readFileSync(file, "utf8")));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const current = currentViolations();
  if (process.argv.includes("--inventory")) {
    process.stdout.write(JSON.stringify({ version: 1, statementModules: [...statementModules], statementOwners, statementTables,
      legacyViolationIds: [...new Set(current.map(item => item.id))].sort() }, null, 2) + "\n");
  } else {
    const baseline = JSON.parse(fs.readFileSync(baselinePath, "utf8"));
    const allowed = new Set(baseline.legacyViolationIds);
    const introduced = current.filter(item => !allowed.has(item.id));
    for (const item of introduced) process.stderr.write(`${item.file}:${item.line} ${item.rule}: use shared workstation primitives.\n`);
    const counts = Object.fromEntries(["rawBrowseTable", "adHocStatusBadge", "localDisplayFormatter"].map(rule => [rule, current.filter(item => item.rule === rule).length]));
    process.stdout.write(JSON.stringify({ status: introduced.length ? "failed" : "passed", remaining: counts, newViolations: introduced.length }) + "\n");
    process.exitCode = introduced.length ? 1 : 0;
  }
}
