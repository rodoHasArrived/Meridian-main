import { fireEvent, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { CommandPalette } from "@/components/meridian/command-palette";
import { renderWithRouter } from "@/test/render";
import type { WorkflowLibrary, WorkflowPresetLibrary } from "@/types";

describe("CommandPalette", () => {
  it("marks the route-aware current workspace", () => {
    renderWithRouter(<CommandPalette open onOpenChange={vi.fn()} />, { initialEntries: ["/portfolio/positions"] });

    expect(screen.getByRole("dialog", { name: "Open workstation command" })).toBeInTheDocument();
    expect(screen.getByText("Route to common operator workflows and canonical workspaces. Current: Portfolio.")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "20 workstation commands" })).toBeInTheDocument();
    expect(screen.getByText("Esc to close")).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Search command palette" })).toHaveFocus();
    expect(screen.getByText("20 commands available")).toBeInTheDocument();
    expect(screen.getByLabelText("Workspaces: 7 workspaces")).toBeInTheDocument();
    expect(screen.getByLabelText("Quick routes: 13 quick routes")).toBeInTheDocument();
    expect(screen.getByLabelText("Route /portfolio")).toBeInTheDocument();
    expect(screen.getByLabelText("Portfolio, current workspace")).toHaveAttribute("aria-current", "page");
  });

  it("closes when Escape is pressed", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    renderWithRouter(<CommandPalette open onOpenChange={onOpenChange} />, { initialEntries: ["/settings"] });

    await user.keyboard("{Escape}");

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("keeps Tab focus inside the modal command surface", () => {
    const onOpenChange = vi.fn();

    renderWithRouter(<CommandPalette open onOpenChange={onOpenChange} />, { initialEntries: ["/settings#alpaca-provider-setup"] });

    const closeButton = screen.getByRole("button", { name: "Close command palette" });
    const lastCommand = screen.getByLabelText("Alpaca provider setup, current route");

    lastCommand.focus();
    fireEvent.keyDown(window, { key: "Tab" });
    expect(closeButton).toHaveFocus();

    fireEvent.keyDown(window, { key: "Tab", shiftKey: true });
    expect(lastCommand).toHaveFocus();
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it("does not mark the Alpaca setup command current from another Settings anchor", () => {
    renderWithRouter(<CommandPalette open onOpenChange={vi.fn()} />, {
      initialEntries: ["/settings#backend-capability-coverage"]
    });

    expect(screen.getByLabelText("Settings, current workspace")).toHaveAttribute("aria-current", "page");
    expect(screen.getByLabelText("Open Alpaca provider setup route")).toHaveAttribute(
      "href",
      "/settings#alpaca-provider-setup"
    );
    expect(screen.queryByLabelText("Alpaca provider setup, current route")).not.toBeInTheDocument();
  });

  it("closes when a workspace command is selected", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    renderWithRouter(<CommandPalette open onOpenChange={onOpenChange} />, { initialEntries: ["/trading"] });

    await user.click(screen.getByLabelText("Open Settings workspace"));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("filters command results from the search field", async () => {
    const user = userEvent.setup();

    renderWithRouter(<CommandPalette open onOpenChange={vi.fn()} />, { initialEntries: ["/trading"] });

    await user.type(screen.getByRole("searchbox", { name: "Search command palette" }), "settings");

    expect(screen.getByText("2 of 20 commands match")).toBeInTheDocument();
    expect(screen.getByLabelText("Workspaces: 1 workspace")).toBeInTheDocument();
    expect(screen.getByLabelText("Quick routes: 1 quick route")).toBeInTheDocument();
    expect(screen.getByLabelText("Open Settings workspace")).toBeInTheDocument();
    expect(screen.getByLabelText("Open Alpaca provider setup route")).toHaveAttribute(
      "href",
      "/settings#alpaca-provider-setup"
    );
    expect(screen.queryByLabelText("Trading, current workspace")).not.toBeInTheDocument();
  });

  it("opens the first filtered command when Enter is pressed from search", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    renderWithRouter(<CommandPalette open onOpenChange={onOpenChange} />, { initialEntries: ["/trading"] });

    await user.type(screen.getByRole("searchbox", { name: "Search command palette" }), "settings{Enter}");

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("keeps the palette open when Enter is pressed with no command matches", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    renderWithRouter(<CommandPalette open onOpenChange={onOpenChange} />, { initialEntries: ["/trading"] });

    await user.type(screen.getByRole("searchbox", { name: "Search command palette" }), "missing command{Enter}");

    expect(screen.getByText("No matching commands")).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it("moves through commands with arrow keys from the search field", () => {
    renderWithRouter(<CommandPalette open onOpenChange={vi.fn()} />, { initialEntries: ["/trading"] });

    const search = screen.getByRole("searchbox", { name: "Search command palette" });
    const currentTrading = screen.getByLabelText("Trading, current workspace");
    const portfolio = screen.getByLabelText("Open Portfolio workspace");

    expect(search).toHaveFocus();

    fireEvent.keyDown(window, { key: "ArrowDown" });
    expect(currentTrading).toHaveFocus();

    fireEvent.keyDown(window, { key: "ArrowDown" });
    expect(portfolio).toHaveFocus();

    fireEvent.keyDown(window, { key: "ArrowUp" });
    expect(currentTrading).toHaveFocus();
  });

  it("shows an empty state when command filtering has no matches", async () => {
    const user = userEvent.setup();

    renderWithRouter(<CommandPalette open onOpenChange={vi.fn()} />, { initialEntries: ["/trading"] });

    await user.type(screen.getByRole("searchbox", { name: "Search command palette" }), "missing command");

    expect(screen.getByText("0 of 20 commands match")).toBeInTheDocument();
    expect(screen.getByText("No matching commands")).toBeInTheDocument();
  });

  it("renders ranked operator focus actions as first-class commands", () => {
    renderWithRouter(
      <CommandPalette
        open
        onOpenChange={vi.fn()}
        operatorFocusItems={[
          {
            id: "work-item:brokerage-sync",
            label: "Brokerage sync failed",
            detail: "Account sync failed after the last provider heartbeat.",
            route: "/settings#alpaca-provider-setup",
            workspaceLabel: "Settings",
            actionLabel: "Fix provider setup",
            tone: "blocked",
            ariaLabel: "Settings: Brokerage sync failed. Account sync failed after the last provider heartbeat. Fix provider setup."
          }
        ]}
      />,
      { initialEntries: ["/data/quotes?symbol=MSFT"] }
    );

    expect(screen.getByRole("navigation", { name: "21 workstation commands" })).toBeInTheDocument();
    expect(screen.getByLabelText("Focus actions: 1 focus action")).toBeInTheDocument();
    expect(screen.getByText(/1 ranked focus action available\./)).toBeInTheDocument();
    expect(screen.getByLabelText(
      "Settings: Brokerage sync failed. Account sync failed after the last provider heartbeat. Fix provider setup."
    )).toHaveAttribute("href", "/settings#alpaca-provider-setup");
    expect(screen.getByText("Blocked")).toBeInTheDocument();
    expect(screen.getByText("Fix provider setup")).toBeInTheDocument();
  });

  it("closes when the backdrop is selected", () => {
    const onOpenChange = vi.fn();

    renderWithRouter(<CommandPalette open onOpenChange={onOpenChange} />, { initialEntries: ["/trading"] });

    fireEvent.click(screen.getByTestId("command-palette-backdrop"));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("renders backend workflow commands and records pinned preset launches", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    const onPresetUsed = vi.fn().mockResolvedValue(undefined);
    const workflowLibrary: WorkflowLibrary = {
      generatedAt: "2026-01-01T00:00:00Z",
      actions: [],
      workflows: [
        {
          workflowId: "data-provider-recovery",
          title: "Data Provider Recovery",
          summary: "Review provider health.",
          workspaceId: "data",
          workspaceTitle: "Data",
          entryPageTag: "DataShell",
          tone: "Warning",
          evidenceTags: ["provider metrics"],
          marketPatternTags: ["provider dashboard"],
          actions: [
            {
              actionId: "workflow.data.review-security-master",
              label: "Review Security Master",
              detail: "Review reference-data coverage and symbol lifecycle issues.",
              targetPageTag: "SecurityMaster",
              tone: "Warning",
              workItemKind: "SecurityMasterCoverage" as const,
              routePrefixes: [],
              routeContains: [],
              aliases: []
            }
          ]
        }
      ]
    };
    const workflowPresets: WorkflowPresetLibrary = {
      generatedAt: "2026-01-01T00:00:00Z",
      presets: [
        {
          presetId: "preset-1",
          name: "Security open items",
          description: "Pinned data workflow",
          workflowId: "data-provider-recovery",
          workflowTitle: "Data Provider Recovery",
          actionId: "workflow.data.review-security-master",
          actionLabel: "Review Security Master",
          workspaceId: "data",
          workspaceTitle: "Data",
          targetPageTag: "SecurityMaster",
          tags: ["security"],
          isPinned: true,
          createdAt: "2026-01-01T00:00:00Z",
          updatedAt: "2026-01-01T00:00:00Z",
          lastUsedAt: null
        }
      ]
    };

    renderWithRouter(
      <CommandPalette
        open
        onOpenChange={onOpenChange}
        workflowLibrary={workflowLibrary}
        workflowPresets={workflowPresets}
        onPresetUsed={onPresetUsed}
      />,
      { initialEntries: ["/trading"] }
    );

    expect(screen.getByRole("navigation", { name: "22 commands" })).toBeInTheDocument();
    expect(screen.getByText("1 workflow action - 1 preset")).toBeInTheDocument();
    expect(screen.getByLabelText("Review Security Master, Data Provider Recovery")).toHaveAttribute("href", "/accounting/security-master");

    await user.click(screen.getByLabelText("Open workflow preset Security open items"));

    expect(onPresetUsed).toHaveBeenCalledWith("preset-1");
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
