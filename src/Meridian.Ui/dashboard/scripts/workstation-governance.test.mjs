import { test } from "node:test";
import assert from "node:assert/strict";
import { scanSource } from "./workstation-governance.mjs";

test("identifies new raw browse tables with stable identities across line shifts", () => {
  const source = 'function Queue(){return <table aria-label="Orders"><thead><tr><th>Order</th></tr></thead></table>}';
  const [first] = scanSource("src/screens/queue.tsx", source);
  assert.equal(first.rule, "rawBrowseTable");
  assert.equal(scanSource("src/screens/queue.tsx", "\n\n" + source)[0].id, first.id);
  assert.notEqual(scanSource("src/screens/queue.tsx", source.replace('"Orders"', '"Fills"'))[0].id, first.id);
});
test("permits declared statement and editing modules without exempting new browse owners", () => {
  const source = 'function Statement(){return <table><tbody /></table>}';
  assert.equal(scanSource("src/screens/reporting-screen.report-writer.tsx", source).length, 0);
  assert.equal(scanSource("src/screens/new-queue.tsx", source).length, 1);
});
test("checks formatting only in display expressions and status only in status badges", () => {
  const source = 'function View(){const calculated=value.toFixed(2); return <div><span>{value.toFixed(2)}</span><Badge>Ready</Badge><Badge>Detail</Badge><code>entity-fund-alpha</code></div>}';
  assert.deepEqual(scanSource("src/screens/view.tsx", source).map(item => item.rule), ["localDisplayFormatter", "adHocStatusBadge"]);
});
test("worksheet exceptions do not exempt browse tables elsewhere in the same owner file", () => {
  const file = "src/screens/accounting-screen.tsx";
  assert.equal(scanSource(file, 'function Editor(){return <table className="accounting-journal-table"><tbody /></table>}').length, 0);
  assert.equal(scanSource(file, 'function Queue(){return <table aria-label="New records"><tbody /></table>}').length, 1);
});
