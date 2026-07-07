// Operator display-density token, its persistence, and body application. Extracted
// so both the DensityToggle control and saved-layout restore share one source of
// truth for the `data-theme-density` scope and the `meridian.workstation.<feature>.vN`
// storage convention.
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

export function writeStoredDensity(
  value: Density,
  key: string = DENSITY_STORAGE_KEY,
  storage: Storage | null = getLocalStorage()
) {
  try {
    storage?.setItem(key, value);
  } catch {
    // Density persistence is best-effort when browser storage is blocked.
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
