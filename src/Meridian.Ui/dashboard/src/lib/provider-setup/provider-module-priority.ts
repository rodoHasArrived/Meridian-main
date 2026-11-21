// Provider module priority: the one rule both edit paths share.
//
// The full-form path (`EditProviderDrawer`) edits priority in a number input with `min={1}` and
// `max={999}`, whose native validation admits whole numbers in that range. The inline priority
// cell on the provider card applies the same range, in canonical form: no sign, no decimals, no
// exponent and no leading zeros, so "100" and "0100" cannot become two different writes.

export const PROVIDER_MODULE_PRIORITY_MIN = 1;
export const PROVIDER_MODULE_PRIORITY_MAX = 999;

export const PROVIDER_MODULE_PRIORITY_RULE =
  `Enter a whole number from ${PROVIDER_MODULE_PRIORITY_MIN} to ${PROVIDER_MODULE_PRIORITY_MAX}.`;

/** The priority a draft stands for, or `null` when the draft is not an acceptable priority. */
export function parseProviderModulePriority(draft: unknown): number | null {
  const text = typeof draft === "number" ? String(draft) : typeof draft === "string" ? draft.trim() : "";
  if (!/^[1-9][0-9]{0,2}$/.test(text)) {
    return null;
  }
  const priority = Number(text);
  return priority >= PROVIDER_MODULE_PRIORITY_MIN && priority <= PROVIDER_MODULE_PRIORITY_MAX ? priority : null;
}

/** Validator in the EditableCell / OptimisticCell shape: `true`, or the message to show. */
export function validateProviderModulePriority(draft: string): true | string {
  return parseProviderModulePriority(draft) === null ? PROVIDER_MODULE_PRIORITY_RULE : true;
}
