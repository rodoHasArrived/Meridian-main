import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import type { AppShellWorkspaceErrorMap } from "@/app-shell.status-panel";
import packageJson from "../package.json";
import type { DataWorkspaceResponse, SessionInfo } from "@/types";

export type AppShellTrustStripTone = "ready" | "review" | "blocked" | "pending";

export interface AppShellTrustStripItem {
  id: string;
  label: string;
  value: string;
  detail: string;
  tone: AppShellTrustStripTone;
  ariaLabel: string;
  href: string | null;
  actionLabel: string | null;
}

export interface AppShellTrustStripState {
  ariaLabel: string;
  items: AppShellTrustStripItem[];
}

export function buildTrustStripState({
  loading,
  bootstrapFailed,
  usingDevelopmentFixtures,
  workspaceErrors,
  session,
  data
}: {
  loading: boolean;
  bootstrapFailed: boolean;
  usingDevelopmentFixtures: boolean;
  workspaceErrors: AppShellWorkspaceErrorMap;
  session: SessionInfo | null;
  data: DataWorkspaceResponse | null;
}): AppShellTrustStripState {
  const providerPosture = buildProviderTrustStripItem(data);
  const failedWorkspaceCount = Object.keys(workspaceErrors).length;

  const environmentValue = loading
    ? "Loading"
    : session?.environment
      ? titleCase(session.environment)
      : "Unknown";
  const environmentTone: AppShellTrustStripTone = session?.environment === "live"
    ? "blocked"
    : session?.environment === "paper"
      ? "ready"
      : loading
        ? "pending"
        : "review";

  const dataSourceValue = usingDevelopmentFixtures
    ? "Demo data"
    : bootstrapFailed
      ? "Unavailable"
      : failedWorkspaceCount > 0
        ? "Limited data"
        : "Connected";
  const dataSourceTone: AppShellTrustStripTone = usingDevelopmentFixtures
    ? "pending"
    : bootstrapFailed
      ? "blocked"
      : failedWorkspaceCount > 0
        ? "review"
        : "ready";
  const dataSourceDetail = usingDevelopmentFixtures
    ? "Demo data is visible; confirm live source status before making operating decisions."
    : bootstrapFailed
      ? "Workspace data is unavailable from this machine. Try again or open diagnostics."
      : failedWorkspaceCount > 0
        ? `${formatCount(failedWorkspaceCount, "workspace area")} did not load.`
        : "Workspace data loaded from Meridian.";

  return {
    ariaLabel: "Workstation build, mode, data source, and provider posture",
    items: [
      {
        id: "build",
        label: "Build",
        value: `v${packageJson.version}`,
        detail: "Current Meridian web release.",
        tone: "ready",
        ariaLabel: `Build ${packageJson.version}. Current Meridian web release.`,
        href: null,
        actionLabel: null
      },
      {
        id: "mode",
        label: "Mode",
        value: environmentValue,
        detail: session
          ? `Session ${session.displayName} is operating in ${environmentValue.toLowerCase()} mode.`
          : "Session environment is not loaded yet.",
        tone: environmentTone,
        ariaLabel: `Mode ${environmentValue}. ${session ? `Session ${session.displayName} is operating in ${environmentValue.toLowerCase()} mode.` : "Session environment is not loaded yet."}`,
        href: session?.environment === "live"
          ? WORKSTATION_ROUTE_CATALOG.tradingReadiness
          : null,
        actionLabel: session?.environment === "live" ? "Review readiness" : null
      },
      {
        id: "source",
        label: "Source",
        value: dataSourceValue,
        detail: dataSourceDetail,
        tone: dataSourceTone,
        ariaLabel: `Data source ${dataSourceValue}. ${dataSourceDetail}`,
        href: bootstrapFailed || failedWorkspaceCount > 0
          ? WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage
          : null,
        actionLabel: bootstrapFailed || failedWorkspaceCount > 0 ? "Open diagnostics" : null
      },
      providerPosture
    ]
  };
}

function buildProviderTrustStripItem(data: DataWorkspaceResponse | null): AppShellTrustStripItem {
  if (!data) {
    return {
      id: "providers",
      label: "Providers",
      value: "Pending",
      detail: "Provider posture has not loaded yet.",
      tone: "pending",
      ariaLabel: "Providers Pending. Provider posture has not loaded yet.",
      href: WORKSTATION_ROUTE_CATALOG.dataProviders,
      actionLabel: "Open provider posture"
    };
  }

  const providers = data.providers ?? [];
  if (providers.length === 0) {
    return {
      id: "providers",
      label: "Providers",
      value: "No providers",
      detail: "No provider status rows are available in the current workspace data.",
      tone: "review",
      ariaLabel: "Providers No providers. No provider status rows are available in the current workspace data.",
      href: WORKSTATION_ROUTE_CATALOG.settingsAlpacaProviderSetup,
      actionLabel: "Configure provider"
    };
  }

  const degraded = providers.filter((provider) => provider.status === "Degraded").length;
  const warning = providers.filter((provider) => provider.status === "Warning").length;
  const healthy = providers.filter((provider) => provider.status === "Healthy").length;
  const value = degraded > 0
    ? `${degraded} degraded`
    : warning > 0
      ? `${warning} warning`
      : `${healthy}/${providers.length} healthy`;
  const detail = degraded > 0
    ? `${formatCount(degraded, "provider")} degraded; open Data provider posture before trading decisions.`
    : warning > 0
      ? `${formatCount(warning, "provider")} warning; review provider trust before relying on fresh data.`
      : `${formatCount(healthy, "provider")} healthy in the loaded data posture.`;
  const tone: AppShellTrustStripTone = degraded > 0 ? "blocked" : warning > 0 ? "review" : "ready";

  return {
    id: "providers",
    label: "Providers",
    value,
    detail,
    tone,
    ariaLabel: `Providers ${value}. ${detail}`,
    href: degraded > 0 || warning > 0
      ? WORKSTATION_ROUTE_CATALOG.dataProviders
      : null,
    actionLabel: degraded > 0 || warning > 0 ? "Open provider posture" : null
  };
}

function titleCase(value: string): string {
  return value.length > 0 ? `${value.charAt(0).toUpperCase()}${value.slice(1).toLowerCase()}` : value;
}

function formatCount(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`;
}
