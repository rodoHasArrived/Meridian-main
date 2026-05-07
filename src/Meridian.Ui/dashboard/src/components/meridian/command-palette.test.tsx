import { fireEvent, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { CommandPalette } from "@/components/meridian/command-palette";
import { renderWithRouter } from "@/test/render";
import type { WorkflowLibrary, WorkflowPresetLibrary } from "@/types";

describe("CommandPalette", () => {
  it("marks the route-aware current workspace", () => {
    renderWithRouter(<CommandPalette open onOpenChange={vi.fn()} />, { initialEntries: ["/portfolio/positions"] });

    expect(screen.getByRole("dialog", { name: "Open workspace" })).toBeInTheDocument();
    expect(screen.getByText("Route to a canonical operator workspace. Current: Portfolio.")).toBeInTheDocument();
    expect(screen.getByRole("navigation", { name: "7 workspace commands" })).toBeInTheDocument();
    expect(screen.getByText("Esc to close")).toBeInTheDocument();
    expect(screen.getByLabelText("Route /portfolio")).toBeInTheDocument();
    expect(screen.getByLabelText("Portfolio, current workspace")).toHaveAttribute("aria-current", "page");
    expect(screen.getByLabelText("Portfolio, current workspace")).toHaveFocus();
  });

  it("closes when Escape is pressed", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    renderWithRouter(<CommandPalette open onOpenChange={onOpenChange} />, { initialEntries: ["/settings"] });

    await user.keyboard("{Escape}");

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("closes when a workspace command is selected", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    renderWithRouter(<CommandPalette open onOpenChange={onOpenChange} />, { initialEntries: ["/trading"] });

    await user.click(screen.getByLabelText("Open Settings workspace"));

    expect(onOpenChange).toHaveBeenCalledWith(false);
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

    expect(screen.getByRole("navigation", { name: "9 commands" })).toBeInTheDocument();
    expect(screen.getByText("1 workflow action - 1 preset")).toBeInTheDocument();
    expect(screen.getByLabelText("Review Security Master, Data Provider Recovery")).toHaveAttribute("href", "/data/security-master");

    await user.click(screen.getByLabelText("Open workflow preset Security open items"));

    expect(onPresetUsed).toHaveBeenCalledWith("preset-1");
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
