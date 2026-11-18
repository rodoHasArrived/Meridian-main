// Accessibility checks for the browser workstation smoke (Phase 1, B13).
//
// - axe on a short list of task routes, with WCAG 2.0-2.2 A/AA tags. Serious and critical findings
//   fail unless the ratchet baseline already lists them; the baseline can only shrink.
// - Focus Not Obscured (WCAG 2.2 SC 2.4.11) on the reconciliation inspector, whose action row is sticky.
//
// Pure helpers are exported for scripts/smoke-workstation-a11y.test.mjs.

import fs from "node:fs/promises";

export const AXE_TAGS = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];
export const GATED_IMPACTS = new Set(["serious", "critical"]);

/** Task routes the smoke audits, relative to /workstation. */
export const A11Y_ROUTES = [
  { id: "today", path: "/" },
  { id: "accounting-reconciliation", path: "/accounting/reconciliation" },
  { id: "accounting-exceptions", path: "/accounting/exceptions" },
  { id: "accounting-ledger", path: "/accounting/ledger" },
  { id: "portfolio-nav", path: "/portfolio/nav" },
  { id: "trading", path: "/trading" }
];

export function findingKey(routeId, ruleId) {
  return `${routeId}:${ruleId}`;
}

/** Gated findings as sorted, unique `route:rule` keys. */
export function gatedFindingKeys(findings) {
  return [...new Set(findings
    .filter((finding) => GATED_IMPACTS.has(finding.impact))
    .map((finding) => findingKey(finding.routeId, finding.ruleId)))].sort();
}

/**
 * Compare gated findings with the baseline. A finding not in the baseline is a regression; a
 * baseline entry no longer found is an improvement that `--write-axe-baseline` locks in.
 */
export function compareAxeFindings(findings, baseline) {
  const current = new Set(gatedFindingKeys(findings));
  const allowed = new Set(baseline?.allowed ?? []);
  return {
    regressions: [...current].filter((key) => !allowed.has(key)).sort(),
    improvements: [...allowed].filter((key) => !current.has(key)).sort()
  };
}

/**
 * The baseline to write. Without an existing baseline it seeds from the current findings. With one,
 * it only removes entries; it refuses while there are regressions.
 */
export function nextAxeBaseline(findings, baseline) {
  if (!baseline) return { allowed: gatedFindingKeys(findings) };
  const { regressions } = compareAxeFindings(findings, baseline);
  if (regressions.length > 0) {
    throw new Error(`Refusing to write the axe baseline with new findings: ${regressions.join(", ")}`);
  }
  const current = new Set(gatedFindingKeys(findings));
  return { allowed: (baseline.allowed ?? []).filter((key) => current.has(key)).sort() };
}

export async function readAxeBaseline(file) {
  try {
    return JSON.parse(await fs.readFile(file, "utf8"));
  } catch (error) {
    if (error && error.code === "ENOENT") return null;
    throw error;
  }
}

export async function writeAxeBaseline(file, baseline) {
  const body = {
    description: "Serious and critical axe findings the workstation smoke still allows, as route:rule. It may only shrink (npm run smoke:workstation -- --write-axe-baseline).",
    allowed: baseline.allowed
  };
  await fs.writeFile(file, `${JSON.stringify(body, null, 2)}\n`, "utf8");
}

/** Run axe in the page and return flat findings for one route. */
export async function runAxe(page, axeSource, routeId) {
  await page.addScriptTag({ content: axeSource });
  const result = await page.evaluate(async (tags) => {
    const outcome = await window.axe.run(document, { preload: false, runOnly: { type: "tag", values: tags } });
    return outcome.violations.map((violation) => ({
      ruleId: violation.id,
      impact: violation.impact ?? "minor",
      help: violation.help,
      nodes: violation.nodes.slice(0, 5).map((node) => node.target.join(" "))
    }));
  }, AXE_TAGS);
  return result.map((finding) => ({ routeId, ...finding }));
}

/**
 * Focus each focusable control in the reconciliation inspector and report any whose centre is
 * covered by something else, such as the sticky action row or the bulk bar.
 */
export async function checkInspectorFocusNotObscured(page, timeoutMs) {
  const row = page.locator('[aria-label="Reconciliation break queue"] tr[data-dense-row-id]').first();
  await row.waitFor({ state: "visible", timeout: timeoutMs });
  await row.click();
  const inspector = page.locator(".workspace-workbench-shell-inspector").first();
  await inspector.waitFor({ state: "visible", timeout: timeoutMs });
  return page.evaluate(() => {
    const container = document.querySelector(".workspace-workbench-shell-inspector");
    if (!container) return { checked: 0, obscured: ["inspector not found"] };
    const selector = "a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), summary, [tabindex]:not([tabindex='-1'])";
    const controls = [...container.querySelectorAll(selector)]
      .filter((element) => element.getClientRects().length > 0)
      .slice(0, 30);
    const obscured = [];
    for (const element of controls) {
      element.focus();
      const box = element.getBoundingClientRect();
      if (box.width === 0 || box.height === 0) continue;
      const hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
      if (!hit || (hit !== element && !element.contains(hit))) {
        const name = element.getAttribute("aria-label") || element.textContent?.trim().slice(0, 40) || element.tagName;
        obscured.push(`${element.tagName.toLowerCase()} "${name}" is covered by ${hit ? hit.tagName.toLowerCase() + (hit.className ? "." + String(hit.className).split(" ")[0] : "") : "nothing (off screen)"}`);
      }
    }
    return { checked: controls.length, obscured };
  });
}
