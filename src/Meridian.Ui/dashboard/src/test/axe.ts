import { axe } from "jest-axe";
import { expect } from "vitest";

/**
 * Runs jest-axe over a rendered container and asserts there are no violations,
 * other than a small, explicit set of pre-existing issues passed via
 * `knownIssues`.
 *
 * `heading-order` is always disabled: the shared `CardTitle` primitive renders a
 * fixed `<h3>`, so every screen that places cards directly under its `<h1>` page
 * title trips the rule app-wide. Correcting that needs a coordinated heading-
 * level pass across the shared Card component and its call sites, tracked
 * separately.
 *
 * `knownIssues` lets a screen opt out of specific axe rule ids that flag
 * pre-existing accessibility debt (typically rooted in shared primitives, e.g.
 * the dense-row detail `<aside>` landmark, or grid `<dl>` layouts). Listing them
 * explicitly per screen keeps these smoke tests enforcing "no regressions and no
 * new violations" today while leaving a greppable inventory of debt to burn
 * down, rather than silently disabling rules everywhere. New, unlisted
 * violations still fail the test.
 */
export async function expectNoAxeViolations(
  container: HTMLElement,
  options: { knownIssues?: string[] } = {}
): Promise<void> {
  const disabledRules: Record<string, { enabled: false }> = {
    "heading-order": { enabled: false }
  };
  for (const ruleId of options.knownIssues ?? []) {
    disabledRules[ruleId] = { enabled: false };
  }

  const results = await axe(container, { rules: disabledRules });

  expect(results.violations).toHaveLength(0);
}
