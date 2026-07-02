export type Appearance = "system" | "light" | "dark";

export const APPEARANCE_STORAGE_KEY = "meridian.workstation.appearance.v1";

export function isAppearance(value: unknown): value is Appearance {
  return value === "system" || value === "light" || value === "dark";
}

export function readStoredAppearance(storage: Storage | null = getLocalStorage()): Appearance {
  const stored = storage?.getItem(APPEARANCE_STORAGE_KEY);
  return isAppearance(stored) ? stored : "system";
}

export function writeStoredAppearance(appearance: Appearance, storage: Storage | null = getLocalStorage()) {
  storage?.setItem(APPEARANCE_STORAGE_KEY, appearance);
}

export function applyAppearance(
  appearance: Appearance,
  root: HTMLElement | null = typeof document === "undefined" ? null : document.documentElement
) {
  if (!root) {
    return;
  }

  if (appearance === "system") {
    root.removeAttribute("data-theme");
    return;
  }

  root.setAttribute("data-theme", appearance);
}

function getLocalStorage() {
  try {
    return typeof localStorage === "undefined" ? null : localStorage;
  } catch {
    return null;
  }
}
