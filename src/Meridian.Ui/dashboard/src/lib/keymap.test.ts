import { describe, expect, it } from "vitest";
import { getGlobalKeyBinding, matchesKeyCombo } from "./keymap";

describe("matchesKeyCombo", () => {
  it("matches Mod+K with either Ctrl or Meta", () => {
    expect(matchesKeyCombo({ key: "k", ctrlKey: true }, "Mod+K")).toBe(true);
    expect(matchesKeyCombo({ key: "K", metaKey: true }, "Mod+K")).toBe(true);
  });

  it("rejects extra or missing modifiers", () => {
    expect(matchesKeyCombo({ key: "k" }, "Mod+K")).toBe(false);
    expect(matchesKeyCombo({ key: "k", ctrlKey: true, shiftKey: true }, "Mod+K")).toBe(false);
    expect(matchesKeyCombo({ key: "k", ctrlKey: true, altKey: true }, "Mod+K")).toBe(false);
    expect(matchesKeyCombo({ key: "j", ctrlKey: true }, "Mod+K")).toBe(false);
  });

  it("matches a bare punctuation key with or without Shift", () => {
    expect(matchesKeyCombo({ key: "?" }, "?")).toBe(true);
    expect(matchesKeyCombo({ key: "?", shiftKey: true }, "?")).toBe(true);
    expect(matchesKeyCombo({ key: "?", ctrlKey: true }, "?")).toBe(false);
  });
});

describe("global key bindings", () => {
  it("keeps the command palette on Mod+K", () => {
    expect(getGlobalKeyBinding("command-palette").keys).toBe("Mod+K");
  });

  it("exposes the keyboard help binding on ?", () => {
    expect(getGlobalKeyBinding("keyboard-help").keys).toBe("?");
  });
});
