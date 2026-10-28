import { fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { WorkspaceViewsMenu } from "@/components/meridian/workspace-views-menu";
import { Dialog, DialogContent } from "@/components/ui/dialog";

function NestedSaveDialog() {
  const [open, setOpen] = useState(false);
  return <>
    <button onClick={() => setOpen(true)}>Save current view</button>
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogContent aria-label="Save view"><input aria-label="View name" /></DialogContent>
    </Dialog>
  </>;
}

describe("WorkspaceViewsMenu", () => {
  it("disposes nested modal state and its focus trap when navigation hides the disclosure", async () => {
    const user = userEvent.setup();
    const content = (routeKey: string) => <>
      <WorkspaceViewsMenu routeKey={routeKey}><NestedSaveDialog /></WorkspaceViewsMenu>
      <button>New screen action</button><button>Next screen action</button>
    </>;
    const { rerender } = render(content("/portfolio"));
    await user.click(screen.getByText("Views"));
    await user.click(screen.getByRole("button", { name: "Save current view" }));
    expect(screen.getByRole("dialog", { name: "Save view" })).toBeVisible();
    rerender(content("/trading"));
    expect(screen.queryByRole("dialog", { name: "Save view", hidden: true })).not.toBeInTheDocument();
    screen.getByRole("button", { name: "New screen action" }).focus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Next screen action" })).toHaveFocus();
  });

  it("closes view controls when navigation changes the route", async () => {
    const user = userEvent.setup();
    const { rerender } = render(<WorkspaceViewsMenu routeKey="/trading"><button>Saved layouts</button></WorkspaceViewsMenu>);
    await user.click(screen.getByText("Views"));
    rerender(<WorkspaceViewsMenu routeKey="/portfolio"><button>Saved layouts</button></WorkspaceViewsMenu>);
    expect(screen.getByText("Views").closest("details")).not.toHaveAttribute("open");
  });

  it("discloses existing controls and restores focus on Escape", async () => {
    const user = userEvent.setup();
    render(<WorkspaceViewsMenu><button>Saved layouts</button><button>Save current view</button></WorkspaceViewsMenu>);
    const summary = screen.getByText("Views");
    expect(summary.closest("details")).not.toHaveAttribute("open");
    await user.click(summary);
    expect(screen.getByRole("region", { name: "View controls" })).toBeVisible();
    await user.tab();
    expect(screen.getByRole("button", { name: "Saved layouts" })).toHaveFocus();
    await user.keyboard("{Escape}");
    expect(summary.closest("details")).not.toHaveAttribute("open");
    expect(summary).toHaveFocus();
  });

  it("leaves nested dialogs in control of Escape and closes on outside pointer interaction", async () => {
    const user = userEvent.setup();
    render(<><WorkspaceViewsMenu><div role="dialog" aria-label="Save view"><input aria-label="Name" /></div></WorkspaceViewsMenu><button>Outside</button></>);
    const summary = screen.getByText("Views");
    await user.click(summary);
    fireEvent.keyDown(screen.getByRole("textbox"), { key: "Escape" });
    expect(summary.closest("details")).toHaveAttribute("open");
    await user.click(screen.getByRole("button", { name: "Outside" }));
    expect(summary.closest("details")).not.toHaveAttribute("open");
  });
});
