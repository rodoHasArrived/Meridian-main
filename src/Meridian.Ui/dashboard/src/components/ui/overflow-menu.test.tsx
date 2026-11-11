import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { OverflowMenu } from "@/components/ui/overflow-menu";

describe("OverflowMenu", () => {
  it("opens a labelled menu from the trigger and runs the chosen action", async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(
      <OverflowMenu
        label="More order actions"
        items={[{ id: "cancel-all", label: "Cancel all open orders…", danger: true, onSelect }]}
      />
    );

    const trigger = screen.getByRole("button", { name: "More order actions" });
    expect(trigger).toHaveAttribute("aria-haspopup", "menu");
    expect(trigger).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();

    await user.click(trigger);
    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("menu", { name: "More order actions" })).toBeInTheDocument();

    await user.click(screen.getByRole("menuitem", { name: /cancel all open orders/i }));
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("closes on Escape and shows why a disabled item is unavailable", async () => {
    const user = userEvent.setup();
    const onSelect = vi.fn();
    render(
      <OverflowMenu
        label="More order actions"
        items={[{
          id: "cancel-all",
          label: "Cancel all open orders…",
          description: "No open orders require cancellation.",
          disabled: true,
          onSelect
        }]}
      />
    );

    await user.click(screen.getByRole("button", { name: "More order actions" }));
    const item = screen.getByRole("menuitem", { name: /cancel all open orders/i });
    expect(item).toBeDisabled();
    expect(item).toHaveTextContent("No open orders require cancellation.");
    // The reason has to stay readable, so a disabled item is not dimmed as a whole.
    expect(item).not.toHaveClass("opacity-50");

    await user.keyboard("{Escape}");
    expect(screen.queryByRole("menu")).not.toBeInTheDocument();
    expect(onSelect).not.toHaveBeenCalled();
  });
});
