// Single registry of workstation-global keyboard shortcuts. The shell matches events
// against these bindings and the keyboard help sheet renders them, so the two cannot
// drift. Screen-local keys (dense rows, charts, dialogs) stay with their components.
//
// Operators may rebind a global shortcut from the keyboard help sheet. Overrides are a
// per-browser preference persisted in localStorage (like the display time zone); a stored
// override that no longer parses, collides or is refused is ignored and the default applies.

import { useSyncExternalStore } from "react";
import { notifyOperatorPreferenceChanged } from "@/lib/operator-preference-events";

export type GlobalKeyBindingId = "command-palette" | "keyboard-help";

export interface GlobalKeyBinding {
  id: GlobalKeyBindingId;
  /** Combo such as "Mod+K" (Ctrl on Windows/Linux, Cmd on macOS) or a bare key such as "?". */
  keys: string;
  label: string;
}

export interface EffectiveGlobalKeyBinding extends GlobalKeyBinding {
  /** The registry default, kept so the editor can show and restore it. */
  defaultKeys: string;
  /** True when an operator override replaces the default. */
  overridden: boolean;
}

export const GLOBAL_KEY_BINDINGS: readonly GlobalKeyBinding[] = [
  { id: "command-palette", keys: "Mod+K", label: "Open command palette" },
  { id: "keyboard-help", keys: "?", label: "Show keyboard shortcuts" }
];

export const KEY_BINDING_OVERRIDES_STORAGE_KEY = "meridian.workstation.keymap.v1";

export type KeyBindingOverrides = Partial<Record<GlobalKeyBindingId, string>>;

export interface KeyEventLike {
  key: string;
  ctrlKey?: boolean;
  metaKey?: boolean;
  altKey?: boolean;
  shiftKey?: boolean;
}

const MODIFIER_KEYS = new Set(["control", "meta", "alt", "shift", "altgraph", "os", "hyper", "super", "capslock", "fn"]);

// Keys the shell or the browser already gives a fixed meaning; binding them globally would
// break dialogs, focus order or the browser chrome.
const RESERVED_COMBOS: ReadonlyArray<{ combo: string; reason: string }> = [
  { combo: "Escape", reason: "Esc closes dialogs and sheets." },
  { combo: "Tab", reason: "Tab moves keyboard focus." },
  { combo: "Shift+Tab", reason: "Shift+Tab moves keyboard focus." },
  { combo: "Enter", reason: "Enter activates the focused control." },
  { combo: "Space", reason: "Space activates the focused control." },
  { combo: "Mod+W", reason: "The browser closes the tab on Ctrl/⌘+W." },
  { combo: "Mod+T", reason: "The browser opens a tab on Ctrl/⌘+T." },
  { combo: "Mod+N", reason: "The browser opens a window on Ctrl/⌘+N." },
  { combo: "Mod+Q", reason: "The browser quits on ⌘+Q." },
  { combo: "Mod+R", reason: "The browser reloads on Ctrl/⌘+R." },
  { combo: "Mod+L", reason: "The browser focuses the address bar on Ctrl/⌘+L." }
];

function isKnownBindingId(id: string): id is GlobalKeyBindingId {
  return GLOBAL_KEY_BINDINGS.some((binding) => binding.id === id);
}

function defaultBinding(id: GlobalKeyBindingId): GlobalKeyBinding {
  const binding = GLOBAL_KEY_BINDINGS.find((entry) => entry.id === id);
  if (!binding) {
    throw new Error(`Unknown global key binding: ${id}`);
  }
  return binding;
}

/** The binding as the shell applies it: the operator's override when one is set, else the default. */
export function getGlobalKeyBinding(id: GlobalKeyBindingId): GlobalKeyBinding {
  const binding = defaultBinding(id);
  const override = getKeyBindingOverrides()[id];
  return override ? { ...binding, keys: override } : binding;
}

/** Every global binding with its effective combo, default and override state. */
export function resolveGlobalKeyBindings(overrides: KeyBindingOverrides = getKeyBindingOverrides()): EffectiveGlobalKeyBinding[] {
  return GLOBAL_KEY_BINDINGS.map((binding) => {
    const override = overrides[binding.id];
    return {
      ...binding,
      keys: override ?? binding.keys,
      defaultKeys: binding.keys,
      overridden: override !== undefined && override !== binding.keys
    };
  });
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

function eventKeyName(key: string): string {
  return key === " " ? "space" : key.toLowerCase();
}

function shiftIgnoredFor(key: string): boolean {
  return key.length === 1 && !/[a-z0-9]/.test(key);
}

/**
 * True when the event is exactly the combo: required modifiers present, no extra
 * modifiers. Shift is ignored for punctuation keys such as "?", which need Shift on
 * many layouts.
 */
export function matchesKeyCombo(event: KeyEventLike, keys: string): boolean {
  const combo = parseKeyCombo(keys);
  if (eventKeyName(event.key) !== combo.key) {
    return false;
  }

  const hasMod = Boolean(event.ctrlKey || event.metaKey);
  if (hasMod !== combo.mod) {
    return false;
  }
  if (Boolean(event.altKey) !== combo.alt) {
    return false;
  }
  return shiftIgnoredFor(combo.key) || Boolean(event.shiftKey) === combo.shift;
}

/**
 * Turns a key press into a combo string ("Mod+Shift+J", "Alt+F2", "?"), or null while only
 * modifiers are held. Shift is dropped for punctuation, whose key value already carries it.
 */
export function comboFromKeyEvent(event: KeyEventLike): string | null {
  const name = eventKeyName(event.key);
  if (!event.key || MODIFIER_KEYS.has(name) || name === "dead" || name === "unidentified" || name === "process") {
    return null;
  }

  const parts: string[] = [];
  if (event.ctrlKey || event.metaKey) parts.push("Mod");
  if (event.altKey) parts.push("Alt");
  if (event.shiftKey && !shiftIgnoredFor(name)) parts.push("Shift");
  parts.push(displayKeyName(event.key));
  return parts.join("+");
}

function displayKeyName(key: string): string {
  if (key === " ") return "Space";
  if (key.length === 1) return key.toUpperCase();
  return key;
}

/** True when two combos fire on the same key presses. */
export function combosEqual(left: string, right: string): boolean {
  const a = parseKeyCombo(left);
  const b = parseKeyCombo(right);
  if (a.key !== b.key || a.mod !== b.mod || a.alt !== b.alt) {
    return false;
  }
  return shiftIgnoredFor(a.key) || a.shift === b.shift;
}

/**
 * Why a combo cannot be used for this binding, or null when it can. Checks, in order: a key is
 * present, the combo is not reserved by the shell or browser, letters, digits and named keys
 * need Ctrl/⌘ or Alt so typing never triggers them, and no other global binding uses it.
 */
export function validateKeyBindingCombo(
  id: GlobalKeyBindingId,
  combo: string,
  overrides: KeyBindingOverrides = getKeyBindingOverrides()
): string | null {
  const parsed = parseKeyCombo(combo);
  if (!parsed.key || parsed.key === "+") {
    return "Press a key together with any modifiers.";
  }

  const reserved = RESERVED_COMBOS.find((entry) => combosEqual(entry.combo, combo));
  if (reserved) {
    return reserved.reason;
  }

  const bare = !parsed.mod && !parsed.alt;
  if (bare && !shiftIgnoredFor(parsed.key) && !/^f([1-9]|1[0-9]|2[0-4])$/.test(parsed.key)) {
    return "Use Ctrl/⌘ or Alt with letters, numbers and named keys so typing never triggers the shortcut.";
  }

  const conflict = resolveGlobalKeyBindings(overrides).find((binding) => binding.id !== id && combosEqual(binding.keys, combo));
  if (conflict) {
    return `${formatKeyCombo(combo)} is already used by ${conflict.label}.`;
  }

  return null;
}

/** Display form of a combo: "Mod" becomes "Ctrl / ⌘". */
export function formatKeyCombo(keys: string): string {
  return keys
    .split("+")
    .map((part) => (part === "Mod" ? "Ctrl / ⌘" : part))
    .join("+");
}

// ── Persisted operator overrides ─────────────────────────────────────

function storage(): Storage | null {
  try {
    return typeof localStorage === "undefined" ? null : localStorage;
  } catch {
    return null;
  }
}

/**
 * Reads stored overrides defensively: unknown ids, non-string values, values equal to the
 * default and combos the editor would refuse are dropped. Overrides are checked against each
 * other in registry order, so a stored swap of two bindings is kept as a pair.
 */
export function decodeKeyBindingOverrides(raw: string | null | undefined): KeyBindingOverrides {
  if (!raw) {
    return {};
  }

  let parsed: unknown;
  try {
    parsed = JSON.parse(raw);
  } catch {
    return {};
  }
  if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
    return {};
  }

  const candidate: KeyBindingOverrides = {};
  for (const [id, keys] of Object.entries(parsed as Record<string, unknown>)) {
    if (isKnownBindingId(id) && typeof keys === "string" && keys.trim() && keys !== defaultBinding(id).keys) {
      candidate[id] = keys.trim();
    }
  }

  // Validate against the full candidate set, then drop refused entries until stable.
  let changed = true;
  while (changed) {
    changed = false;
    for (const binding of GLOBAL_KEY_BINDINGS) {
      const keys = candidate[binding.id];
      if (keys !== undefined && validateKeyBindingCombo(binding.id, keys, candidate) !== null) {
        delete candidate[binding.id];
        changed = true;
      }
    }
  }
  return candidate;
}

let currentOverrides: KeyBindingOverrides | null = null;
const listeners = new Set<() => void>();
let storageListenerAttached = false;

function notify() {
  for (const listener of listeners) {
    listener();
  }
}

function onStorage(event: StorageEvent) {
  if (event.key === null || event.key === KEY_BINDING_OVERRIDES_STORAGE_KEY) {
    reloadKeyBindingOverrides();
  }
}

function ensureStorageListener(): void {
  if (storageListenerAttached || typeof window === "undefined") {
    return;
  }
  storageListenerAttached = true;
  window.addEventListener("storage", onStorage);
}

/** The operator's current overrides for this browser (empty when every binding is default). */
export function getKeyBindingOverrides(): KeyBindingOverrides {
  ensureStorageListener();
  if (currentOverrides === null) {
    let raw: string | null;
    try {
      raw = storage()?.getItem(KEY_BINDING_OVERRIDES_STORAGE_KEY) ?? null;
    } catch {
      raw = null;
    }
    currentOverrides = decodeKeyBindingOverrides(raw);
  }
  return currentOverrides;
}

function writeOverrides(next: KeyBindingOverrides, announce = true): void {
  try {
    if (Object.keys(next).length === 0) {
      storage()?.removeItem(KEY_BINDING_OVERRIDES_STORAGE_KEY);
    } else {
      storage()?.setItem(KEY_BINDING_OVERRIDES_STORAGE_KEY, JSON.stringify(next));
    }
  } catch {
    // Persistence is best-effort when browser storage is blocked; the change still applies now.
  }
  currentOverrides = next;
  notify();
  if (announce) {
    notifyOperatorPreferenceChanged();
  }
}

/**
 * Replaces this browser's overrides with a set loaded from the operator's host preferences,
 * decoded as defensively as stored values. Does not announce an operator change.
 */
export function applyKeyBindingOverrides(overrides: Readonly<Record<string, string>> | null | undefined): void {
  const next = decodeKeyBindingOverrides(overrides ? JSON.stringify(overrides) : null);
  if (JSON.stringify(next) !== JSON.stringify(getKeyBindingOverrides())) {
    writeOverrides(next, false);
  }
}

/**
 * Rebinds a global shortcut for this browser. Returns null on success, or the reason the combo
 * was refused (nothing changes). Choosing the default combo removes the override.
 */
export function setKeyBindingOverride(id: GlobalKeyBindingId, combo: string): string | null {
  const overrides = getKeyBindingOverrides();
  const refusal = validateKeyBindingCombo(id, combo, overrides);
  if (refusal !== null) {
    return refusal;
  }

  const next: KeyBindingOverrides = { ...overrides };
  if (combosEqual(combo, defaultBinding(id).keys)) {
    delete next[id];
  } else {
    next[id] = combo;
  }
  writeOverrides(next);
  return null;
}

/** Restores one binding, or every binding when no id is given, to the registry default. */
export function resetKeyBindingOverrides(id?: GlobalKeyBindingId): void {
  const overrides = getKeyBindingOverrides();
  if (id === undefined) {
    if (Object.keys(overrides).length > 0) writeOverrides({});
    return;
  }
  if (overrides[id] === undefined) {
    return;
  }
  const next: KeyBindingOverrides = { ...overrides };
  delete next[id];
  writeOverrides(next);
}

/** Forget the in-memory overrides so the next read comes from storage again (tests, other tabs). */
export function reloadKeyBindingOverrides(): void {
  currentOverrides = null;
  getKeyBindingOverrides();
  notify();
}

function subscribe(listener: () => void): () => void {
  ensureStorageListener();
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** React view of the effective global bindings; re-renders when an override changes in any tab. */
export function useGlobalKeyBindings(): EffectiveGlobalKeyBinding[] {
  const overrides = useSyncExternalStore(subscribe, getKeyBindingOverrides, getKeyBindingOverrides);
  return resolveGlobalKeyBindings(overrides);
}
