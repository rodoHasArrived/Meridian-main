import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

export type AccountingNavigationLifecycle =
  | "close"
  | "records"
  | "reconciliation"
  | "review"
  | "administration";

export type AccountingNavigationMatchMode = "exact" | "prefix";

export interface AccountingNavigationRouteMatch {
  route: string;
  match?: AccountingNavigationMatchMode;
}

export interface AccountingNavigationItemDefinition {
  id: string;
  label: string;
  route: string;
  match?: AccountingNavigationMatchMode;
  activeRoutes?: readonly AccountingNavigationRouteMatch[];
}

export interface AccountingNavigationGroupDefinition {
  id: AccountingNavigationLifecycle;
  label: string;
  items: readonly AccountingNavigationItemDefinition[];
}

/**
 * The one primary Accounting browse model. Detail routes remain deep links and
 * resolve back to their owning destination through activeRoutes.
 */
export const ACCOUNTING_NAVIGATION_GROUPS: readonly AccountingNavigationGroupDefinition[] = [
  {
    id: "close",
    label: "Close",
    items: [
      {
        id: "today",
        label: "Today",
        route: WORKSTATION_ROUTE_CATALOG.accounting,
        match: "exact"
      },
      {
        id: "operations-continuity",
        label: "Operations continuity",
        route: WORKSTATION_ROUTE_CATALOG.accountingOperationsContinuity
      },
      {
        id: "close-calendar",
        label: "Close calendar",
        route: WORKSTATION_ROUTE_CATALOG.accountingCloseCalendar
      }
    ]
  },
  {
    id: "records",
    label: "Records",
    items: [
      {
        id: "ledger",
        label: "Ledger explorer",
        route: WORKSTATION_ROUTE_CATALOG.accountingLedger,
        activeRoutes: [
          { route: WORKSTATION_ROUTE_CATALOG.accountingTrialBalanceLegacy },
          { route: WORKSTATION_ROUTE_CATALOG.accountingAccountDetail }
        ]
      },
      {
        id: "adjustments",
        label: "Adjustments",
        route: WORKSTATION_ROUTE_CATALOG.accountingJournalEntries
      },
      {
        id: "capital-accounts",
        label: "Capital accounts",
        route: WORKSTATION_ROUTE_CATALOG.accountingCapitalAccounts
      },
      {
        id: "security-master",
        label: "Security Master",
        route: WORKSTATION_ROUTE_CATALOG.accountingSecurityMaster
      }
    ]
  },
  {
    id: "reconciliation",
    label: "Reconciliation",
    items: [
      {
        id: "statement-import",
        label: "Import statement",
        route: WORKSTATION_ROUTE_CATALOG.accountingStatementImport
      },
      {
        id: "casework",
        label: "Casework",
        route: WORKSTATION_ROUTE_CATALOG.accountingReconciliation,
        match: "exact",
        activeRoutes: [
          { route: WORKSTATION_ROUTE_CATALOG.accountingReconciliationMatch }
        ]
      },
      {
        id: "external-gl",
        label: "External GL",
        route: WORKSTATION_ROUTE_CATALOG.accountingExternalGlReconciliation
      }
    ]
  },
  {
    id: "review",
    label: "Review",
    items: [
      {
        id: "exceptions",
        label: "Exceptions",
        route: WORKSTATION_ROUTE_CATALOG.accountingExceptions
      },
      {
        id: "approvals",
        label: "Approvals",
        route: WORKSTATION_ROUTE_CATALOG.accountingApprovals
      }
    ]
  },
  {
    id: "administration",
    label: "Administration",
    items: [
      {
        id: "entity-setup",
        label: "Entity setup",
        route: WORKSTATION_ROUTE_CATALOG.accountingEntitySetup
      },
      {
        id: "configure",
        label: "Configure",
        route: WORKSTATION_ROUTE_CATALOG.accountingConfigure
      }
    ]
  }
];

export const ACCOUNTING_NAVIGATION_ITEMS: readonly AccountingNavigationItemDefinition[] =
  ACCOUNTING_NAVIGATION_GROUPS.flatMap((group) => group.items);

/**
 * These dimensions scope multiple Accounting destinations. Route-owned query
 * state such as approvalId, tab, frexRecord, and journalEntryId deliberately
 * does not cross primary navigation.
 */
export const ACCOUNTING_NAVIGATION_CONTEXT_QUERY_KEYS = [
  "fundProfileId",
  "ledgerBookId",
  "periodId",
  "workflowStatus"
] as const;

export function isAccountingNavigationItemActive(
  pathname: string,
  item: AccountingNavigationItemDefinition
): boolean {
  return routeMatches(pathname, item.route, item.match)
    || Boolean(item.activeRoutes?.some((candidate) => (
      routeMatches(pathname, candidate.route, candidate.match)
    )));
}

export function appendAccountingNavigationContextToRoute(route: string, search: string): string {
  if (!search) {
    return route;
  }

  const source = new URLSearchParams(search);
  const hashIndex = route.indexOf("#");
  const routeWithoutHash = hashIndex >= 0 ? route.slice(0, hashIndex) : route;
  const hash = hashIndex >= 0 ? route.slice(hashIndex) : "";
  const searchIndex = routeWithoutHash.indexOf("?");
  const pathname = searchIndex >= 0 ? routeWithoutHash.slice(0, searchIndex) : routeWithoutHash;
  const target = new URLSearchParams(searchIndex >= 0 ? routeWithoutHash.slice(searchIndex) : "");

  ACCOUNTING_NAVIGATION_CONTEXT_QUERY_KEYS.forEach((key) => {
    const value = source.get(key)?.trim();
    if (value && !target.has(key)) {
      target.set(key, value);
    }
  });

  const nextSearch = target.toString();
  return `${pathname}${nextSearch ? `?${nextSearch}` : ""}${hash}`;
}

function routeMatches(
  pathname: string,
  route: string,
  match: AccountingNavigationMatchMode = "prefix"
): boolean {
  const cleanPathname = normalizePath(pathname);
  const cleanRoute = normalizePath(route);
  return match === "exact"
    ? cleanPathname === cleanRoute
    : cleanPathname === cleanRoute || cleanPathname.startsWith(`${cleanRoute}/`);
}

function normalizePath(value: string): string {
  return value.split(/[?#]/)[0]?.replace(/\/+$/, "") || "/";
}
