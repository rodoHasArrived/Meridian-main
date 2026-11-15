// Fixture and internal-tool copy stays out of the operator's reading path.
//
// Development fixtures and screenshot fixtures describe themselves ("Adapter wiring from screenshot
// fixtures."). That text is useful while building against mock data and is noise, or a false
// statement, anywhere else. `operatorNote` passes ordinary notes through and drops
// self-describing fixture text unless the mock-data build is active.

/** True when this build serves development fixtures in place of a Meridian host. */
export function isMockDataBuild(): boolean {
  return Boolean(import.meta.env.DEV) && !import.meta.env.VITE_MERIDIAN_DEV_MODE;
}

const INTERNAL_COPY = /\b(?:fixtures?|screenshot|mock data|no-host|adapter wiring|wiring preview|development preview)\b/i;

/** True for text that describes the build's own scaffolding rather than the operator's data. */
export function isInternalCopy(text: string | null | undefined): boolean {
  return Boolean(text && INTERNAL_COPY.test(text));
}

/** A note fit for an operator: `null` for blank text and for fixture copy outside the mock-data build. */
export function operatorNote(text: string | null | undefined, mockDataBuild: boolean = isMockDataBuild()): string | null {
  const trimmed = text?.trim();
  if (!trimmed) {
    return null;
  }
  return isInternalCopy(trimmed) && !mockDataBuild ? null : trimmed;
}
