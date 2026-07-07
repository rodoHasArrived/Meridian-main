import React, { useEffect, useState } from "react";
import { ChevronRight } from "lucide-react";
import { Link, useLocation } from "react-router-dom";
import "@/styles/workspace-nav.css";
import { buildWorkspaceNavViewModel } from "@/components/meridian/workspace-nav.view-model";
import { meridianWorkspaceIconAssets } from "@/design-system/assets";
import { DesignSystemNavRail, designSystemNavRailClasses } from "@/design-system/primitives";
import { cn } from "@/lib/utils";
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
  const compact = density === "compact";
  const activeWorkspaceKey = viewModel.items.find((item) => item.active)?.key ?? viewModel.items[0]?.key;
  const [expandedWorkspaces, setExpandedWorkspaces] = useState<Set<WorkspaceKey>>(() =>
    activeWorkspaceKey ? new Set([activeWorkspaceKey]) : new Set()
  );

  useEffect(() => {
    if (!activeWorkspaceKey) {
      return;
    }

    setExpandedWorkspaces((current) => {
      if (current.has(activeWorkspaceKey)) {
        return current;
      }

      const next = new Set(current);
      next.add(activeWorkspaceKey);
      return next;
    });
  }, [activeWorkspaceKey]);

  const toggleWorkspace = (key: WorkspaceKey) => {
    setExpandedWorkspaces((current) => {
      const next = new Set(current);
      if (next.has(key)) {
        next.delete(key);
      } else {
        next.add(key);
      }

      return next;
    });
  };

  return (
    <DesignSystemNavRail
      className={className}
      compact={compact}
      ariaLabel={`${viewModel.brandTitle} navigation`}
      navAriaLabel="Workspaces"
    >
      <div className={designSystemNavRailClasses.section}>{viewModel.navEyebrow}</div>
      {!compact && viewModel.operatingScopeLabel ? (
        <div className={designSystemNavRailClasses.scope} aria-label={viewModel.operatingScopeAriaLabel ?? undefined}>
          <span>Context</span>
          <span>{viewModel.operatingScopeLabel}</span>
        </div>
      ) : null}
      {viewModel.items.map((item) => {
        const iconSrc = meridianWorkspaceIconAssets[item.key];
        const expanded = expandedWorkspaces.has(item.key);
        const subMenuId = `workspace-nav-${density}-${item.key}-sections`;
        return (
          <React.Fragment key={item.key}>
            <div className={designSystemNavRailClasses.group}>
              <div className={designSystemNavRailClasses.row}>
                <Link
                  to={item.route}
                  aria-current={item.ariaCurrent}
                  aria-label={item.ariaLabel}
                  className={cn(
                    designSystemNavRailClasses.item,
                    "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                    item.active && designSystemNavRailClasses.itemActive
                  )}
                  onClick={onNavigate}
                >
                  <img className={designSystemNavRailClasses.itemIcon} src={iconSrc} width="16" height="16" alt="" aria-hidden="true" />
                  <span className="truncate font-medium">{item.label}</span>
                  {!compact ? (
                    <span className={cn(designSystemNavRailClasses.status, designSystemNavRailClasses.statusTone(item.statusTone))}>
                      <span className={designSystemNavRailClasses.statusDot} aria-hidden="true" />
                      {item.statusLabel}
                    </span>
                  ) : null}
                </Link>
                {item.subItems.length > 0 ? (
                  <button
                    type="button"
                    className={cn(
                      designSystemNavRailClasses.expand,
                      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                      expanded && designSystemNavRailClasses.expandExpanded
                    )}
                    aria-label={`${expanded ? "Collapse" : "Expand"} ${item.label} pages`}
                    aria-expanded={expanded}
                    aria-controls={subMenuId}
                    onClick={() => toggleWorkspace(item.key)}
                  >
                    <ChevronRight className="h-3.5 w-3.5" aria-hidden="true" />
                  </button>
                ) : null}
              </div>
            </div>
            {item.subItems.length > 0 && (
              <div
                id={subMenuId}
                className={cn(designSystemNavRailClasses.subItems, !expanded && designSystemNavRailClasses.subItemsCollapsed)}
                role="group"
                aria-label={`${item.label} pages`}
                aria-hidden={!expanded}
              >
                {expanded ? item.subItems.map((sub) => (
                  <Link
                    key={sub.route}
                    to={sub.route}
                    aria-current={sub.ariaCurrent}
                    aria-label={sub.ariaLabel}
                    className={cn(
                      designSystemNavRailClasses.subItem,
                      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                      sub.active && designSystemNavRailClasses.itemActive
                    )}
                    onClick={onNavigate}
                  >
                    <span className="truncate">{sub.label}</span>
                  </Link>
                )) : null}
              </div>
            )}
          </React.Fragment>
        );
      })}
    </DesignSystemNavRail>
  );
}
