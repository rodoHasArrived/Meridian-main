import { WORKSPACES, WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { WorkspaceKey } from "@/types";

export type AppShellWorkspaceErrorMap = Partial<Record<WorkspaceKey, string>>;

export type ShellStatusTone = "loading" | "warning" | "danger";

export interface ShellStatusItem {
  key: string;
  label: string;
  detail: string;
  ariaLabel: string;
}

export interface ShellStatusPanel {
  id: string;
  titleId: string;
  detailId: string;
  tone: ShellStatusTone;
  title: string;
  detail: string;
  role: "status" | "alert";
  ariaLive: "polite" | "assertive";
  actionLabel: string | null;
  actionAriaLabel: string | null;
  secondaryActionLabel: string | null;
  secondaryActionAriaLabel: string | null;
  secondaryActionHref: string | null;
  itemListLabel: string;
  items: ShellStatusItem[];
}

export function buildShellStatusPanel({
  loading,
  error,
  failedItems,
  bootstrapFailed
}: {
  loading: boolean;
  error: string | null;
  failedItems: ShellStatusItem[];
  bootstrapFailed: boolean;
}): ShellStatusPanel | null {
  if (loading) {
    return {
      id: "workstation-shell-status-loading",
      titleId: "workstation-shell-status-loading-title",
      detailId: "workstation-shell-status-loading-detail",
      tone: "loading",
      title: "Preparing workspace",
      detail: "Loading session state, operator workspaces, and the initial evidence views.",
      role: "status",
      ariaLive: "polite",
      actionLabel: null,
      actionAriaLabel: null,
      secondaryActionLabel: null,
      secondaryActionAriaLabel: null,
      secondaryActionHref: null,
      itemListLabel: "Workspace data loading status",
      items: [
        {
          key: "session-state",
          label: "Session state",
          detail: "Resolving operator context and environment guardrails.",
          ariaLabel: "Session state: resolving operator context and environment guardrails"
        },
        {
          key: "workspace-payloads",
          label: "Workspace data",
          detail: "Loading Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings.",
          ariaLabel: "Workspace data: loading Trading, Portfolio, Accounting, Reporting, Strategy, Data, and Settings"
        },
        {
          key: "evidence-slices",
          label: "Evidence slices",
          detail: "Preparing readiness, reconciliation, provider, and report-pack evidence.",
          ariaLabel: "Evidence slices: preparing readiness, reconciliation, provider, and report-pack evidence"
        }
      ]
    };
  }

  if (bootstrapFailed) {
    return {
      id: "workstation-shell-status-failed",
      titleId: "workstation-shell-status-failed-title",
      detailId: "workstation-shell-status-failed-detail",
      tone: "danger",
      title: "Workspace data unavailable",
      detail: formatUserVisibleWorkspaceError(error, "Meridian could not load workspace data. Try again or open diagnostics."),
      role: "alert",
      ariaLive: "assertive",
      actionLabel: "Retry workspace data",
      actionAriaLabel: "Retry workspace data",
      secondaryActionLabel: null,
      secondaryActionAriaLabel: null,
      secondaryActionHref: null,
      itemListLabel: "Workspace data issues",
      items: failedItems
    };
  }

  if (failedItems.length > 0) {
    const areaLabel = failedItems.length === 1 ? "area" : "areas";
    const recoveryLabel = failedItems.length === 1 ? "that area recovers" : "those areas recover";
    return {
      id: "workstation-shell-status-degraded",
      titleId: "workstation-shell-status-degraded-title",
      detailId: "workstation-shell-status-degraded-detail",
      tone: "warning",
      title: "Some workspace data is unavailable",
      detail: `${failedItems.length} workspace ${areaLabel} did not load. Available routes remain open while ${recoveryLabel}.`,
      role: "status",
      ariaLive: "polite",
      actionLabel: "Retry failed areas",
      actionAriaLabel: "Retry failed workspace areas",
      secondaryActionLabel: "Review diagnostics",
      secondaryActionAriaLabel: "Review Settings diagnostics for failed workspace areas",
      secondaryActionHref: WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage,
      itemListLabel: "Workspace data issues",
      items: failedItems
    };
  }

  return null;
}

export function buildShellFailureItems(
  workspaceErrors: AppShellWorkspaceErrorMap,
  workflowError: string | null
): ShellStatusItem[] {
  const items: ShellStatusItem[] = Object.entries(workspaceErrors)
    .map(([key, detail]) => {
      const workspaceKey = key as WorkspaceKey;
      const label = WORKSPACES.find((workspace) => workspace.key === workspaceKey)?.label ?? key;
      const visibleDetail = formatUserVisibleWorkspaceError(detail, "Workspace data unavailable. Try again or open diagnostics.");
      return {
        key: workspaceKey,
        label,
        detail: visibleDetail,
        ariaLabel: `${label}: ${visibleDetail}`
      };
    })
    .sort((left, right) => left.label.localeCompare(right.label));

  if (workflowError) {
    const visibleDetail = formatUserVisibleWorkspaceError(workflowError, "Workflow data unavailable. Try again or open diagnostics.");
    items.push({
      key: "workflow-catalog",
      label: "Workflow catalog",
      detail: visibleDetail,
      ariaLabel: `Workflow catalog: ${visibleDetail}`
    });
  }

  return items;
}

function formatUserVisibleWorkspaceError(error: string | null | undefined, fallback: string): string {
  const detail = error?.trim();
  if (!detail) {
    return fallback;
  }

  return looksLikeRawTechnicalResponse(detail) ? fallback : detail;
}

function looksLikeRawTechnicalResponse(value: string): boolean {
  return /<!doctype\s+html/i.test(value)
    || /<html(?:\s|>)/i.test(value)
    || /\bfile not found\b/i.test(value)
    || /^404(?:\s|$|:|-)/i.test(value)
    || /\bhttp\s+error\s+404\b/i.test(value);
}
