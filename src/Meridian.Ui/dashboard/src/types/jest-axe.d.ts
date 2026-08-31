/**
 * Type declarations for `jest-axe`, which ships none of its own as of v10.
 *
 * `@types/jest-axe` is deliberately not used. It depends on `@types/jest`, which would pull the
 * entire Jest global type surface into a Vitest project — competing with `vitest/globals` for
 * `expect`, `describe` and friends — and it carries a second, older `axe-core` (v3) alongside the
 * v4 that `jest-axe` actually runs. Installing it added roughly 600 lines of lockfile for a package
 * whose types would then disagree with the runtime. Declaring the two members this repository
 * imports costs nothing and stays accurate.
 *
 * Result and option shapes are taken from `axe-core` rather than restated here, so they cannot
 * drift from what `axe()` really returns. `jest-axe` pins `axe-core` to an exact version, and it is
 * declared as a direct devDependency at that same version so this import resolves through a
 * dependency this package actually owns rather than a hoisted transitive one.
 */
declare module "jest-axe" {
  import type { AxeResults, ElementContext, RunOptions } from "axe-core";

  export function axe(container: ElementContext, options?: RunOptions): Promise<AxeResults>;

  /**
   * The matcher object passed to `expect.extend` in `src/test/setup.ts`. Registered but not
   * currently invoked anywhere — every accessibility test asserts on `results.violations`
   * directly — so no `expect(...).toHaveNoViolations()` assertion augmentation is declared. Add one
   * here alongside the first test that calls the matcher.
   */
  export const toHaveNoViolations: {
    toHaveNoViolations(results: AxeResults): { pass: boolean; message: () => string };
  };
}
