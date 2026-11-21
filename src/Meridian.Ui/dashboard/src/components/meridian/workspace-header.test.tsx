import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { WorkspaceHeader } from "@/components/meridian/workspace-header";
import { workspaceForKey } from "@/lib/workspace";
import type { SessionInfo } from "@/types";

const session: SessionInfo = {
  displayName: "Ops Desk",
  role: "Operator",
  environment: "paper",
  activeWorkspace: "trading",
  commandCount: 8
};

describe("WorkspaceHeader", () => {
  it("renders the breadcrumb ending in the page heading and the loading refresh state", () => {
    render(
      <WorkspaceHeader
        breadcrumbItems={[{ label: "Trading", onClick: vi.fn() }, { label: "Orders", current: true }]}
        workspace={workspaceForKey("trading")}
        session={session}
        onRefresh={vi.fn()}
        refreshing
      />
    );

    // One heading: the page name that ends the breadcrumb, not "Trading Workstation".
    const headings = screen.getAllByRole("heading");
    expect(headings).toHaveLength(1);
    expect(headings[0]).toHaveTextContent("Orders");
    expect(headings[0]).toHaveAttribute("aria-current", "page");
    const breadcrumb = screen.getByLabelText("Breadcrumb");
    expect(breadcrumb).toContainElement(headings[0]);
    expect(within(breadcrumb).getByRole("button", { name: "Trading" })).toBeEnabled();
    expect(screen.queryByText("Workstation")).not.toBeInTheDocument();
    expect(screen.queryByText("Trading Workstation")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("paper environment")).not.toBeInTheDocument();
    // Product maturity is not a page badge: it lives in the About section of the overflow menu.
    expect(screen.queryByLabelText("Trading product maturity Available")).not.toBeInTheDocument();
    expect(within(screen.getByLabelText("About Trading")).getByText("Product maturity: Available.")).toBeInTheDocument();
    expect(screen.getByLabelText("Session Ops Desk, role Operator")).toHaveTextContent("Ops Desk");
    const refreshButton = screen.getByRole("button", { name: "Refreshing Trading workspace data" });
    expect(refreshButton).toBeDisabled();
    expect(refreshButton).toHaveAttribute("title", "Trading workspace data is refreshing.");
    expect(refreshButton.querySelector("svg")).toHaveClass("animate-spin");
    expect(screen.queryByLabelText("Canonical route /trading")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("8 commands available in the command palette")).not.toBeInTheDocument();
    expect(screen.getByText("Refreshing Trading workspace data.")).toHaveClass("sr-only");
  });

  it("names a workspace root by the workspace alone", () => {
    render(
      <WorkspaceHeader
        breadcrumbItems={[{ label: "Trading", current: true }]}
        workspace={workspaceForKey("trading")}
        session={session}
      />
    );

    expect(screen.getByRole("heading", { name: "Trading" })).toBeInTheDocument();
    expect(within(screen.getByLabelText("Breadcrumb")).queryByRole("button")).not.toBeInTheDocument();
  });

  it("orders the actions Refresh, shell actions, then the overflow menu", () => {
    render(
      <WorkspaceHeader
        workspace={workspaceForKey("trading")}
        session={session}
        onRefresh={vi.fn()}
        actions={<button type="button">Views</button>}
        overflowActions={<button type="button">Copy link</button>}
      />
    );

    const buttons = screen.getAllByRole("button");
    expect(buttons[0]).toHaveAccessibleName("Refresh Trading workspace data");
    expect(buttons[1]).toHaveTextContent("Views");
    const overflow = screen.getByRole("region", { name: "More actions", hidden: true });
    expect(overflow).toContainElement(screen.getByRole("button", { name: "Copy link", hidden: true }));
  });

  it("keeps scope visible while Copy link and the workspace description sit in the overflow menu", async () => {
    const user = userEvent.setup();
    render(
      <WorkspaceHeader
        workspace={workspaceForKey("trading")}
        session={session}
        scope={<p>MSFT</p>}
        overflowActions={<button type="button">Copy link</button>}
      />
    );

    expect(screen.getByText("MSFT")).toBeVisible();
    const overflow = screen.getByLabelText("More actions", { selector: "summary" });
    const details = overflow.closest("details");
    expect(details).not.toHaveAttribute("open");
    await user.click(overflow);
    expect(details).toHaveAttribute("open");
    expect(screen.getByText(workspaceForKey("trading").description)).toBeVisible();
    // A one-shot command closes the menu behind it.
    await user.click(screen.getByRole("button", { name: "Copy link" }));
    expect(details).not.toHaveAttribute("open");
  });
});
