import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { Link } from "react-router-dom";
import { DesignSystemStatus } from "@/design-system/status";
import type { SettingsScreenViewModel } from "./settings-screen.view-model";

const settingsTasks = [
  {
    id: "preferences",
    title: "Personalize my workstation",
    description: "Choose theme and information density for this browser.",
    href: WORKSTATION_ROUTE_CATALOG.settingsPreferences,
    actionLabel: "Open Preferences",
    status: "Browser preferences"
  },
  {
    id: "access",
    title: "Review access and authority",
    description: "Check authentication posture and manage scoped grants with audit evidence.",
    href: WORKSTATION_ROUTE_CATALOG.settingsAccess,
    actionLabel: "Open Access",
    status: "Scoped authority"
  },
  {
    id: "accounting-systems",
    title: "Configure accounting systems",
    description: "Manage external ledger connections and governed accounting configuration.",
    href: WORKSTATION_ROUTE_CATALOG.settingsAccountingSystems,
    actionLabel: "Open Accounting Systems",
    status: "Governed configuration"
  },
  {
    id: "providers",
    title: "Connect a provider",
    description: "Review connection health, start guided setup, or inspect advanced runtime evidence.",
    href: WORKSTATION_ROUTE_CATALOG.settingsProviders,
    actionLabel: "Open Provider Connections",
    status: "Owned connections"
  },
  {
    id: "diagnostics",
    title: "Investigate a problem",
    description: "Start with failed services and recent events before opening technical coverage.",
    href: WORKSTATION_ROUTE_CATALOG.settingsDiagnostics,
    actionLabel: "Open Diagnostics",
    status: "Service health"
  }
];

export function SettingsTaskChooser({ viewModel }: { viewModel: SettingsScreenViewModel }) {
  const providers = viewModel.providerConnectionCenter.groups.flatMap((group) => group.rows);
  const connections = providers.filter((row) => row.capabilityGroup !== "accounting");
  const accounting = providers.filter((row) => row.capabilityGroup === "accounting");
  const providerReady = connections.length > 0 && connections.every((row) => row.readinessLabel === "Ready");
  const accountingReady = accounting.length > 0 && accounting.every((row) => row.readinessLabel === "Ready");
  const checks = {
    providers: {
      status: providerReady ? "ready" : "review", state: providerReady ? "Verified" : connections.length > 0 ? "Review connections" : "Setup needed",
      detail: connections.length > 0
        ? `${connections.filter((row) => row.readinessLabel === "Ready").length} of ${connections.length} loaded connections ready. ${connections.find((row) => row.readinessLabel !== "Ready")?.recommendedAction ?? "Current verification evidence is available."}`
        : "No verified provider evidence is loaded. Open Provider Connections to configure or inspect a connection."
    },
    "accounting-systems": {
      status: accountingReady ? "ready" : "review", state: accountingReady ? "Verified" : "Review setup",
      detail: accounting.length > 0
        ? `${accounting.filter((row) => row.readinessLabel === "Ready").length} of ${accounting.length} loaded accounting connections ready.`
        : "Accounting connection evidence is not loaded here. Review systems and mappings if this workspace uses external accounting."
    },
    access: {
      status: viewModel.hasSession ? "review" : "blocked", state: viewModel.hasSession ? "Review authority" : "Session unavailable",
      detail: viewModel.profileAuthenticationPanel.summary
    },
    preferences: {
      status: "ready", state: "Defaults or saved preferences active",
      detail: "Theme and information density are available. Default preferences are valid; personalization is optional."
    }
  };

  return (
    <section aria-label="Workstation setup checklist" className="space-y-3">
      <div className="flex items-baseline justify-between gap-3">
        <h2 className="text-sm font-semibold text-foreground">Setup checklist</h2>
        <Link className="text-xs text-foreground underline decoration-dotted underline-offset-4" to={WORKSTATION_ROUTE_CATALOG.settingsDiagnostics}>Review diagnostic exceptions</Link>
      </div>
      <p className="text-xs text-muted-foreground">Completion reflects loaded evidence. A configured connection does not establish live readiness.</p>
      <ol className="divide-y divide-border border-y border-border">
        {settingsTasks.filter((item) => item.id !== "diagnostics").map((item) => {
          const check = checks[item.id as keyof typeof checks];
          return <li key={item.id} className="grid gap-2 py-4 lg:grid-cols-[12rem_minmax(0,1fr)_auto] lg:items-start">
            <div className="space-y-2"><h3 className="text-sm font-medium">{item.title}</h3><DesignSystemStatus status={check.status}>{check.state}</DesignSystemStatus></div>
            <p className="text-sm leading-6 text-muted-foreground">{check.detail}</p>
            <Link className="whitespace-nowrap text-sm text-foreground underline decoration-dotted underline-offset-4" to={item.href}>{item.actionLabel}</Link>
          </li>;
        })}
      </ol>
    </section>
  );
}
