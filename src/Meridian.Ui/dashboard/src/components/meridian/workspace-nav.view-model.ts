import { isWorkspacePathActive, WORKSPACES, workspacePath } from "@/lib/workspace";
import type { WorkspaceKey, WorkspaceSummary } from "@/types";

export interface WorkspaceNavItemViewModel {
  key: WorkspaceKey;
  label: string;
  description: string;
  statusLabel: string;
  statusTone: WorkspaceNavStatusTone;
  route: string;
  active: boolean;
  ariaCurrent: "page" | undefined;
  ariaLabel: string;
}

export interface WorkspaceNavCurrentWorkspaceViewModel {
  label: string;
  description: string;
  statusLabel: string;
  statusTone: WorkspaceNavStatusTone;
  ariaLabel: string;
}

export interface WorkspaceNavViewModel {
  brandTitle: string;
  brandSubtitle: string;
  modelEyebrow: string;
  modelDescription: string;
  currentWorkspace: WorkspaceNavCurrentWorkspaceViewModel;
  navEyebrow: string;
  deliveryEyebrow: string;
  deliveryTitle: string;
  deliveryDescription: string;
  items: WorkspaceNavItemViewModel[];
}

export type WorkspaceNavStatusTone = "live" | "review" | "paper" | "preview" | "setup";

export function buildWorkspaceNavViewModel(
  pathname: string,
  workspaces: WorkspaceSummary[] = WORKSPACES
): WorkspaceNavViewModel {
  const currentWorkspace =
    workspaces.find((workspace) => isWorkspacePathActive(pathname, workspace.key)) ?? workspaces[0];

  const items = workspaces.map<WorkspaceNavItemViewModel>((workspace) => {
    const active = isWorkspacePathActive(pathname, workspace.key);
    const statusTone = workspaceStatusTone(workspace.status);

    return {
      key: workspace.key,
      label: workspace.label,
      description: workspace.description,
      statusLabel: active ? `${workspace.status} · Current` : workspace.status,
      statusTone,
      route: workspacePath(workspace.key),
      active,
      ariaCurrent: active ? "page" : undefined,
      ariaLabel: active
        ? `${workspace.label} workspace, current route, ${workspace.status}`
        : `Open ${workspace.label} workspace, ${workspace.status}`
    };
  });

  return {
    brandTitle: "Meridian",
    brandSubtitle: "Operator Workstation",
    modelEyebrow: "Operating model",
    modelDescription:
      "Workflow-centric shell for trading, portfolio, accounting, reporting, strategy, data, and settings posture.",
    currentWorkspace: {
      label: currentWorkspace.label,
      description: currentWorkspace.description,
      statusLabel: `${currentWorkspace.status} posture`,
      statusTone: workspaceStatusTone(currentWorkspace.status),
      ariaLabel: `Current workspace: ${currentWorkspace.label}, ${currentWorkspace.status} posture`
    },
    navEyebrow: "Workspaces",
    deliveryEyebrow: "Web delivery",
    deliveryTitle: "Seven-workspace operator lane",
    deliveryDescription:
      "Browser navigation follows the canonical workstation taxonomy while legacy route aliases stay available.",
    items
  };
}

function workspaceStatusTone(status: string): WorkspaceNavStatusTone {
  switch (status.toLowerCase()) {
    case "live":
      return "live";
    case "paper":
      return "paper";
    case "preview":
      return "preview";
    case "setup":
      return "setup";
    default:
      return "review";
  }
}
