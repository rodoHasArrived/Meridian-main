import { act, screen, within } from "@testing-library/react";
import type userEvent from "@testing-library/user-event";
import { expect, vi } from "vitest";

type User = ReturnType<typeof userEvent.setup>;

/**
 * Widest viewport at which a switchable workbench shows its queue and inspector one at a
 * time (see `workspace-workbench-shell.css`).
 */
export const NARROW_WORKBENCH_MAX_WIDTH = 1279;

/**
 * jsdom 31+ evaluates `@media` rules against a fixed 1024x768 viewport and has no
 * `matchMedia`. Answer width queries against that same viewport so components that consult
 * `matchMedia` agree with the CSS the test renders.
 */
export function viewportMatchMedia(query: string): MediaQueryList {
  const width = window.innerWidth;
  const max = /max-width:\s*(\d+)px/.exec(query);
  const min = /min-width:\s*(\d+)px/.exec(query);
  const matches = (!max || width <= Number(max[1])) && (!min || width >= Number(min[1]));
  return {
    matches,
    media: query,
    onchange: null,
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    addListener: vi.fn(),
    removeListener: vi.fn(),
    dispatchEvent: vi.fn(() => false)
  };
}

/** Call from `beforeEach`; pair with `vi.unstubAllGlobals()` in `afterEach`. */
export function installViewportMatchMedia() {
  vi.stubGlobal("matchMedia", vi.fn(viewportMatchMedia));
}

/** Guards against a future jsdom viewport change silently returning tests to the wide layout. */
export function expectNarrowWorkbenchViewport() {
  expect(window.innerWidth).toBeLessThanOrEqual(NARROW_WORKBENCH_MAX_WIDTH);
}

function workbenchFor(scope?: HTMLElement): HTMLElement | Document {
  if (!scope) return document;
  const shell = scope.closest<HTMLElement>(".workspace-workbench-shell");
  if (!shell) throw new Error("The scope element is not inside a workspace workbench shell.");
  return shell;
}

function toggleIn(container: HTMLElement | Document, name: "Inspect selection" | "Back to work list") {
  const query = container instanceof Document ? screen : within(container);
  return query.getByRole("button", { name });
}

/** The shell moves focus on the frame after a switch; let that land before the next input. */
async function settleFocusHandoff() {
  await act(() => new Promise<void>((resolve) => requestAnimationFrame(() => resolve())));
}

/** The queue is shown and the inspector is collapsed behind "Inspect selection". */
export function expectWorkbenchQueueShown(scope?: HTMLElement) {
  expect(toggleIn(workbenchFor(scope), "Inspect selection")).toHaveAttribute("aria-expanded", "false");
}

/**
 * Switches the narrow workbench containing `scope` (or the only one rendered) to its
 * inspector, as an operator does after selecting a row.
 */
export async function inspectSelection(user: User, scope?: HTMLElement) {
  const container = workbenchFor(scope);
  const toggle = toggleIn(container, "Inspect selection");
  expect(toggle).toHaveAttribute("aria-expanded", "false");
  await user.click(toggle);
  await settleFocusHandoff();
  expect(toggleIn(container, "Back to work list")).toHaveAttribute("aria-expanded", "true");
}

/** Returns the narrow workbench containing `scope` (or the only one rendered) to its queue. */
export async function backToWorkList(user: User, scope?: HTMLElement) {
  const container = workbenchFor(scope);
  await user.click(toggleIn(container, "Back to work list"));
  await settleFocusHandoff();
  expect(toggleIn(container, "Inspect selection")).toHaveAttribute("aria-expanded", "false");
}

const RECONCILIATION_INSPECTOR = /^(Reconciliation break detail for |Case inspector$)/;

/**
 * The reconciliation workbench manages its own switch: "Inspect selected break" (or activating
 * a break row) opens the inspector and "Back to break queue" returns.
 */
export async function inspectReconciliationBreak(user: User) {
  const toggle = screen.getByRole("button", { name: "Inspect selected break" });
  expect(toggle).toHaveAttribute("aria-expanded", "false");
  await user.click(toggle);
  await settleFocusHandoff();
  expect(screen.queryByRole("treegrid", { name: "Reconciliation break queue" })).not.toBeInTheDocument();
  // A case inspector nests inside the break detail panel; return the outermost region.
  const [inspector] = screen.getAllByRole("region", { name: RECONCILIATION_INSPECTOR });
  return inspector;
}

export async function backToBreakQueue(user: User) {
  await user.click(screen.getByRole("button", { name: "Back to break queue" }));
  await settleFocusHandoff();
  expect(screen.queryAllByRole("region", { name: RECONCILIATION_INSPECTOR })).toHaveLength(0);
  expect(screen.getByRole("treegrid", { name: "Reconciliation break queue" })).toBeInTheDocument();
}
