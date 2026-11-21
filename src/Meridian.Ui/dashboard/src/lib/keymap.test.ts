import { afterEach, beforeEach, describe, expect, it } from "vitest";
import {
  comboFromKeyEvent,
  combosEqual,
  decodeKeyBindingOverrides,
  getGlobalKeyBinding,
  KEY_BINDING_OVERRIDES_STORAGE_KEY,
  matchesKeyCombo,
  reloadKeyBindingOverrides,
  resetKeyBindingOverrides,
  resolveGlobalKeyBindings,
  setKeyBindingOverride,
  validateKeyBindingCombo
} from "./keymap";

beforeEach(() => {
  localStorage.clear();
  reloadKeyBindingOverrides();
});

afterEach(() => {
  localStorage.clear();
  reloadKeyBindingOverrides();
});

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

describe("comboFromKeyEvent", () => {
  it("builds combos with modifiers in Mod, Alt, Shift order", () => {
    expect(comboFromKeyEvent({ key: "j", ctrlKey: true, shiftKey: true })).toBe("Mod+Shift+J");
    expect(comboFromKeyEvent({ key: "J", metaKey: true, altKey: true, shiftKey: true })).toBe("Mod+Alt+Shift+J");
    expect(comboFromKeyEvent({ key: "F2", altKey: true })).toBe("Alt+F2");
  });

  it("drops Shift for punctuation, whose key value already carries it", () => {
    expect(comboFromKeyEvent({ key: "?", shiftKey: true })).toBe("?");
    expect(comboFromKeyEvent({ key: "/", ctrlKey: true, shiftKey: true })).toBe("Mod+/");
  });

  it("returns null while only modifiers are held", () => {
    expect(comboFromKeyEvent({ key: "Control", ctrlKey: true })).toBeNull();
    expect(comboFromKeyEvent({ key: "Shift", shiftKey: true })).toBeNull();
    expect(comboFromKeyEvent({ key: "Meta", metaKey: true })).toBeNull();
    expect(comboFromKeyEvent({ key: "Dead" })).toBeNull();
  });

  it("names the space bar", () => {
    expect(comboFromKeyEvent({ key: " ", ctrlKey: true })).toBe("Mod+Space");
    expect(matchesKeyCombo({ key: " ", ctrlKey: true }, "Mod+Space")).toBe(true);
  });

  it("produces combos that match the event that made them", () => {
    const events = [
      { key: "j", ctrlKey: true, shiftKey: true },
      { key: "?", shiftKey: true },
      { key: "F2", altKey: true }
    ];
    for (const event of events) {
      expect(matchesKeyCombo(event, comboFromKeyEvent(event)!)).toBe(true);
    }
  });
});

describe("validateKeyBindingCombo", () => {
  it("accepts a free modified combo, a bare punctuation key and a bare function key", () => {
    expect(validateKeyBindingCombo("command-palette", "Mod+Shift+P")).toBeNull();
    expect(validateKeyBindingCombo("keyboard-help", "/")).toBeNull();
    expect(validateKeyBindingCombo("keyboard-help", "F1")).toBeNull();
  });

  it("refuses bare letters and digits so typing never triggers them", () => {
    expect(validateKeyBindingCombo("command-palette", "P")).toMatch(/Use Ctrl\/⌘ or Alt/);
    expect(validateKeyBindingCombo("command-palette", "Shift+P")).toMatch(/Use Ctrl\/⌘ or Alt/);
    expect(validateKeyBindingCombo("command-palette", "7")).toMatch(/Use Ctrl\/⌘ or Alt/);
  });

  it("refuses keys reserved by the shell or the browser", () => {
    expect(validateKeyBindingCombo("keyboard-help", "Escape")).toBe("Esc closes dialogs and sheets.");
    expect(validateKeyBindingCombo("keyboard-help", "Tab")).toBe("Tab moves keyboard focus.");
    expect(validateKeyBindingCombo("command-palette", "Mod+W")).toBe("The browser closes the tab on Ctrl/⌘+W.");
  });

  it("refuses a combo another global binding uses, naming it", () => {
    expect(validateKeyBindingCombo("keyboard-help", "Mod+K")).toBe("Ctrl / ⌘+K is already used by Open command palette.");
  });

  it("allows a binding to keep its own combo", () => {
    expect(validateKeyBindingCombo("command-palette", "Mod+K")).toBeNull();
  });
});

describe("operator key binding overrides", () => {
  it("applies an override to the shell's lookup and persists it for this browser", () => {
    expect(setKeyBindingOverride("command-palette", "Mod+Shift+P")).toBeNull();

    expect(getGlobalKeyBinding("command-palette").keys).toBe("Mod+Shift+P");
    expect(JSON.parse(localStorage.getItem(KEY_BINDING_OVERRIDES_STORAGE_KEY)!)).toEqual({ "command-palette": "Mod+Shift+P" });

    reloadKeyBindingOverrides();
    expect(getGlobalKeyBinding("command-palette").keys).toBe("Mod+Shift+P");
    expect(resolveGlobalKeyBindings().find((binding) => binding.id === "command-palette")).toMatchObject({
      keys: "Mod+Shift+P",
      defaultKeys: "Mod+K",
      overridden: true
    });
  });

  it("returns the refusal and changes nothing for a combo in use", () => {
    expect(setKeyBindingOverride("keyboard-help", "Mod+K")).toMatch(/already used by Open command palette/);
    expect(getGlobalKeyBinding("keyboard-help").keys).toBe("?");
    expect(localStorage.getItem(KEY_BINDING_OVERRIDES_STORAGE_KEY)).toBeNull();
  });

  it("removes the override when the default combo is chosen again", () => {
    setKeyBindingOverride("command-palette", "Mod+Shift+P");
    expect(setKeyBindingOverride("command-palette", "Mod+K")).toBeNull();

    expect(localStorage.getItem(KEY_BINDING_OVERRIDES_STORAGE_KEY)).toBeNull();
    expect(getGlobalKeyBinding("command-palette").keys).toBe("Mod+K");
  });

  it("resets one binding or all of them", () => {
    setKeyBindingOverride("command-palette", "Mod+Shift+P");
    setKeyBindingOverride("keyboard-help", "F1");

    resetKeyBindingOverrides("keyboard-help");
    expect(getGlobalKeyBinding("keyboard-help").keys).toBe("?");
    expect(getGlobalKeyBinding("command-palette").keys).toBe("Mod+Shift+P");

    resetKeyBindingOverrides();
    expect(getGlobalKeyBinding("command-palette").keys).toBe("Mod+K");
    expect(localStorage.getItem(KEY_BINDING_OVERRIDES_STORAGE_KEY)).toBeNull();
  });

  it("picks up an override written by another tab", () => {
    localStorage.setItem(KEY_BINDING_OVERRIDES_STORAGE_KEY, JSON.stringify({ "keyboard-help": "F1" }));
    window.dispatchEvent(new StorageEvent("storage", { key: KEY_BINDING_OVERRIDES_STORAGE_KEY }));

    expect(getGlobalKeyBinding("keyboard-help").keys).toBe("F1");
  });
});

describe("decodeKeyBindingOverrides", () => {
  it("ignores malformed, unknown, default-valued and refused entries", () => {
    expect(decodeKeyBindingOverrides("not json")).toEqual({});
    expect(decodeKeyBindingOverrides("[]")).toEqual({});
    expect(
      decodeKeyBindingOverrides(
        JSON.stringify({ "command-palette": "Mod+K", "keyboard-help": "Escape", "removed-binding": "F2" })
      )
    ).toEqual({});
    expect(decodeKeyBindingOverrides(JSON.stringify({ "keyboard-help": 42 }))).toEqual({});
  });

  it("keeps a stored swap of the two bindings", () => {
    expect(decodeKeyBindingOverrides(JSON.stringify({ "command-palette": "?", "keyboard-help": "Mod+K" }))).toEqual({
      "command-palette": "?",
      "keyboard-help": "Mod+K"
    });
  });

  it("drops a stored override that collides with the other binding's default", () => {
    expect(decodeKeyBindingOverrides(JSON.stringify({ "keyboard-help": "Mod+K" }))).toEqual({});
  });
});

describe("combosEqual", () => {
  it("treats Shift on punctuation as the same key", () => {
    expect(combosEqual("Shift+?", "?")).toBe(true);
    expect(combosEqual("Mod+Shift+K", "Mod+K")).toBe(false);
    expect(combosEqual("mod+k", "Mod+K")).toBe(true);
  });
});
