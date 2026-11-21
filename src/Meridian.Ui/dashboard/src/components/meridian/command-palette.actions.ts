// Command palette action registry. Screens register context-scoped verbs while
// mounted (and unregister on unmount); the app shell composes these with its own
// shell-level actions and feeds one array into the palette view model.
//
// Governance: approval-gated or destructive mutations are never registered here.
// A `run` handler must delegate to an existing screen command or lib/api wrapper —
// the palette is a dispatch surface, not a home for business logic.
import { useEffect, useRef, useSyncExternalStore } from "react";

export interface CommandPaletteActionResult {
  title: string;
  detail?: string;
  tone?: "success" | "warning" | "danger" | "info";
}

export interface CommandPaletteActionItem {
  id: string;
  /** Imperative label, e.g. "Pin preset Morning close". */
  verbLabel: string;
  description: string;
  keywords?: string[];
  /** Require the inline two-step arm/confirm flow before running. */
  confirm?: boolean;
  disabled?: boolean;
  disabledReason?: string | null;
  run: () => Promise<CommandPaletteActionResult | void>;
}

type Listener = () => void;

const actionSources = new Map<string, CommandPaletteActionItem[]>();
const listeners = new Set<Listener>();
let snapshot: CommandPaletteActionItem[] = [];

function rebuildSnapshot() {
  snapshot = [...actionSources.values()].flat();
  for (const listener of listeners) {
    listener();
  }
}

/**
 * Registers a source's action items, replacing any prior registration under the
 * same sourceId. Returns an unregister callback for effect cleanup.
 */
export function registerCommandPaletteActions(
  sourceId: string,
  items: CommandPaletteActionItem[]
): () => void {
  actionSources.set(sourceId, items);
  rebuildSnapshot();
  return () => {
    if (actionSources.get(sourceId) === items) {
      actionSources.delete(sourceId);
      rebuildSnapshot();
    }
  };
}

export function getCommandPaletteActionsSnapshot(): CommandPaletteActionItem[] {
  return snapshot;
}

function subscribe(listener: Listener): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useCommandPaletteActions(): CommandPaletteActionItem[] {
  return useSyncExternalStore(subscribe, getCommandPaletteActionsSnapshot, getCommandPaletteActionsSnapshot);
}

export interface ScreenPaletteActionSpec extends Omit<CommandPaletteActionItem, "run"> {
  run: () => Promise<CommandPaletteActionResult | void> | void;
}

/**
 * Registers a mounted screen's palette actions. Re-registers only when an action's
 * identity, label or enablement changes; `run` always calls the latest handler, so
 * screens can pass inline closures without churning the registry each render.
 */
export function useScreenPaletteActions(sourceId: string, specs: ScreenPaletteActionSpec[]): void {
  const latest = useRef(specs);
  latest.current = specs;
  const signature = JSON.stringify(specs.map(({ run: _run, ...rest }) => rest));
  useEffect(() => {
    const items: CommandPaletteActionItem[] = latest.current.map(({ run: _run, ...rest }) => ({
      ...rest,
      run: async () => {
        const current = latest.current.find((spec) => spec.id === rest.id);
        return (await current?.run()) ?? undefined;
      }
    }));
    return registerCommandPaletteActions(sourceId, items);
  }, [sourceId, signature]);
}
