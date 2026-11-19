// Operator display-density token, its persistence, and body application. Extracted
// so both the DensityToggle control and saved-layout restore share one source of
// truth for the `data-theme-density` scope and the `meridian.workstation.<feature>.vN`
// storage convention.
import { notifyOperatorPreferenceChanged } from "@/lib/operator-preference-events";
import { getLocalStorage } from "@/lib/theme";

export type Density = "compact" | "default" | "spacious";

export const DENSITY_STORAGE_KEY = "meridian.workstation.density.v1";
export const DEFAULT_DENSITY: Density = "default";

export function isDensity(value: unknown): value is Density {
  return value === "compact" || value === "default" || value === "spacious";
}

export function readStoredDensity(
  key: string = DENSITY_STORAGE_KEY,
  storage: Storage | null = getLocalStorage()
): Density {
  try {
    const stored = storage?.getItem(key);
    return isDensity(stored) ? stored : DEFAULT_DENSITY;
  } catch {
    return DEFAULT_DENSITY;
  }
}

// A density applied for this page session only (a restored saved layout). It is never written to
// browser storage or announced, so it can't reach the host as the operator's preference. Any write
// of the stored preference (an operator choice or a host sync) replaces it.
let sessionDensity: Density | null = null;

/** Applies a density for this page session without changing the operator's stored preference. */
export function applySessionDensity(
  value: Density,
  el: HTMLElement | null = typeof document === "undefined" ? null : document.body
) {
  sessionDensity = value;
  applyDensity(value, el);
}

/** The density on screen: a session override when one is active, otherwise the stored preference. */
export function readEffectiveDensity(storage: Storage | null = getLocalStorage()): Density {
  return sessionDensity ?? readStoredDensity(DENSITY_STORAGE_KEY, storage);
}

/** Drops the session override; the stored preference applies again. */
export function clearSessionDensity() {
  sessionDensity = null;
}

export function writeStoredDensity(
  value: Density,
  key: string = DENSITY_STORAGE_KEY,
  storage: Storage | null = getLocalStorage(),
  announce = true
) {
  if (key === DENSITY_STORAGE_KEY) {
    sessionDensity = null;
  }
  try {
    storage?.setItem(key, value);
  } catch {
    // Density persistence is best-effort when browser storage is blocked.
  }
  if (announce && key === DENSITY_STORAGE_KEY) {
    notifyOperatorPreferenceChanged();
  }
}

/**
 * Apply a density to an element via the `data-theme-density` scope. `default` clears
 * the attribute (the baseline token set), matching the DensityToggle contract.
 */
export function applyDensity(
  value: Density,
  el: HTMLElement | null = typeof document === "undefined" ? null : document.body
) {
  if (!el) {
    return;
  }
  if (value === "default") {
    el.removeAttribute("data-theme-density");
  } else {
    el.setAttribute("data-theme-density", value);
  }
}
