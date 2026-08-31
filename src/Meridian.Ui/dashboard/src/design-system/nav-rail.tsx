import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * The operator rail class contract owned by the design-system layer. Consumers
 * (`WorkspaceNav`) compose these class names so the rail visual language stays defined
 * once, in the design-system bridge, rather than scattered across screens.
 */
export const designSystemNavRailClasses = {
  root: "operator-rail",
  compact: "operator-rail-compact",
  nav: "operator-rail-nav op-rail__nav",
  section: "operator-rail-section op-rail__section",
  scope: "operator-nav-scope",
  group: "operator-nav-group",
  row: "operator-nav-row",
  item: "operator-nav-item op-nav-item",
  itemIcon: "operator-nav-item__icon op-nav-item__icon",
  itemActive: "active",
  status: "operator-nav-status",
  statusDot: "operator-nav-status-dot",
  statusTone: (tone: string) => `operator-nav-status-${tone}`,
  expand: "operator-nav-expand",
  expandExpanded: "expanded",
  subItems: "operator-nav-subitems",
  subItemsCollapsed: "operator-nav-subitems-collapsed",
  subItem: "operator-nav-subitem"
} as const;

export interface DesignSystemNavRailProps {
  className?: string;
  compact?: boolean;
  ariaLabel: string;
  navAriaLabel: string;
  children: ReactNode;
}

/**
 * Presentational shell for the operator navigation rail: the labelled `<aside>` and its
 * inner `<nav>` landmark, styled through {@link designSystemNavRailClasses}. Callers render
 * the rail content (section label, workspace items, sub-items) as children so navigation
 * behavior and accessibility stay owned by the caller.
 */
export function DesignSystemNavRail({
  className,
  compact = false,
  ariaLabel,
  navAriaLabel,
  children
}: DesignSystemNavRailProps) {
  return (
    <aside
      className={cn(
        designSystemNavRailClasses.root,
        "mds-nav-rail op-rail",
        compact && designSystemNavRailClasses.compact,
        className
      )}
      aria-label={ariaLabel}
      data-design-system-component="NavRail"
    >
      <nav className={designSystemNavRailClasses.nav} aria-label={navAriaLabel}>
        {children}
      </nav>
    </aside>
  );
}
