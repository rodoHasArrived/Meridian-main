import { useState } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Sheet, SheetBody, SheetCloseButton, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";

describe("Sheet", () => {
  it("requests close when Escape is pressed", async () => {
    const onOpenChange = vi.fn();

    render(
      <Sheet open onOpenChange={onOpenChange}>
        <SheetContent aria-labelledby="sheet-title">
          <SheetHeader>
            <SheetTitle id="sheet-title">Workspace navigation</SheetTitle>
          </SheetHeader>
        </SheetContent>
      </Sheet>
    );

    await userEvent.keyboard("{Escape}");

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("moves focus into the sheet and restores focus after close", async () => {
    const user = userEvent.setup();

    function Harness() {
      const [open, setOpen] = useState(false);

      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>Open sheet</button>
          <Sheet open={open} onOpenChange={setOpen}>
            <SheetContent aria-labelledby="sheet-title" aria-describedby="sheet-description">
              <SheetHeader>
                <SheetTitle id="sheet-title">Workspace navigation</SheetTitle>
                <SheetDescription id="sheet-description">Move between operator workspaces.</SheetDescription>
                <SheetCloseButton onClick={() => setOpen(false)} label="Close workspace navigation" />
              </SheetHeader>
              <SheetBody>
                <button type="button">First action</button>
                <button type="button">Last action</button>
              </SheetBody>
            </SheetContent>
          </Sheet>
        </>
      );
    }

    render(<Harness />);

    const trigger = screen.getByRole("button", { name: "Open sheet" });
    await user.click(trigger);

    await waitFor(() => expect(screen.getByRole("button", { name: "Close workspace navigation" })).toHaveFocus());

    await user.keyboard("{Escape}");

    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it("wraps Tab focus within the sheet", async () => {
    const user = userEvent.setup();

    render(
      <Sheet open onOpenChange={vi.fn()}>
        <SheetContent aria-labelledby="sheet-title">
          <SheetHeader>
            <SheetTitle id="sheet-title">Workspace navigation</SheetTitle>
            <SheetCloseButton label="Close workspace navigation" />
          </SheetHeader>
          <SheetBody>
            <button type="button">First action</button>
            <button type="button">Last action</button>
          </SheetBody>
        </SheetContent>
      </Sheet>
    );

    const close = screen.getByRole("button", { name: "Close workspace navigation" });
    const last = screen.getByRole("button", { name: "Last action" });

    await waitFor(() => expect(close).toHaveFocus());

    await user.tab({ shift: true });
    expect(last).toHaveFocus();

    await user.tab();
    expect(close).toHaveFocus();
  });

  it("requests close from the backdrop but keeps content clicks inside", async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();

    render(
      <Sheet open onOpenChange={onOpenChange}>
        <SheetContent aria-labelledby="sheet-title">
          <SheetHeader>
            <SheetTitle id="sheet-title">Workspace navigation</SheetTitle>
          </SheetHeader>
        </SheetContent>
      </Sheet>
    );

    await user.click(screen.getByRole("dialog"));
    expect(onOpenChange).not.toHaveBeenCalled();

    const backdrop = screen.getByRole("dialog").parentElement;
    expect(backdrop).not.toBeNull();
    await user.click(backdrop!);

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
