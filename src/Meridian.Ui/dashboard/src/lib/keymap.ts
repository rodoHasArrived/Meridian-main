// Single registry of workstation-global keyboard shortcuts. The shell matches events
// against these bindings and the keyboard help sheet renders them, so the two cannot
// drift. Screen-local keys (dense rows, charts, dialogs) stay with their components.

export type GlobalKeyBindingId = "command-palette" | "keyboard-help";

export interface GlobalKeyBinding {
  id: GlobalKeyBindingId;
  /** Combo such as "Mod+K" (Ctrl on Windows/Linux, Cmd on macOS) or a bare key such as "?". */
  keys: string;
  label: string;
}

export const GLOBAL_KEY_BINDINGS: readonly GlobalKeyBinding[] = [
  { id: "command-palette", keys: "Mod+K", label: "Open command palette" },
  { id: "keyboard-help", keys: "?", label: "Show keyboard shortcuts" }
];

export interface KeyEventLike {
  key: string;
  ctrlKey?: boolean;
  metaKey?: boolean;
  altKey?: boolean;
  shiftKey?: boolean;
}

export function getGlobalKeyBinding(id: GlobalKeyBindingId): GlobalKeyBinding {
  const binding = GLOBAL_KEY_BINDINGS.find((entry) => entry.id === id);
  if (!binding) {
    throw new Error(`Unknown global key binding: ${id}`);
  }
  return binding;
}

/** Splits a combo into its modifiers and key. "Mod" matches Ctrl or Meta. */
export function parseKeyCombo(keys: string): { mod: boolean; alt: boolean; shift: boolean; key: string } {
  const parts = keys.split("+").map((part) => part.trim()).filter(Boolean);
  const key = parts[parts.length - 1] ?? "";
  const modifiers = new Set(parts.slice(0, -1).map((part) => part.toLowerCase()));
  return {
    mod: modifiers.has("mod"),
    alt: modifiers.has("alt"),
    shift: modifiers.has("shift"),
    key: key.toLowerCase()
  };
}

/**
 * True when the event is exactly the combo: required modifiers present, no extra
 * modifiers. Shift is ignored for punctuation keys such as "?", which need Shift on
 * many layouts.
 */
export function matchesKeyCombo(event: KeyEventLike, keys: string): boolean {
  const combo = parseKeyCombo(keys);
  if (event.key.toLowerCase() !== combo.key) {
    return false;
  }

  const hasMod = Boolean(event.ctrlKey || event.metaKey);
  if (hasMod !== combo.mod) {
    return false;
  }
  if (Boolean(event.altKey) !== combo.alt) {
    return false;
  }
  const shiftIgnored = combo.key.length === 1 && !/[a-z0-9]/.test(combo.key);
  return shiftIgnored || Boolean(event.shiftKey) === combo.shift;
}
