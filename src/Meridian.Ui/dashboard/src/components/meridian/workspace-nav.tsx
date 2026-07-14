import { useLocation } from "react-router-dom";
import "@/styles/workspace-nav.css";
import { useWorkspaceExpansion } from "@/components/meridian/use-workspace-expansion";
import { buildWorkspaceNavViewModel } from "@/components/meridian/workspace-nav.view-model";
import { DesignSystemNavRail } from "@/design-system/primitives";
import { meridianWorkspaceIconAssets } from "@/design-system/assets";
import type { AppShellOperatingScopeInput } from "@/app-shell.operating-scope";
import type { WorkspaceKey } from "@/types";

/**
 * Left-rail operator navigation sidebar. Renders as a fixed-width `<aside>` inside the
 * `.workstation-shell` grid and uses `.operator-rail` CSS from the Track B surface system.
 *
 * All data is derived from `useLocation()` via `buildWorkspaceNavViewModel`. Optional props allow
 * host surfaces such as drawers to add wrapper classes and close after navigation.
 *
 * The rail intentionally avoids repeating the masthead brand or workspace header context. It keeps
 * one scannable list of the seven root workspaces, with the active item carrying `aria-current`.
 *
 * **Status tones** for nav items are one of:
 * `"live"`, `"review"`, `"paper"`, `"preview"`, `"setup"`, or `"muted"` — each has a
 * matching `.operator-nav-status-*` CSS modifier.
 *
 * @example
 * // Mount inside the .workstation-shell grid:
 * <WorkspaceNav />
 */
interface WorkspaceNavProps {
  className?: string;
  density?: "compact" | "detailed";
  onNavigate?: () => void;
  operatingContextScope?: AppShellOperatingScopeInput | null;
}

export function WorkspaceNav({
  className,
  density = "detailed",
  onNavigate,
  operatingContextScope = null
}: WorkspaceNavProps) {
  const location = useLocation();
  const viewModel = buildWorkspaceNavViewModel(location.pathname, undefined, location.search, operatingContextScope);
  const activeWorkspaceKey = viewModel.items.find((item) => item.active)?.key ?? viewModel.items[0]?.key;
  const { expandedWorkspaces, toggleWorkspace } = useWorkspaceExpansion(activeWorkspaceKey);

  const items = viewModel.items.map((item) => ({
    ...item,
    iconSrc: meridianWorkspaceIconAssets[item.key]
  }));

  return (
    <DesignSystemNavRail
      brandTitle={viewModel.brandTitle}
      navEyebrow={viewModel.navEyebrow}
      items={items}
      density={density}
      className={className}
      operatingScopeLabel={viewModel.operatingScopeLabel}
      operatingScopeAriaLabel={viewModel.operatingScopeAriaLabel}
      expandedKeys={expandedWorkspaces}
      onToggleWorkspace={(key) => toggleWorkspace(key as WorkspaceKey)}
      onNavigate={onNavigate}
    />
  );
}
