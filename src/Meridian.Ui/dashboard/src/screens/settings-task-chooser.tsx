import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";
import { WorkspaceTaskWorkbench } from "@/components/meridian/workspace-task-workbench";

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

export function SettingsTaskChooser() {
  return <WorkspaceTaskWorkbench title="Settings tasks" description="Choose a task, inspect its purpose, then open its focused settings surface." items={settingsTasks} stateKey="settingsTask" />;
}
