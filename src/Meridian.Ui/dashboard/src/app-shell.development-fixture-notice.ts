import {
  WORKSTATION_ROUTE_CATALOG,
  workstationRouteWithQuery
} from "@/lib/workspace";

export interface DevelopmentFixtureNoticeStep {
  id: "watchlist" | "quotes" | "readiness" | "connect";
  step: string;
  href: string;
  label: string;
  ariaLabel: string;
  active: boolean;
}

export interface DevelopmentFixtureNoticeViewModel {
  role: "status";
  ariaLive: "polite";
  title: string;
  detail: string;
  workflowLabel: string;
  retryLabel: string;
  retryAriaLabel: string;
  retryDisabled: boolean;
  retryBusy: boolean;
  steps: DevelopmentFixtureNoticeStep[];
}

export function buildDevelopmentFixtureNoticeViewModel({
  pathname,
  hash = "",
  refreshing = false
}: {
  pathname: string;
  hash?: string;
  refreshing?: boolean;
}): DevelopmentFixtureNoticeViewModel {
  return {
    role: "status",
    ariaLive: "polite",
    title: "Demo data",
    detail: "Showing demo data because live Meridian data is unavailable; use the evidence path for watchlist, quotes, readiness, and Alpaca setup.",
    workflowLabel: "Evidence path",
    retryLabel: refreshing ? "Retrying live data" : "Retry live data",
    retryAriaLabel: refreshing
      ? "Retrying live Meridian workspace data"
      : "Retry live Meridian workspace data",
    retryDisabled: refreshing,
    retryBusy: refreshing,
    steps: developmentFixtureDemoSteps.map((item) => ({
      ...item,
      active: isCurrentDevelopmentFixtureDemoStep(item, pathname, hash)
    }))
  };
}

const developmentFixtureDemoSteps = [
  {
    id: "watchlist",
    step: "1",
    href: WORKSTATION_ROUTE_CATALOG.dataWatchlist,
    matchPath: WORKSTATION_ROUTE_CATALOG.dataWatchlist,
    label: "Watchlist",
    ariaLabel: "Open sample watchlist demo lane"
  },
  {
    id: "quotes",
    step: "2",
    href: workstationRouteWithQuery("dataQuotes", { symbol: "AAPL" }),
    matchPath: WORKSTATION_ROUTE_CATALOG.dataQuotes,
    label: "Quotes",
    ariaLabel: "Open sample live quotes for AAPL"
  },
  {
    id: "readiness",
    step: "3",
    href: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    matchPath: WORKSTATION_ROUTE_CATALOG.tradingReadiness,
    label: "Readiness",
    ariaLabel: "Open sample readiness console"
  },
  {
    id: "connect",
    step: "4",
    href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
    matchPath: WORKSTATION_ROUTE_CATALOG.settings,
    matchHash: "#alpaca-provider-setup",
    label: "Connect",
    ariaLabel: "Open Alpaca paper provider setup"
  }
] as const;

function isCurrentDevelopmentFixtureDemoStep(
  item: (typeof developmentFixtureDemoSteps)[number],
  pathname: string,
  hash: string
) {
  if (item.matchPath !== pathname) {
    return false;
  }

  return !("matchHash" in item) || item.matchHash === hash;
}
