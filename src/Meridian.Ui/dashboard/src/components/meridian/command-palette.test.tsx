import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { CommandPalette } from "@/components/meridian/command-palette";

describe("CommandPalette", () => {
  it("marks the route-aware current workspace", () => {
    render(
      <MemoryRouter initialEntries={["/portfolio/positions"]}>
        <CommandPalette open onOpenChange={vi.fn()} />
      </MemoryRouter>
    );

    expect(screen.getByRole("dialog", { name: "Open workspace" })).toBeInTheDocument();
    expect(screen.getByText("Route to a canonical operator workspace. Current: Portfolio.")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "7 workspace commands" })).toBeInTheDocument();
    expect(screen.getByLabelText("Portfolio, current workspace")).toHaveAttribute("aria-current", "page");
    expect(screen.getByLabelText("Portfolio, current workspace")).toHaveFocus();
  });

  it("closes when Escape is pressed", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    render(
      <MemoryRouter initialEntries={["/settings"]}>
        <CommandPalette open onOpenChange={onOpenChange} />
      </MemoryRouter>
    );

    await user.keyboard("{Escape}");

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("closes when a workspace command is selected", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    render(
      <MemoryRouter initialEntries={["/trading"]}>
        <CommandPalette open onOpenChange={onOpenChange} />
      </MemoryRouter>
    );

    await user.click(screen.getByLabelText("Open Settings workspace"));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
