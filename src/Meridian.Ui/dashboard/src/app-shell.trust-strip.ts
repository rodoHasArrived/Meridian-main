import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { pluralizeCount } from "@/lib/format";
import type { AppShellWorkspaceErrorMap } from "@/app-shell.status-panel";
import {
  buildDataProvenanceBadgeViewModel,
  type DataProvenanceKind
} from "@/app-shell.data-provenance-badge";
import type { DataWorkspaceResponse, SessionInfo } from "@/types";
import { countProviderHealth } from "@/lib/cross-screen-counts";
import type { WorkstationCrossScreenCounts, WorkstationProviderHealthCount } from "@/types/cross-screen-counts";

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

/**
 * The session environment as the masthead pill's leading segment. PAPER vs LIVE is the one
 * fact an operator cannot afford to lose, so it carries its own variant (LIVE uses the
 * alarm-red `--live-env` token) and never shrinks, whatever else the pill truncates.
 */
export interface AppShellTrustStripEnvironment {
  label: string;
  variant: "live" | "paper" | "research" | "default";
  /**
   * The data qualifier that rides beside the environment: SEEDED, SIMULATED, SAMPLE or UNKNOWN
   * whenever the loaded data is not confirmed real, and null for real data. It replaces the
   * separate sample-data chip, so the badge is the one place that says what the operator is
   * looking at.
   */
  dataLabel: string | null;
}

export interface AppShellTrustStripState {
  ariaLabel: string;
  environment: AppShellTrustStripEnvironment;
  items: AppShellTrustStripItem[];
}

function buildTrustStripEnvironment(
  session: SessionInfo | null,
  loading: boolean,
  dataLabel: string | null
): AppShellTrustStripEnvironment {
  return { ...resolveEnvironmentBadge(session, loading), dataLabel };
}

function resolveEnvironmentBadge(
  session: SessionInfo | null,
  loading: boolean
): Pick<AppShellTrustStripEnvironment, "label" | "variant"> {
  switch (session?.environment) {
    case "live":
      return { label: "LIVE", variant: "live" };
    case "paper":
      return { label: "PAPER", variant: "paper" };
    case "research":
      return { label: "DEMO", variant: "research" };
    default:
      return loading ? { label: "LOADING", variant: "default" } : { label: "DEMO", variant: "research" };
  }
}

/**
 * The one rule for the data-source tone, read by the masthead, every page status row and Today.
 *
 * Declared demo data on a PAPER or DEMO session is the expected state: the environment badge
 * names it permanently, so it is not also raised as a warning. It becomes an issue only when the
 * operator could be misled: provenance that was never confirmed, or non-real data inside a LIVE
 * session.
 */
export function resolveDataSourceTone(
  provenance: DataProvenanceKind,
  environment: SessionInfo["environment"] | null | undefined
): AppShellTrustStripTone {
  if (provenance === "real") {
    return "ready";
  }
  if (provenance === "unknown") {
    return "review";
  }
  return environment === "live" ? "blocked" : "ready";
}

export function buildTrustStripState({
  loading,
  bootstrapFailed,
  usingDevelopmentFixtures,
  dataProvenance,
  workspaceErrors,
  session,
  data,
  crossScreenCounts = null
}: {
  loading: boolean;
  bootstrapFailed: boolean;
  usingDevelopmentFixtures: boolean;
  dataProvenance?: DataProvenanceKind;
  workspaceErrors: AppShellWorkspaceErrorMap;
  session: SessionInfo | null;
  data: DataWorkspaceResponse | null;
  /** The shell's cross-screen counts; used for provider health on routes without the Data workspace. */
  crossScreenCounts?: WorkstationCrossScreenCounts | null;
}): AppShellTrustStripState {
  const providerPosture = buildProviderTrustStripItem({
    data,
    sharedProviders: crossScreenCounts?.providers ?? null,
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
  // The environment is identity, not a warning: LIVE is announced by the alarm-red badge and
  // never counted as an issue on its own.
  const environmentTone: AppShellTrustStripTone = loading ? "pending" : "ready";
  const environmentDetail = loading
    ? "Session environment is loading."
    : session?.environment === "research"
      ? `Session ${session.displayName} is operating in research mode, shown as Demo.`
      : session
        ? `Session ${session.displayName} is operating in ${environmentValue.toLowerCase()} mode.`
        : "Session environment is not loaded; Demo is the safe default.";

  const dataSourceValue = resolvedProvenance.toUpperCase();
  const dataSourceTone = resolveDataSourceTone(resolvedProvenance, session?.environment);
  const dataSourceDetail = provenanceBadge.visible
    ? dataSourceTone === "blocked"
      ? `${provenanceBadge.headline} in a LIVE session. ${provenanceBadge.detail}`
      : `${provenanceBadge.headline}. ${provenanceBadge.detail}`
    : "Server mode evidence identifies the loaded workstation data as real.";
  const dataSourceHref = provenanceBadge.visible
    ? WORKSTATION_ROUTE_CATALOG.dataProviders
    : null;
  const dataSourceActionLabel = provenanceBadge.visible
    ? "Connect live source"
    : null;

  return {
    ariaLabel: "Workstation build, environment, provenance, and provider posture",
    // Follows the session itself, not the workspace bootstrap: the session card this badge
    // replaced showed PAPER/LIVE as soon as the session resolved, so a known environment is
    // never hidden behind LOADING while workspace data is still arriving.
    environment: buildTrustStripEnvironment(
      session,
      loading,
      provenanceBadge.visible ? provenanceBadge.label : null
    ),
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
  sharedProviders,
  loading,
  bootstrapFailed,
  dataWorkspaceError
}: {
  data: DataWorkspaceResponse | null;
  sharedProviders: WorkstationProviderHealthCount | null;
  loading: boolean;
  bootstrapFailed: boolean;
  dataWorkspaceError?: string;
}): AppShellTrustStripItem {
  if (!data && sharedProviders) {
    // Routes that do not load the Data workspace show the same count the Data screen shows.
    const value = `${sharedProviders.healthy}/${sharedProviders.total} healthy`;
    const detail = `${formatCount(sharedProviders.healthy, "provider")} healthy of ${sharedProviders.total} listed in Data provider posture.`;
    return {
      id: "providers",
      label: "Providers",
      value,
      detail,
      tone: sharedProviders.total > 0 && sharedProviders.healthy === sharedProviders.total ? "ready" : "review",
      ariaLabel: `Providers ${value}. ${detail}`,
      href: WORKSTATION_ROUTE_CATALOG.dataProviders,
      actionLabel: "Open provider posture"
    };
  }

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
  const healthy = countProviderHealth(providers)?.healthy ?? 0;
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
