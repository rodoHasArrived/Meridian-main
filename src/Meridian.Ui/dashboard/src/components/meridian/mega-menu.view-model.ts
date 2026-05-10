import { useCallback, useEffect, useMemo, useState } from "react";
import type { WorkspaceKey } from "@/types";

export interface MegaMenuLink {
  label: string;
  route: string;
  description: string;
  active: boolean;
  ariaCurrent: "page" | undefined;
  ariaLabel: string;
}

export interface MegaMenuSection {
  key: WorkspaceKey;
  label: string;
  eyebrow: string;
  links: MegaMenuLink[];
  active: boolean;
  ariaCurrent: "page" | undefined;
}

export interface MegaMenuViewModel {
  open: boolean;
  sections: MegaMenuSection[];
  triggerAriaLabel: string;
  triggerControlsId: string;
  triggerExpanded: boolean;
  panelAriaLabel: string;
  panelId: string;
  closeAriaLabel: string;
  footerLabel: string;
  footerShortcut: string;
  openMenu: () => void;
  closeMenu: () => void;
  toggleMenu: () => void;
}

export type MegaMenuFocusBoundary = "outside" | "first" | "middle" | "last" | "none";
export type MegaMenuKeyCommand = "close" | "focus-first" | "focus-last" | null;

type StaticMegaMenuLink = Omit<MegaMenuLink, "active" | "ariaCurrent" | "ariaLabel">;
type StaticMegaMenuSection = Omit<MegaMenuSection, "active" | "ariaCurrent" | "links"> & {
  links: StaticMegaMenuLink[];
};

const MEGA_MENU_PANEL_ID = "workspace-mega-menu-panel";

const MENU_SECTIONS: StaticMegaMenuSection[] = [
  {
    key: "trading",
    label: "Trading",
    eyebrow: "Execution & orders",
    links: [
      { label: "Cockpit", route: "/trading", description: "Live trading dashboard" },
      { label: "Orders", route: "/trading/orders", description: "Order blotter and status" },
      { label: "Positions", route: "/trading/positions", description: "Open position tracking" },
      { label: "Risk", route: "/trading/risk", description: "Real-time risk exposure" },
      { label: "Readiness", route: "/trading/readiness", description: "Pre-trade readiness check" }
    ]
  },
  {
    key: "portfolio",
    label: "Portfolio",
    eyebrow: "Exposure & attribution",
    links: [
      { label: "Exposure", route: "/portfolio", description: "Portfolio exposure overview" },
      { label: "Attribution", route: "/portfolio/attribution", description: "Performance attribution" },
      { label: "Brokerage sync", route: "/portfolio/brokerage-sync", description: "Brokerage account sync review" }
    ]
  },
  {
    key: "accounting",
    label: "Accounting",
    eyebrow: "Ledger & reconciliation",
    links: [
      { label: "Ledger", route: "/accounting", description: "Fund ledger entries" },
      { label: "Reconciliation", route: "/accounting/reconciliation", description: "Position reconciliation" },
      { label: "Approvals", route: "/accounting/approvals", description: "Pending approval queue" }
    ]
  },
  {
    key: "reporting",
    label: "Reporting",
    eyebrow: "Reports & exports",
    links: [
      { label: "Profiles", route: "/reporting", description: "Governed export profiles" },
      { label: "Report packs", route: "/reporting/report-packs", description: "Approval-ready report packet review" },
      { label: "Evidence", route: "/reporting/evidence", description: "Packet completeness and lineage" },
      { label: "Exports", route: "/reporting/exports", description: "Data export queue" }
    ]
  },
  {
    key: "strategy",
    label: "Strategy",
    eyebrow: "Research & backtesting",
    links: [
      { label: "Backtest runs", route: "/strategy", description: "Strategy backtest results" },
      { label: "Promotions", route: "/strategy/promotions", description: "Paper-to-live promotions" },
      { label: "Research", route: "/strategy/research", description: "Signal research workspace" },
      { label: "Quant Lab", route: "/strategy/quant-lab", description: "Run C# scripts with plots and metrics" }
    ]
  },
  {
    key: "data",
    label: "Data",
    eyebrow: "Providers & feeds",
    links: [
      { label: "Provider posture", route: "/data", description: "Data provider health" },
      { label: "Watchlist", route: "/data/watchlist", description: "Symbol subscription setup" },
      { label: "Live quotes", route: "/data/quotes", description: "Quote, trade, and depth workbench" },
      { label: "Backfill queues", route: "/data/backfills", description: "Historical data backfill" },
      { label: "Security Master", route: "/data/security-master", description: "Reference-data readiness" }
    ]
  },
  {
    key: "settings",
    label: "Settings",
    eyebrow: "Session & preferences",
    links: [
      { label: "Session", route: "/settings", description: "Active session details" },
      { label: "Preferences", route: "/settings/preferences", description: "User preferences" },
      { label: "Integrations", route: "/settings/integrations", description: "API and broker connections" }
    ]
  }
];

export function useMegaMenuViewModel(pathname: string): MegaMenuViewModel {
  const [open, setOpen] = useState(false);

  useEffect(() => {
    setOpen(false);
  }, [pathname]);

  const openMenu = useCallback(() => setOpen(true), []);
  const closeMenu = useCallback(() => setOpen(false), []);
  const toggleMenu = useCallback(() => setOpen((current) => !current), []);

  return useMemo(
    () => buildMegaMenuViewModel({
      pathname,
      open,
      openMenu,
      closeMenu,
      toggleMenu
    }),
    [closeMenu, open, openMenu, pathname, toggleMenu]
  );
}

export function buildMegaMenuViewModel({
  pathname,
  open,
  openMenu,
  closeMenu,
  toggleMenu
}: {
  pathname: string;
  open: boolean;
  openMenu: () => void;
  closeMenu: () => void;
  toggleMenu: () => void;
}): MegaMenuViewModel {
  return {
    open,
    sections: buildMegaMenuSections(pathname),
    triggerAriaLabel: open ? "Close workspace navigation menu" : "Open workspace navigation menu",
    triggerControlsId: MEGA_MENU_PANEL_ID,
    triggerExpanded: open,
    panelAriaLabel: "Workspace navigation",
    panelId: MEGA_MENU_PANEL_ID,
    closeAriaLabel: "Close navigation menu",
    footerLabel: "All workspaces / Palette-first routing",
    footerShortcut: "Ctrl K",
    openMenu,
    closeMenu,
    toggleMenu
  };
}

export function resolveMegaMenuKeyCommand({
  key,
  shiftKey,
  focusBoundary
}: {
  key: string;
  shiftKey: boolean;
  focusBoundary: MegaMenuFocusBoundary;
}): MegaMenuKeyCommand {
  if (key === "Escape") {
    return "close";
  }

  if (key !== "Tab") {
    return null;
  }

  if (focusBoundary === "none") {
    return "focus-first";
  }

  if (shiftKey && (focusBoundary === "first" || focusBoundary === "outside")) {
    return "focus-last";
  }

  if (!shiftKey && (focusBoundary === "last" || focusBoundary === "outside")) {
    return "focus-first";
  }

  return null;
}

function buildMegaMenuSections(pathname: string): MegaMenuSection[] {
  return MENU_SECTIONS.map((section) => {
    const sectionActive = isWorkspaceSectionActive(pathname, section.key);
    return {
      ...section,
      active: sectionActive,
      ariaCurrent: sectionActive ? "page" : undefined,
      links: section.links.map((link) => {
        const active = isRouteActive(pathname, link.route);
        return {
          ...link,
          active,
          ariaCurrent: active ? "page" : undefined,
          ariaLabel: active
            ? `${link.label}, current route, ${section.label} workspace`
            : `Open ${link.label}, ${section.label} workspace`
        };
      })
    };
  });
}

function isWorkspaceSectionActive(pathname: string, key: WorkspaceKey): boolean {
  return pathname === `/${key}` || pathname.startsWith(`/${key}/`);
}

function isRouteActive(pathname: string, route: string): boolean {
  const routeSegments = route.split("/").filter(Boolean);
  if (routeSegments.length <= 1) {
    return pathname === route;
  }

  return pathname === route || pathname.startsWith(`${route}/`);
}
