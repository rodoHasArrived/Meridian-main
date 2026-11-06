import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { BREAK_QUEUE_KEYBOARD_MAP, isEditableShortcutTarget, resolveQueueShortcut } from "@/lib/queue-keyboard-map";

function keyEvent(key: string, target: EventTarget | null = document.body, modifiers: Partial<{ ctrlKey: boolean; metaKey: boolean; altKey: boolean }> = {}) {
  return { key, target, ctrlKey: false, metaKey: false, altKey: false, ...modifiers };
}

describe("break queue keyboard map (C2)", () => {
  it("maps each documented key to one action", () => {
    expect(BREAK_QUEUE_KEYBOARD_MAP.map((shortcut) => [shortcut.key, shortcut.action])).toEqual([
      ["j", "next"], ["k", "previous"], ["x", "toggle-select"], ["a", "assign"], ["e", "evidence"], ["?", "help"]
    ]);
    for (const shortcut of BREAK_QUEUE_KEYBOARD_MAP) {
      expect(resolveQueueShortcut(keyEvent(shortcut.key))).toBe(shortcut.action);
    }
    expect(resolveQueueShortcut(keyEvent("z"))).toBeNull();
  });

  it("ignores chorded keys and keys typed into text fields, but not a focused checkbox", () => {
    expect(resolveQueueShortcut(keyEvent("j", document.body, { ctrlKey: true }))).toBeNull();
    expect(resolveQueueShortcut(keyEvent("a", document.body, { metaKey: true }))).toBeNull();

    const text = document.createElement("input");
    const checkbox = Object.assign(document.createElement("input"), { type: "checkbox" });
    const select = document.createElement("select");
    const dialog = Object.assign(document.createElement("div"), { role: "dialog" });
    dialog.setAttribute("role", "dialog");
    const inDialog = document.createElement("button");
    dialog.append(inDialog);
    expect(isEditableShortcutTarget(text)).toBe(true);
    expect(isEditableShortcutTarget(select)).toBe(true);
    expect(isEditableShortcutTarget(inDialog)).toBe(true);
    expect(isEditableShortcutTarget(checkbox)).toBe(false);
    expect(resolveQueueShortcut(keyEvent("j", text))).toBeNull();
    expect(resolveQueueShortcut(keyEvent("j", checkbox))).toBe("next");
  });

  it("is documented in the dashboard README, key for key", () => {
    const readme = readFileSync(resolve(process.cwd(), "README.md"), "utf8");
    for (const shortcut of BREAK_QUEUE_KEYBOARD_MAP) {
      expect(readme).toContain(`| \`${shortcut.key}\` | ${shortcut.description} |`);
    }
  });
});
