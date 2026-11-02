import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { KeyboardShortcutsSheet } from "./keyboard-shortcuts-sheet";

describe("KeyboardShortcutsSheet", () => {
  it("lists every global binding from the registry when open", () => {
    render(<KeyboardShortcutsSheet open onOpenChange={vi.fn()} />);

    expect(screen.getByText("Open command palette")).toBeTruthy();
    expect(screen.getByText("Show keyboard shortcuts")).toBeTruthy();
  });

  it("renders nothing while closed", () => {
    render(<KeyboardShortcutsSheet open={false} onOpenChange={vi.fn()} />);

    expect(screen.queryByText("Open command palette")).toBeNull();
  });
});
