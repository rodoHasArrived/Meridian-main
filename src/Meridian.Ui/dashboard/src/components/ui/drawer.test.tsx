import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { Drawer, DrawerBody, DrawerFooter, DrawerHeader } from "@/components/ui/drawer";

describe("Drawer", () => {
  it("renders nothing when closed", () => {
    render(
      <Drawer open={false} onClose={vi.fn()} title="Inspector">
        <DrawerBody>content</DrawerBody>
      </Drawer>
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("renders header/body/footer and closes via the close button", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    render(
      <Drawer open onClose={onClose}>
        <DrawerHeader title="Account 4000" onClose={onClose} />
        <DrawerBody>Revenue detail</DrawerBody>
        <DrawerFooter>
          <button type="button">Save</button>
        </DrawerFooter>
      </Drawer>
    );

    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("Revenue detail")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Close drawer" }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it("closes on Escape", async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();

    render(
      <Drawer open onClose={onClose} title="Filters">
        <DrawerBody>content</DrawerBody>
      </Drawer>
    );

    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
