import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { KeyboardShortcutsSheet } from "./keyboard-shortcuts-sheet";
import { QUEUE_KEY_BINDINGS } from "@/lib/keymap";

describe("KeyboardShortcutsSheet", () => {
  it("lists every global binding from the registry when open", () => {
    render(<KeyboardShortcutsSheet open onOpenChange={vi.fn()} />);

    expect(screen.getByText("Open command palette")).toBeTruthy();
    expect(screen.getByText("Show keyboard shortcuts")).toBeTruthy();
  });

  it("lists the work-queue keys from the same registry the queues use", () => {
    render(<KeyboardShortcutsSheet open onOpenChange={vi.fn()} />);

    const queueKeys = screen.getByRole("list", { name: "Work queue shortcuts" });
    for (const binding of Object.values(QUEUE_KEY_BINDINGS)) {
      expect(queueKeys).toHaveTextContent(binding.label);
    }
    expect(queueKeys).toHaveTextContent("Acknowledge the selected item");
  });

  it("renders nothing while closed", () => {
    render(<KeyboardShortcutsSheet open={false} onOpenChange={vi.fn()} />);

    expect(screen.queryByText("Open command palette")).toBeNull();
  });
});
