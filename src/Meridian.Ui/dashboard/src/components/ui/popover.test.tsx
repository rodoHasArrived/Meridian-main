import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Popover } from "@/components/ui/popover";

describe("Popover", () => {
  it("renders nothing when closed", () => {
    render(<Popover open={false}>panel</Popover>);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("renders content and closes on Escape", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    render(
      <Popover open onClose={onClose}>
        Anchored content
      </Popover>
    );

    expect(screen.getByText("Anchored content")).toBeInTheDocument();
    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
