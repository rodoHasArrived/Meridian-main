import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";

describe("Dialog", () => {
  it("requests close when Escape is pressed", async () => {
    const onOpenChange = vi.fn();

    render(
      <Dialog open onOpenChange={onOpenChange}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Confirm action</DialogTitle>
            <DialogDescription>Review the action before confirming.</DialogDescription>
          </DialogHeader>
        </DialogContent>
      </Dialog>
    );

    await userEvent.keyboard("{Escape}");

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("requests close from the backdrop but keeps dialog content clicks inside", async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();

    render(
      <Dialog open onOpenChange={onOpenChange}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Confirm action</DialogTitle>
            <DialogDescription>Review the action before confirming.</DialogDescription>
          </DialogHeader>
        </DialogContent>
      </Dialog>
    );

    await user.click(screen.getByRole("dialog"));
    expect(onOpenChange).not.toHaveBeenCalled();

    const backdrop = screen.getByRole("dialog").parentElement;
    expect(backdrop).not.toBeNull();
    await user.click(backdrop!);

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("moves focus into the dialog and restores focus after close", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [open, setOpen] = useState(false);

      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>Open confirmation</button>
          <Dialog open={open} onOpenChange={setOpen}>
            <DialogContent>
              <DialogHeader>
                <DialogTitle>Confirm action</DialogTitle>
                <DialogDescription>Review the action before confirming.</DialogDescription>
              </DialogHeader>
              <button type="button">First action</button>
              <button type="button">Last action</button>
            </DialogContent>
          </Dialog>
        </>
      );
    }

    render(<Harness />);

    const trigger = screen.getByRole("button", { name: "Open confirmation" });
    await user.click(trigger);

    await waitFor(() => expect(screen.getByRole("button", { name: "First action" })).toHaveFocus());

    await user.keyboard("{Escape}");

    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("wraps keyboard tab focus within the dialog", async () => {
    const user = userEvent.setup();

    render(
      <Dialog open onOpenChange={vi.fn()}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Confirm action</DialogTitle>
            <DialogDescription>Review the action before confirming.</DialogDescription>
          </DialogHeader>
          <button type="button">First action</button>
          <button type="button">Last action</button>
        </DialogContent>
      </Dialog>
    );

    const first = screen.getByRole("button", { name: "First action" });
    const last = screen.getByRole("button", { name: "Last action" });

    await waitFor(() => expect(first).toHaveFocus());

    await user.tab({ shift: true });
    expect(last).toHaveFocus();

    await user.tab();
    expect(first).toHaveFocus();
  });
});
