import { AlertCircle, BookCheck, Landmark, Network, Paperclip, ShieldCheck, Table2, UserCheck, WalletCards } from "lucide-react";
import { Link } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { accountingToolingBadgeVariant, accountingToolingBorderClass } from "@/screens/accounting-screen.styles";
import type { AccountingWorkflowLaunchViewState } from "@/screens/accounting-screen.view-model";
import { cn } from "@/lib/utils";

const accountingWorkflowStepIcons: Record<AccountingWorkflowLaunchViewState["steps"][number]["id"], typeof ShieldCheck> = {
  "close-cockpit": ShieldCheck,
  ledger: Table2,
  configure: Landmark,
  "journal-entries": BookCheck,
  "capital-accounts": WalletCards,
  reconciliation: Network,
  exceptions: AlertCircle,
  "security-master": ShieldCheck,
  approvals: UserCheck,
  evidence: Paperclip,
  reporting: Paperclip
};

const accountingWorkflowActionIcons: Record<string, typeof ShieldCheck> = {
  reconcile: Network,
  "journal-entry": BookCheck,
  approvals: UserCheck,
  evidence: Paperclip
};

export function AccountingWorkflowLaunchPanel({ view }: { view: AccountingWorkflowLaunchViewState }) {
  return (
    <section className="workspace-section-band" aria-labelledby="accounting-workflow-heading">
      <span className="sr-only" aria-live="polite">{view.liveRegionText}</span>
      <div className="workspace-section-subheader">
        <div className="min-w-0">
          <p className="eyebrow-label">Workflow</p>
          <h3 id="accounting-workflow-heading" className="workspace-section-title">{view.title}</h3>
          <p className="workspace-section-summary">{view.description}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={accountingToolingBadgeVariant(view.statusTone)} dot>{view.statusLabel}</Badge>
          <AccountingWorkflowChip label="Active" value={view.activeLabel} />
        </div>
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_19rem]" role="region" aria-label={view.ariaLabel}>
        <div className="grid gap-2 md:grid-cols-2 xl:grid-cols-4">
          {view.steps.map((step) => {
            const Icon = accountingWorkflowStepIcons[step.id];
            return (
              <Link
                key={step.id}
                to={step.href}
                aria-label={step.ariaLabel}
                aria-current={step.isActive ? "page" : undefined}
                className={cn(
                  "group rounded-md border px-3 py-3 transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40",
                  accountingToolingBorderClass(step.tone),
                  step.isActive && "border-primary/60 bg-primary/10"
                )}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <Icon className={cn("h-4 w-4", step.isActive ? "text-primary" : "text-muted-foreground group-hover:text-primary")} aria-hidden="true" />
                      <span className="font-semibold text-foreground">{step.label}</span>
                    </div>
                    <p className="mt-2 text-xs leading-5 text-muted-foreground">{step.caption}</p>
                  </div>
                  <Badge variant={accountingToolingBadgeVariant(step.tone)}>{step.statusLabel}</Badge>
                </div>
                <div className="mt-3 flex items-center justify-between gap-2 border-t border-border/60 pt-2 text-xs">
                  <span className="text-muted-foreground">{step.metricLabel}</span>
                  <span className="font-mono text-foreground">{step.metricValue}</span>
                </div>
              </Link>
            );
          })}
        </div>

        <div className="rounded-md border border-border/70 bg-secondary/15 px-3 py-3">
          <div className="text-xs font-semibold uppercase text-muted-foreground">Operator actions</div>
          <div className="mt-3 grid gap-2">
            {view.actionRows.map((action) => {
              const Icon = accountingWorkflowActionIcons[action.id] ?? ShieldCheck;
              return (
                <Button key={action.id} asChild variant={action.tone === "warning" || action.tone === "danger" ? "default" : "outline"} size="sm" className="h-auto justify-start py-2 text-left">
                  <Link to={action.href} aria-label={action.ariaLabel}>
                    <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                    <span className="min-w-0">
                      <span className="block font-semibold">{action.label}</span>
                      <span className="mt-1 block text-xs font-normal leading-5 text-muted-foreground">{action.detail}</span>
                    </span>
                  </Link>
                </Button>
              );
            })}
          </div>
        </div>
      </div>
    </section>
  );
}

function AccountingWorkflowChip({ label, value }: { label: string; value: string }) {
  return (
    <span className="toolbar-chip" role="group" aria-label={`${label} ${value}`}>
      <span className="text-muted-foreground">{label}</span>
      <span className="font-mono capitalize text-foreground">{value}</span>
    </span>
  );
}
