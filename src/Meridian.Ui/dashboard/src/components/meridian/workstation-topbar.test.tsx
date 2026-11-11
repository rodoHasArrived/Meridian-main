import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { vi } from "vitest";
import { WorkstationTopbar } from "./workstation-topbar";
import type { AppShellTrustStripState } from "@/app-shell.view-model";
import type { SessionInfo } from "@/types";

const trustStrip: AppShellTrustStripState = {
  ariaLabel: "Trust status",
  environment: { label: "PAPER", variant: "paper", dataLabel: "SEEDED" },
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
    // The workspace is announced, not shown: the header breadcrumb is its one visible name.
    expect(screen.getByText("Current workspace: Accounting")).toHaveClass("sr-only");
    const navToggle = screen.getByRole("button", { name: "Open workspace navigation" });
    expect(navToggle).toHaveAttribute("aria-expanded", "false");
    await userEvent.click(navToggle);
    expect(onOpenNavigation).toHaveBeenCalledTimes(1);

    const command = screen.getByRole("button", { name: "Search commands Ctrl K" });
    expect(command).toHaveAccessibleDescription("Open command search. Keyboard shortcut: Ctrl K.");
    expect(command).toHaveAttribute("aria-controls", "command-palette");
    expect(command).toHaveAttribute("aria-haspopup", "dialog");
    await userEvent.click(command);
    expect(onOpenCommandPalette).toHaveBeenCalledTimes(1);

    expect(screen.getByRole("region", { name: "Trust status" })).toHaveAttribute("aria-live", "polite");
    const summary = screen.getByText("PAPER").closest("summary");
    expect(summary).not.toHaveAttribute("aria-label");
    expect(summary).toHaveAccessibleName(expect.stringMatching(/^PAPER\s*SEEDED.*not real market or account data/));
    expect(summary).toHaveAccessibleDescription(expect.stringMatching(/^Environment, provenance, and provider posture\./));
    await userEvent.click(screen.getByText("PAPER"));
    expect(screen.getByRole("link", { name: "Mode Paper Open settings." })).toHaveAttribute("href", "/settings");
    expect(screen.getByText("Provider")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Activity" })).toBeInTheDocument();
    expect(screen.getByRole("group", { name: "Current session: Avery Stone, Portfolio operator" }))
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
          trustStrip={{ ariaLabel: "Trust status", environment: { label: "LOADING", variant: "default", dataLabel: null }, items: [] }}
          session={null}
        />
      </MemoryRouter>
    );

    expect(screen.getByText("Loading session...")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Open workspace navigation" })).toHaveAttribute("aria-expanded", "true");
  });
});

it("keeps provenance and a governing blocker visible in one closed status control", async () => {
  const { container } = render(<MemoryRouter><WorkstationTopbar
    brandMarkSrc="/brand.svg" workspaceLabel="Trading" navOpen={false} onOpenNavigation={vi.fn()}
    commandTrigger={{ label: "Commands", placeholder: "Search", shortcutLabel: "Ctrl K", controlsId: "commands", expanded: false, hasPopup: "dialog" }}
    onOpenCommandPalette={vi.fn()} trustStrip={trustStrip} session={session} provenance="seeded"
    decisionBrief={{
      label: "Decision", title: "Resolve cash variance", summary: "Cash differs from retained evidence", reasonLabel: "Why", reason: "Review evidence",
      statusLabel: "Blocked", statusTone: "blocked", evidenceLabel: "Evidence", actionLabel: "Open case",
      actionHref: "/accounting/reconciliation", actionAriaLabel: "Open cash case"
    }}
  /></MemoryRouter>);
  const summary = container.querySelector(".workstation-trust-summary")!;
  expect(summary).toHaveTextContent("SEEDED");
  expect(summary).toHaveTextContent("Blocked");
  expect(summary).toHaveTextContent("Resolve cash variance");
  expect(summary).toHaveClass("workstation-trust-item-blocked");
  expect(container.querySelectorAll(".workstation-trust-summary")).toHaveLength(1);
  expect(container.querySelector("header")).toHaveAttribute("data-provenance", "seeded");
  expect(screen.queryByRole("button", { name: /dismiss/i })).not.toBeInTheDocument();
  await userEvent.click(summary);
  expect(screen.getByRole("link", { name: "Open cash case" })).toHaveAttribute("href", "/accounting/reconciliation");
  await userEvent.keyboard("{Escape}");
  expect(summary.closest("details")).not.toHaveAttribute("open");
  expect(summary).toHaveFocus();
});

it("offers unknown-provenance retry through the same status disclosure", async () => {
  const retry = vi.fn();
  const { container } = render(<MemoryRouter><WorkstationTopbar
    brandMarkSrc="/brand.svg" workspaceLabel="Trading" navOpen={false} onOpenNavigation={vi.fn()}
    commandTrigger={{ label: "Commands", placeholder: "Search", shortcutLabel: "Ctrl K", controlsId: "commands", expanded: false, hasPopup: "dialog" }}
    onOpenCommandPalette={vi.fn()} trustStrip={trustStrip} session={session} provenance="unknown" onRetryLiveData={retry}
  /></MemoryRouter>);
  expect(screen.getByTestId("data-provenance-unknown")).toBeVisible();
  await userEvent.click(container.querySelector(".workstation-trust-summary")!);
  await userEvent.click(screen.getByRole("button", { name: "Retry live Meridian workspace data" }));
  expect(retry).toHaveBeenCalledOnce();
});
