// Keyboard triage map for workstation queues (Phase 3, C2).
//
// One table drives the handler, the `?` cheat sheet and the README, so the documented keys are
// the keys that work. Queue keys apply while focus is inside the queue and never inside a text
// field, select or contenteditable region. A row of a DenseDataTable otherwise treats printable
// keys as type-ahead, so the queue claims these keys in the capture phase before the row sees
// them; every other letter still jumps to a matching row.

export type QueueShortcutAction = "next" | "previous" | "toggle-select" | "assign" | "evidence" | "help";

export interface QueueShortcut {
  key: string;
  action: QueueShortcutAction;
  description: string;
}

export const BREAK_QUEUE_KEYBOARD_MAP: readonly QueueShortcut[] = [
  { key: "j", action: "next", description: "Move to the next break" },
  { key: "k", action: "previous", description: "Move to the previous break" },
  { key: "x", action: "toggle-select", description: "Add the current break to the bulk selection, or remove it" },
  { key: "a", action: "assign", description: "Assign to me: the bulk selection when there is one, otherwise the current break" },
  { key: "e", action: "evidence", description: "Open the evidence drawer for the current break" },
  { key: "?", action: "help", description: "Show these shortcuts" }
];

export interface QueueShortcutEvent {
  key: string;
  ctrlKey: boolean;
  metaKey: boolean;
  altKey: boolean;
  target: EventTarget | null;
}

// Inputs that take no typed text, so queue keys keep working while one has focus.
const NON_TEXT_INPUT_TYPES = new Set(["checkbox", "radio", "button", "submit", "reset", "range", "color", "file", "image"]);

export function isEditableShortcutTarget(target: EventTarget | null): boolean {
  if (!target || typeof (target as Element).closest !== "function") return false;
  const element = target as HTMLElement;
  if (element.isContentEditable) return true;
  if (element.closest("textarea, select, [contenteditable=''], [contenteditable='true'], [role='dialog']")) return true;
  const input = element.closest("input");
  return input !== null && !NON_TEXT_INPUT_TYPES.has(input.type);
}

/** The queue action a key press asks for, or null when the key belongs to something else. */
export function resolveQueueShortcut(
  event: QueueShortcutEvent,
  map: readonly QueueShortcut[] = BREAK_QUEUE_KEYBOARD_MAP
): QueueShortcutAction | null {
  if (event.ctrlKey || event.metaKey || event.altKey) return null;
  if (isEditableShortcutTarget(event.target)) return null;
  return map.find((shortcut) => shortcut.key === event.key)?.action ?? null;
}
