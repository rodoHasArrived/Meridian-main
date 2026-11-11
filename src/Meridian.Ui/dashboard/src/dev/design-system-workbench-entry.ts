/** The native preview is a fixture-development entry, never an application route. */
export const DESIGN_SYSTEM_WORKBENCH_PATH = "/workstation/__design-system";

export function isDesignSystemWorkbenchEntry(pathname: string, development: boolean, mode: string | undefined): boolean {
  return development && mode === "fixture-only"
    && pathname.replace(/\/$/, "") === DESIGN_SYSTEM_WORKBENCH_PATH;
}
