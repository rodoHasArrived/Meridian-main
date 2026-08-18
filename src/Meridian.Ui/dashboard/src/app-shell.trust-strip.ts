import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { pluralizeCount } from "@/lib/format";
import type { AppShellWorkspaceErrorMap } from "@/app-shell.status-panel";
import {
  buildDataProvenanceBadgeViewModel,
  type DataProvenanceKind
} from "@/app-shell.data-provenance-badge";
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
  dataProvenance,
  workspaceErrors,
  session,
  data
}: {
  loading: boolean;
  bootstrapFailed: boolean;
  usingDevelopmentFixtures: boolean;
  dataProvenance?: DataProvenanceKind;
  workspaceErrors: AppShellWorkspaceErrorMap;
  session: SessionInfo | null;
  data: DataWorkspaceResponse | null;
}): AppShellTrustStripState {
  const providerPosture = buildProviderTrustStripItem({
    data,
    loading,
    bootstrapFailed,
    dataWorkspaceError: workspaceErrors.data
  });
  const resolvedProvenance = usingDevelopmentFixtures
    ? "seeded"
    : dataProvenance ?? "unknown";
  const provenanceBadge = buildDataProvenanceBadgeViewModel({
    provenance: resolvedProvenance
  });

  const environmentValue = loading
    ? "Loading"
    : session?.environment === "live"
      ? "Live"
      : session?.environment === "paper"
        ? "Paper"
        : "Demo";
  const environmentTone: AppShellTrustStripTone = loading
    ? "pending"
    : environmentValue === "Live"
      ? "review"
      : environmentValue === "Paper"
        ? "ready"
        : "review";
  const environmentDetail = loading
    ? "Session environment is loading."
    : session?.environment === "research"
      ? `Session ${session.displayName} is operating in research mode, shown as Demo.`
      : session
        ? `Session ${session.displayName} is operating in ${environmentValue.toLowerCase()} mode.`
        : "Session environment is not loaded; Demo is the safe default.";

  const dataSourceValue = resolvedProvenance.toUpperCase();
  const dataSourceTone: AppShellTrustStripTone = provenanceBadge.visible
    ? "review"
    : "ready";
  const dataSourceDetail = provenanceBadge.visible
    ? `${provenanceBadge.headline}. ${provenanceBadge.detail}`
    : "Server mode evidence identifies the loaded workstation data as real.";
  const dataSourceHref = provenanceBadge.visible
    ? WORKSTATION_ROUTE_CATALOG.dataProviders
    : null;
  const dataSourceActionLabel = provenanceBadge.visible
    ? "Connect live source"
    : null;

  return {
    ariaLabel: "Workstation build, environment, provenance, and provider posture",
    items: [
      {
        id: "build",
        label: "Build",
        value: `v${__APP_VERSION__}`,
        detail: "Current Meridian web release.",
        tone: "ready",
        ariaLabel: `Build ${__APP_VERSION__}. Current Meridian web release.`,
        href: null,
        actionLabel: null
      },
      {
        id: "mode",
        label: "Environment",
        value: environmentValue,
        detail: environmentDetail,
        tone: environmentTone,
        ariaLabel: `Environment ${environmentValue}. ${environmentDetail}`,
        href: environmentValue === "Live"
          ? WORKSTATION_ROUTE_CATALOG.tradingReadiness
          : null,
        actionLabel: environmentValue === "Live" ? "Review readiness" : null
      },
      {
        id: "source",
        label: "Provenance",
        value: dataSourceValue,
        detail: dataSourceDetail,
        tone: dataSourceTone,
        ariaLabel: `Data provenance ${dataSourceValue}. ${dataSourceDetail}`,
        href: dataSourceHref,
        actionLabel: dataSourceActionLabel
      },
      providerPosture
    ]
  };
}

function buildProviderTrustStripItem({
  data,
  loading,
  bootstrapFailed,
  dataWorkspaceError
}: {
  data: DataWorkspaceResponse | null;
  loading: boolean;
  bootstrapFailed: boolean;
  dataWorkspaceError?: string;
}): AppShellTrustStripItem {
  if (!data) {
    if (loading) {
      return {
        id: "providers",
        label: "Providers",
        value: "Pending",
        detail: "Provider posture has not loaded yet.",
        tone: "pending",
        ariaLabel: "Providers Pending. Provider posture has not loaded yet.",
        href: null,
        actionLabel: null
      };
    }

    const failed = bootstrapFailed || Boolean(dataWorkspaceError);
    const detail = dataWorkspaceError
      ? `Data workspace failed: ${dataWorkspaceError}`
      : failed
        ? "Workspace bootstrap failed before provider posture loaded."
        : "Provider posture is unavailable in the loaded workspace data.";
    return {
      id: "providers",
      label: "Providers",
      value: "Unavailable",
      detail,
      tone: failed ? "blocked" : "review",
      ariaLabel: `Providers Unavailable. ${detail}`,
      href: failed
        ? WORKSTATION_ROUTE_CATALOG.settingsBackendCapabilityCoverage
        : WORKSTATION_ROUTE_CATALOG.dataProviders,
      actionLabel: failed ? "Open diagnostics" : "Open provider posture"
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

  const blocked = providers.filter((provider) => provider.status === "Blocked").length;
  const degraded = providers.filter((provider) => provider.status === "Degraded").length;
  const warning = providers.filter((provider) => provider.status === "Warning").length;
  const healthy = providers.filter((provider) => provider.status === "Healthy").length;
  const value = blocked > 0
    ? `${blocked} blocked`
    : degraded > 0
      ? `${degraded} degraded`
      : warning > 0
        ? `${warning} warning`
        : `${healthy}/${providers.length} healthy`;
  const detail = blocked > 0
    ? `${formatCount(blocked, "provider")} blocked; restore provider connectivity before relying on workstation data.`
    : degraded > 0
      ? `${formatCount(degraded, "provider")} degraded; open Data provider posture before trading decisions.`
      : warning > 0
        ? `${formatCount(warning, "provider")} warning; review provider trust before relying on fresh data.`
        : `${formatCount(healthy, "provider")} healthy in the loaded data posture.`;
  const needsAction = blocked > 0 || degraded > 0 || warning > 0;
  const tone: AppShellTrustStripTone = blocked > 0 || degraded > 0
    ? "blocked"
    : warning > 0
      ? "review"
      : "ready";

  return {
    id: "providers",
    label: "Providers",
    value,
    detail,
    tone,
    ariaLabel: `Providers ${value}. ${detail}`,
    href: needsAction
      ? WORKSTATION_ROUTE_CATALOG.dataProviders
      : null,
    actionLabel: needsAction ? "Open provider posture" : null
  };
}

function formatCount(count: number, singular: string, plural = `${singular}s`): string {
  return pluralizeCount(count, singular, { plural });
}
