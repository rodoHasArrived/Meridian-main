import assert from "node:assert/strict";
import test from "node:test";
import { compareAxeFindings, gatedFindingKeys, nextAxeBaseline } from "./smoke-workstation-a11y.mjs";

const finding = (routeId, ruleId, impact = "serious") => ({ routeId, ruleId, impact });

test("only serious and critical findings are gated, as unique route:rule keys", () => {
  assert.deepEqual(
    gatedFindingKeys([finding("today", "b"), finding("today", "b"), finding("today", "a", "critical"), finding("today", "c", "moderate")]),
    ["today:a", "today:b"]
  );
});

test("a finding outside the baseline is a regression; a baseline entry no longer found is an improvement", () => {
  const result = compareAxeFindings([finding("today", "new-rule")], { allowed: ["trading:old-rule"] });
  assert.deepEqual(result, { regressions: ["today:new-rule"], improvements: ["trading:old-rule"] });
});

test("the baseline seeds once, then only shrinks", () => {
  assert.deepEqual(nextAxeBaseline([finding("today", "a")], null), { allowed: ["today:a"] });
  assert.deepEqual(nextAxeBaseline([], { allowed: ["today:a"] }), { allowed: [] });
  assert.throws(() => nextAxeBaseline([finding("today", "b")], { allowed: ["today:a"] }), /new findings: today:b/);
});
