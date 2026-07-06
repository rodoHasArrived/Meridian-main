import { cleanup, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SaveViewButton } from "@/components/meridian/save-view-dialog";
import { ToastProvider } from "@/components/ui/toast";
import { saveWorkflowPreset } from "@/lib/api";
import { renderWithRouter } from "@/test/render";
import type { WorkflowAction, WorkflowDefinition, WorkflowLibrary, WorkflowPreset } from "@/types";

vi.mock("@/lib/api", () => ({
  saveWorkflowPreset: vi.fn()
}));

const saveWorkflowPresetMock = vi.mocked(saveWorkflowPreset);

const reportingAction: WorkflowAction = {
  actionId: "reporting-exports",
  aliases: [],
  detail: "Run governed reporting exports.",
  label: "Reporting exports",
  routeContains: [],
  routePrefixes: ["/reporting"],
  targetPageTag: "Reporting",
  tone: "default",
  workItemKind: null
};

const reportingWorkflow: WorkflowDefinition = {
  actions: [reportingAction],
  entryPageTag: "Reporting",
  evidenceTags: [],
  marketPatternTags: [],
  summary: "Reporting workflow",
  title: "Reporting",
  tone: "default",
  workflowId: "reporting",
  workspaceId: "reporting",
  workspaceTitle: "Reporting"
};

const workflowLibrary: WorkflowLibrary = {
  actions: [reportingAction],
  generatedAt: "2026-07-03T00:00:00Z",
  workflows: [reportingWorkflow]
};

function savedPreset(overrides: Partial<WorkflowPreset> = {}): WorkflowPreset {
  return {
    actionId: reportingAction.actionId,
    actionLabel: reportingAction.label,
    createdAt: "2026-07-03T00:01:00Z",
    description: null,
    isPinned: false,
    lastUsedAt: null,
    name: "Month-end breaks",
    presetId: "preset-1",
    tags: [],
    targetPageTag: reportingAction.targetPageTag,
    updatedAt: "2026-07-03T00:01:00Z",
    viewStateEnvelope: "encoded-view-token",
    workflowId: reportingWorkflow.workflowId,
    workflowTitle: reportingWorkflow.title,
    workspaceId: reportingWorkflow.workspaceId,
    workspaceTitle: reportingWorkflow.workspaceTitle,
    ...overrides
  };
}

function renderSaveViewButton(initialEntry: string, onPresetSaved = vi.fn()) {
  renderWithRouter(
    <ToastProvider>
      <SaveViewButton workflowLibrary={workflowLibrary} onPresetSaved={onPresetSaved} />
    </ToastProvider>,
    { initialEntries: [initialEntry] }
  );

  return { onPresetSaved };
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("SaveViewButton", () => {
  it("saves the current route view state as a workflow preset", async () => {
    const user = userEvent.setup();
    const preset = savedPreset();
    saveWorkflowPresetMock.mockResolvedValueOnce(preset);
    const { onPresetSaved } = renderSaveViewButton("/reporting/exports?fundAccountId=fund-alpha&view=encoded-view-token");

    await user.click(screen.getByRole("button", { name: "Save this view as a preset" }));
    await user.type(screen.getByLabelText("View name"), "  Month-end breaks  ");
    await user.type(screen.getByLabelText("Description (optional)"), "Open month-end exceptions");
    await user.click(screen.getByRole("button", { name: "Save view" }));

    await waitFor(() => expect(saveWorkflowPresetMock).toHaveBeenCalledWith({
      actionId: reportingAction.actionId,
      description: "Open month-end exceptions",
      isPinned: false,
      name: "Month-end breaks",
      tags: [],
      viewStateEnvelope: "encoded-view-token",
      workflowId: reportingWorkflow.workflowId
    }));
    expect(onPresetSaved).toHaveBeenCalledWith(preset);
    expect(await screen.findByText("Saved view Month-end breaks.")).toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: "Save this view" })).not.toBeInTheDocument();
  });

  it("stays disabled when no workflow action owns the current route", () => {
    renderSaveViewButton("/unowned-route");

    expect(screen.getByRole("button", { name: "Save this view as a preset" })).toBeDisabled();
    expect(saveWorkflowPresetMock).not.toHaveBeenCalled();
  });
});
