import { fireEvent, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { MegaMenu } from "@/components/meridian/mega-menu";
import { renderWithRouter } from "@/test/render";

describe("MegaMenu", () => {
  it("opens with active route state and closes from Escape with focus restored", async () => {
    const user = userEvent.setup();
    renderWithRouter(<MegaMenu />, { initialEntries: ["/strategy/quant-lab"] });

    const trigger = screen.getByRole("button", { name: "Open workspace navigation menu" });
    trigger.focus();
    await user.click(trigger);

    expect(screen.getByRole("dialog", { name: "Workspace navigation" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Close workspace navigation menu" })).toHaveAttribute(
      "aria-expanded",
      "true"
    );
    expect(screen.getByRole("link", { name: "Quant Lab, current route, Strategy workspace" })).toHaveAttribute(
      "aria-current",
      "page"
    );

    fireEvent.keyDown(window, { key: "Escape" });

    expect(screen.queryByRole("dialog", { name: "Workspace navigation" })).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });

  it("contains keyboard tab flow inside the open menu", async () => {
    const user = userEvent.setup();
    renderWithRouter(<MegaMenu />, { initialEntries: ["/data/backfill"] });

    await user.click(screen.getByRole("button", { name: "Open workspace navigation menu" }));
    const firstLink = screen.getByRole("link", { name: "Trading" });
    const lastLink = screen.getByRole("link", { name: "Open Integrations, Settings workspace" });

    await waitFor(() => expect(firstLink).toHaveFocus());

    fireEvent.keyDown(window, { key: "Tab", shiftKey: true });
    expect(lastLink).toHaveFocus();

    fireEvent.keyDown(window, { key: "Tab" });
    expect(firstLink).toHaveFocus();
  });

  it("closes when a workspace route is selected", async () => {
    const user = userEvent.setup();
    renderWithRouter(<MegaMenu />, { initialEntries: ["/portfolio"] });

    await user.click(screen.getByRole("button", { name: "Open workspace navigation menu" }));
    await user.click(screen.getByRole("link", { name: "Open Reconciliation, Accounting workspace" }));

    expect(screen.queryByRole("dialog", { name: "Workspace navigation" })).not.toBeInTheDocument();
  });
});
