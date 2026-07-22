import { ArrowRight, KeyRound, MonitorCheck, Palette, PlugZap, ShieldCheck } from "lucide-react";
import { Link } from "react-router-dom";
import { WORKSTATION_ROUTE_CATALOG } from "@/lib/workspace";

const settingsTasks = [
  {
    id: "preferences",
    title: "Personalize my workstation",
    description: "Choose theme and information density for this browser.",
    href: WORKSTATION_ROUTE_CATALOG.settingsPreferences,
    actionLabel: "Open Preferences",
    icon: Palette
  },
  {
    id: "access",
    title: "Review access and authority",
    description: "Check authentication posture and manage scoped grants with audit evidence.",
    href: WORKSTATION_ROUTE_CATALOG.settingsAccess,
    actionLabel: "Open Access",
    icon: ShieldCheck
  },
  {
    id: "accounting-systems",
    title: "Configure accounting systems",
    description: "Manage external ledger connections and governed accounting configuration.",
    href: WORKSTATION_ROUTE_CATALOG.settingsAccountingSystems,
    actionLabel: "Open Accounting Systems",
    icon: KeyRound
  },
  {
    id: "providers",
    title: "Connect a provider",
    description: "Review connection health, start guided setup, or inspect advanced runtime evidence.",
    href: WORKSTATION_ROUTE_CATALOG.settingsProviders,
    actionLabel: "Open Provider Connections",
    icon: PlugZap
  },
  {
    id: "diagnostics",
    title: "Investigate a problem",
    description: "Start with failed services and recent events before opening technical coverage.",
    href: WORKSTATION_ROUTE_CATALOG.settingsDiagnostics,
    actionLabel: "Open Diagnostics",
    icon: MonitorCheck
  }
] as const;

export function SettingsTaskChooser() {
  return (
    <section aria-labelledby="settings-task-chooser-title" className="grid gap-4">
      <div>
        <h3 id="settings-task-chooser-title" className="text-base font-semibold text-foreground">
          What do you need to do?
        </h3>
        <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
          Each task opens a focused settings surface. Advanced controls stay behind the relevant provider or diagnostics route.
        </p>
      </div>
      <div role="list" aria-label="Settings tasks" className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {settingsTasks.map((task) => {
          const Icon = task.icon;
          return (
            <div key={task.id} role="listitem" className="panel-surface rounded-lg border border-border/70 bg-background/35 p-4">
              <div className="flex items-start gap-3">
                <span className="grid h-9 w-9 shrink-0 place-items-center rounded-md border border-primary/25 bg-primary/10 text-primary">
                  <Icon className="h-4 w-4" aria-hidden="true" />
                </span>
                <div className="min-w-0">
                  <h4 className="text-sm font-semibold text-foreground">{task.title}</h4>
                  <p className="mt-1 text-xs leading-5 text-muted-foreground">{task.description}</p>
                </div>
              </div>
              <Link
                to={task.href}
                className="mt-4 inline-flex min-h-9 items-center gap-2 rounded-md border border-border/70 px-3 py-2 text-xs font-semibold text-foreground transition-colors hover:bg-secondary/45 focus:outline-none focus:ring-2 focus:ring-primary/40"
                aria-label={`${task.actionLabel}: ${task.description}`}
              >
                {task.actionLabel}
                <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
              </Link>
            </div>
          );
        })}
      </div>
    </section>
  );
}
