import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getGlobalKeyBinding, KEY_BINDING_OVERRIDES_STORAGE_KEY, reloadKeyBindingOverrides, setKeyBindingOverride } from "@/lib/keymap";
import { KeyboardShortcutsSheet } from "./keyboard-shortcuts-sheet";

function press(init: KeyboardEventInit) {
  act(() => {
    window.dispatchEvent(new KeyboardEvent("keydown", { bubbles: true, cancelable: true, ...init }));
  });
}

describe("KeyboardShortcutsSheet", () => {
  beforeEach(() => {
    localStorage.clear();
    reloadKeyBindingOverrides();
  });

  afterEach(() => {
    localStorage.clear();
    reloadKeyBindingOverrides();
  });

  it("lists every global binding from the registry when open", () => {
    render(<KeyboardShortcutsSheet open onOpenChange={vi.fn()} />);

    expect(screen.getByText("Open command palette")).toBeTruthy();
    expect(screen.getByText("Show keyboard shortcuts")).toBeTruthy();
  });

  it("renders nothing while closed", () => {
    render(<KeyboardShortcutsSheet open={false} onOpenChange={vi.fn()} />);

    expect(screen.queryByText("Open command palette")).toBeNull();
  });

  it("rebinds a shortcut from the next chord and saves it for this browser", () => {
    render(<KeyboardShortcutsSheet open onOpenChange={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Change shortcut for Open command palette" }));
    expect(screen.getByRole("status").textContent).toBe("Press the new shortcut for Open command palette, or Esc to cancel.");

    press({ key: "Control", ctrlKey: true }); // modifier alone: still capturing
    expect(screen.getByRole("button", { name: "Cancel changing Open command palette" })).toBeTruthy();

    press({ key: "p", ctrlKey: true, shiftKey: true });

    expect(screen.getByRole("status").textContent).toBe("Open command palette is now Ctrl / ⌘+Shift+P.");
    expect(getGlobalKeyBinding("command-palette").keys).toBe("Mod+Shift+P");
    expect(localStorage.getItem(KEY_BINDING_OVERRIDES_STORAGE_KEY)).toContain("Mod+Shift+P");
    expect(screen.getByText("Default: Ctrl / ⌘+K")).toBeTruthy();
  });

  it("keeps the captured chord from reaching the shell's global shortcut handler", () => {
    const shellHandler = vi.fn();
    window.addEventListener("keydown", shellHandler);
    try {
      render(<KeyboardShortcutsSheet open onOpenChange={vi.fn()} />);
      fireEvent.click(screen.getByRole("button", { name: "Change shortcut for Show keyboard shortcuts" }));

      press({ key: "k", ctrlKey: true });

      expect(shellHandler).not.toHaveBeenCalled();
    } finally {
      window.removeEventListener("keydown", shellHandler);
    }
  });

  it("shows why a chord was refused and leaves the binding unchanged", () => {
    render(<KeyboardShortcutsSheet open onOpenChange={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Change shortcut for Show keyboard shortcuts" }));
    press({ key: "k", ctrlKey: true });

    expect(screen.getByRole("status").textContent).toBe("Ctrl / ⌘+K is already used by Open command palette.");
    expect(getGlobalKeyBinding("keyboard-help").keys).toBe("?");
  });

  it("cancels capture on Esc without closing the sheet", () => {
    const onOpenChange = vi.fn();
    render(<KeyboardShortcutsSheet open onOpenChange={onOpenChange} />);

    fireEvent.click(screen.getByRole("button", { name: "Change shortcut for Open command palette" }));
    press({ key: "Escape" });

    expect(onOpenChange).not.toHaveBeenCalled();
    expect(screen.getByRole("status").textContent).toBe("Change cancelled.");
    expect(screen.getByRole("button", { name: "Change shortcut for Open command palette" })).toBeTruthy();
    expect(getGlobalKeyBinding("command-palette").keys).toBe("Mod+K");

    press({ key: "Escape" });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("resets one shortcut, then all shortcuts", () => {
    setKeyBindingOverride("command-palette", "Mod+Shift+P");
    setKeyBindingOverride("keyboard-help", "F1");
    render(<KeyboardShortcutsSheet open onOpenChange={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Reset Show keyboard shortcuts to ?" }));
    expect(getGlobalKeyBinding("keyboard-help").keys).toBe("?");
    expect(screen.getByRole("status").textContent).toBe("Show keyboard shortcuts is back to ?.");

    fireEvent.click(screen.getByRole("button", { name: "Reset all to defaults" }));
    expect(getGlobalKeyBinding("command-palette").keys).toBe("Mod+K");
    expect(screen.queryByRole("button", { name: "Reset all to defaults" })).toBeNull();
  });
});
