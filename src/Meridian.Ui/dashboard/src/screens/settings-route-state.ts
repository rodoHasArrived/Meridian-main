import {
  WORKSTATION_ROUTE_CATALOG,
  settingsProviderAdvancedRoute,
  settingsProviderSetupRoute
} from "@/lib/workspace";

export type SettingsRouteViewId =
  | "chooser"
  | "preferences"
  | "access"
  | "operations"
  | "providers"
  | "provider-setup"
  | "provider-advanced"
  | "diagnostics"
  | "diagnostics-advanced";

export interface SettingsRouteState {
  view: SettingsRouteViewId;
  providerId: string | null;
  legacyAlias: boolean;
}

export interface SettingsRouteTab {
  id: "chooser" | "preferences" | "access" | "operations" | "providers" | "diagnostics";
  label: string;
  route: string;
  views: readonly SettingsRouteViewId[];
}

export const SETTINGS_ROUTE_TABS: readonly SettingsRouteTab[] = [
  { id: "chooser", label: "Overview", route: WORKSTATION_ROUTE_CATALOG.settings, views: ["chooser"] },
  { id: "preferences", label: "Preferences", route: WORKSTATION_ROUTE_CATALOG.settingsPreferences, views: ["preferences"] },
  { id: "access", label: "Access", route: WORKSTATION_ROUTE_CATALOG.settingsAccess, views: ["access"] },
  { id: "operations", label: "Accounting Systems", route: WORKSTATION_ROUTE_CATALOG.settingsAccountingSystems, views: ["operations"] },
  {
    id: "providers",
    label: "Provider Connections",
    route: WORKSTATION_ROUTE_CATALOG.settingsProviders,
    views: ["providers", "provider-setup", "provider-advanced"]
  },
  {
    id: "diagnostics",
    label: "Diagnostics",
    route: WORKSTATION_ROUTE_CATALOG.settingsDiagnostics,
    views: ["diagnostics", "diagnostics-advanced"]
  }
];

export const SETTINGS_ROUTE_VIEW_COPY: Readonly<Record<SettingsRouteViewId, { title: string; description: string }>> = {
  chooser: {
    title: "Settings",
    description: "Choose the operator task you need without loading unrelated administration controls."
  },
  preferences: {
    title: "Preferences",
    description: "Personalize this browser workstation without changing authority or service configuration."
  },
  access: {
    title: "Access",
    description: "Review authentication posture and manage scoped authority with retained audit evidence."
  },
  operations: {
    title: "Accounting systems",
    description: "External general-ledger connections and asset-profile governance."
  },
  providers: {
    title: "Provider connections",
    description: "Review provider health and choose guided setup or advanced runtime evidence."
  },
  "provider-setup": {
    title: "Guided provider setup",
    description: "Configure and verify one provider connection before reviewing advanced runtime evidence."
  },
  "provider-advanced": {
    title: "Advanced provider controls",
    description: "Inspect provider integration manifests, sync evidence, quarantine, and activation controls."
  },
  diagnostics: {
    title: "Diagnostics",
    description: "Start with current failures and recent events that need operator attention."
  },
  "diagnostics-advanced": {
    title: "Advanced diagnostics",
    description: "Inspect the complete endpoint inventory, runtime capabilities, and backend coverage."
  }
};

export function resolveSettingsRouteState(pathname: string, hash = ""): SettingsRouteState | null {
  const normalizedPath = normalizeSettingsPath(pathname);
  const providerRoute = matchProviderRoute(normalizedPath);
  if (providerRoute) {
    return providerRoute;
  }

  if (normalizedPath.endsWith("/settings/preferences")) {
    return routeState("preferences");
  }
  if (normalizedPath.endsWith("/settings/access")) {
    return routeState("access");
  }
  if (normalizedPath.endsWith("/settings/accounting-systems")) {
    return routeState("operations");
  }
  if (normalizedPath.endsWith("/settings/integrations")) {
    return routeState("operations", null, true);
  }
  if (normalizedPath.endsWith("/settings/providers")) {
    return routeState("providers");
  }
  if (normalizedPath.endsWith("/settings/diagnostics/advanced")) {
    return routeState("diagnostics-advanced");
  }
  if (normalizedPath.endsWith("/settings/feature-coverage")) {
    return routeState("diagnostics-advanced", null, true);
  }
  if (normalizedPath.endsWith("/settings/diagnostics")) {
    return routeState("diagnostics");
  }
  if (normalizedPath.endsWith("/settings")) {
    return resolveLegacySettingsHash(hash) ?? routeState("chooser");
  }

  return null;
}

export function resolveLegacySettingsHash(hash: string): SettingsRouteState | null {
  const normalizedHash = hash.trim().replace(/^#/, "").toLowerCase();
  if (!normalizedHash) {
    return null;
  }

  if (normalizedHash === "alpaca-provider-setup") {
    return routeState("provider-setup", "alpaca", true);
  }
  if (normalizedHash === "robinhood-provider-setup") {
    return routeState("provider-setup", "robinhood", true);
  }
  if (normalizedHash === "data-provider-modules") {
    return routeState("provider-setup", "data", true);
  }

  const providerConnectionMatch = normalizedHash.match(/^provider-(.+)-connection$/);
  if (providerConnectionMatch?.[1]) {
    return routeState("provider-setup", safeDecodeProviderId(providerConnectionMatch[1]), true);
  }

  if (normalizedHash === "diagnostic-endpoints") {
    return routeState("diagnostics-advanced", null, true);
  }
  if (normalizedHash === "runtime-feature-capabilities" || normalizedHash === "backend-capability-coverage") {
    return routeState("diagnostics-advanced", null, true);
  }
  if (normalizedHash === "settings-appearance") {
    return routeState("preferences", null, true);
  }
  if (normalizedHash === "settings-overview" || normalizedHash === "profile-authentication" || normalizedHash === "scoped-access-control") {
    return routeState("access", null, true);
  }
  if (normalizedHash === "fund-operations-control-center" || normalizedHash === "asset-profile-accounting") {
    return routeState("operations", null, true);
  }
  if (normalizedHash === "provider-connection-center") {
    return routeState("providers", null, true);
  }

  return null;
}

export function canonicalSettingsRouteForState(state: Pick<SettingsRouteState, "view" | "providerId">): string {
  switch (state.view) {
    case "chooser":
      return WORKSTATION_ROUTE_CATALOG.settings;
    case "preferences":
      return WORKSTATION_ROUTE_CATALOG.settingsPreferences;
    case "access":
      return WORKSTATION_ROUTE_CATALOG.settingsAccess;
    case "operations":
      return WORKSTATION_ROUTE_CATALOG.settingsAccountingSystems;
    case "providers":
      return WORKSTATION_ROUTE_CATALOG.settingsProviders;
    case "provider-setup":
      return settingsProviderSetupRoute(state.providerId ?? "provider");
    case "provider-advanced":
      return settingsProviderAdvancedRoute(state.providerId ?? "provider");
    case "diagnostics":
      return WORKSTATION_ROUTE_CATALOG.settingsDiagnostics;
    case "diagnostics-advanced":
      return WORKSTATION_ROUTE_CATALOG.settingsDiagnosticsAdvanced;
  }
}

function matchProviderRoute(pathname: string): SettingsRouteState | null {
  const match = pathname.match(/\/settings\/providers\/([^/]+)\/(setup|advanced)$/);
  if (!match?.[1] || !match[2]) {
    return null;
  }

  return routeState(
    match[2] === "advanced" ? "provider-advanced" : "provider-setup",
    safeDecodeProviderId(match[1])
  );
}

function normalizeSettingsPath(pathname: string): string {
  const normalized = pathname.split(/[?#]/)[0]?.replace(/\/+$/, "").toLowerCase() ?? "";
  return normalized || "/";
}

function safeDecodeProviderId(value: string): string {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function routeState(
  view: SettingsRouteViewId,
  providerId: string | null = null,
  legacyAlias = false
): SettingsRouteState {
  return { view, providerId, legacyAlias };
}
