import { ArrowRight } from "lucide-react";
import { Link } from "react-router-dom";
import { accountingTaskModeLauncherLinks } from "@/screens/accounting-screen.task-mode-view-model";

export function AccountingTaskModeLauncher() {
  return (
    <section
      className="workspace-section-band"
      role="navigation"
      aria-label="Accounting task modes"
    >
      <div className="workspace-section-subheader">
        <div>
          <p className="eyebrow-label">Task modes</p>
          <h3 className="workspace-section-title">Open focused accounting work</h3>
          <p className="workspace-section-summary">
            The close cockpit stays on daily blockers, owners, affected outputs, next actions, and retained proof; focused routes hold the heavier accounting work.
          </p>
        </div>
      </div>
      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-6">
        {accountingTaskModeLauncherLinks.map((mode) => (
          <Link
            key={mode.id}
            to={mode.href}
            className="group rounded-md border border-border/70 bg-background/75 px-4 py-3 text-sm transition hover:border-primary/50 hover:bg-primary/5 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            aria-label={`Open ${mode.label} accounting task mode`}
          >
            <span className="flex items-start justify-between gap-3">
              <span className="font-semibold text-foreground">{mode.label}</span>
              <ArrowRight className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground transition group-hover:text-primary" aria-hidden="true" />
            </span>
            <span className="mt-2 block text-xs leading-5 text-muted-foreground">{mode.description}</span>
          </Link>
        ))}
      </div>
    </section>
  );
}
