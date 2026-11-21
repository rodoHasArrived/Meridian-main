import { isAppearance, type Appearance } from "@/lib/theme";
import { isDensity, type Density } from "@/lib/density";

export type WorkbenchDataState = "populated" | "loading" | "empty" | "error";
export type WorkbenchButtonState = "normal" | "disabled" | "busy";
export type WorkbenchSurface = "inherited" | "light";

export interface WorkbenchOptions {
  theme: Appearance;
  density: Density;
  surface: WorkbenchSurface;
  data: WorkbenchDataState;
  buttons: WorkbenchButtonState;
}

export const DEFAULT_WORKBENCH_OPTIONS: Readonly<WorkbenchOptions> = {
  theme: "light", density: "default", surface: "inherited", data: "populated", buttons: "normal"
};

function choice<T extends string>(value: string | null, choices: readonly T[], fallback: T): T {
  return choices.includes(value as T) ? value as T : fallback;
}

export function readWorkbenchOptions(search: URLSearchParams): WorkbenchOptions {
  const theme = search.get("theme");
  const density = search.get("density");
  return {
    theme: isAppearance(theme) ? theme : DEFAULT_WORKBENCH_OPTIONS.theme,
    density: isDensity(density) ? density : DEFAULT_WORKBENCH_OPTIONS.density,
    surface: choice(search.get("surface"), ["inherited", "light"], DEFAULT_WORKBENCH_OPTIONS.surface),
    data: choice(search.get("data"), ["populated", "loading", "empty", "error"], DEFAULT_WORKBENCH_OPTIONS.data),
    buttons: choice(search.get("buttons"), ["normal", "disabled", "busy"], DEFAULT_WORKBENCH_OPTIONS.buttons)
  };
}

/** Only preview options go in the URL; no operator storage or application scope is used. */
export function writeWorkbenchOptions(options: WorkbenchOptions): URLSearchParams {
  const search = new URLSearchParams();
  for (const key of Object.keys(DEFAULT_WORKBENCH_OPTIONS) as Array<keyof WorkbenchOptions>) {
    if (options[key] !== DEFAULT_WORKBENCH_OPTIONS[key]) search.set(key, options[key]);
  }
  return search;
}
