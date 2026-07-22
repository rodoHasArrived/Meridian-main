import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { vi } from "vitest";
import { WorkstationTopbar } from "./workstation-topbar";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import type { SessionInfo } from "@/types";

const trustStrip: AppShellTrustStripState = {
  ariaLabel: "Trust status",
  items: [
    {
      id: "mode",
      label: "Mode",
      value: "Paper",
      detail: "Paper trading mode",
      tone: "ready",
      ariaLabel: "Mode Paper",
      href: "/settings",
      actionLabel: "Open settings"
    },
    {
      id: "provider",
      label: "Provider",
      value: "Review",
      detail: "Provider review required",
      tone: "review",
      ariaLabel: "Provider Review",
      href: null,
      actionLabel: null
    }
  ]
};

const session: SessionInfo = {
  displayName: "Avery Stone",
  role: "Portfolio operator",
  environment: "paper",
  activeWorkspace: "accounting",
  commandCount: 12
};

describe("WorkstationTopbar", () => {
  it("renders brand, workspace, command trigger, trust strip, actions, and session card", async () => {
    const onOpenNavigation = vi.fn();
    const onOpenCommandPalette = vi.fn();

    render(
      <MemoryRouter>
        <WorkstationTopbar
          brandMarkSrc="/brand-mark.svg"
          workspaceLabel="Accounting"
          navOpen={false}
          onOpenNavigation={onOpenNavigation}
          commandTrigger={{
            label: "Open command search",
            placeholder: "Search commands",
            shortcutLabel: "Ctrl K",
            controlsId: "command-palette",
            expanded: false,
            hasPopup: "dialog"
          }}
          onOpenCommandPalette={onOpenCommandPalette}
          trustStrip={trustStrip}
          session={session}
          actions={<button type="button">Activity</button>}
        />
      </MemoryRouter>
    );

    expect(screen.getByText("Meridian")).toBeInTheDocument();
    expect(screen.getByText("Accounting")).toBeInTheDocument();
    const navToggle = screen.getByRole("button", { name: "Open workspace navigation" });
    expect(navToggle).toHaveAttribute("aria-expanded", "false");
    await userEvent.click(navToggle);
    expect(onOpenNavigation).toHaveBeenCalledTimes(1);

    const command = screen.getByRole("button", { name: "Open command search" });
    expect(command).toHaveAttribute("aria-controls", "command-palette");
    expect(command).toHaveAttribute("aria-haspopup", "dialog");
    await userEvent.click(command);
    expect(onOpenCommandPalette).toHaveBeenCalledTimes(1);

    expect(screen.getByRole("region", { name: "Trust status" })).toBeInTheDocument();
    await userEvent.click(screen.getByText("Environment"));
    expect(screen.getByRole("link", { name: "Mode Paper Open settings." })).toHaveAttribute("href", "/settings");
    expect(screen.getByText("Provider")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Activity" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Current session: paper, Avery Stone, Portfolio operator" }))
      .toBeInTheDocument();
  });

  it("renders a loading session fallback", () => {
    render(
      <MemoryRouter>
        <WorkstationTopbar
          brandMarkSrc="/brand-mark.svg"
          workspaceLabel="Data"
          navOpen
          onOpenNavigation={vi.fn()}
          commandTrigger={{
            label: "Open commands",
            placeholder: "Search",
            shortcutLabel: "Ctrl K",
            controlsId: "commands",
            expanded: true,
            hasPopup: "dialog"
          }}
          onOpenCommandPalette={vi.fn()}
          trustStrip={{ ariaLabel: "Trust status", items: [] }}
          session={null}
        />
      </MemoryRouter>
    );

    expect(screen.getByText("Loading session...")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Open workspace navigation" })).toHaveAttribute("aria-expanded", "true");
  });
});
