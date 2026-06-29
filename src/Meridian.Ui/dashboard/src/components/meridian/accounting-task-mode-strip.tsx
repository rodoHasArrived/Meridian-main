import { BookCheck, Landmark, Network, Paperclip, ShieldCheck, Table2, TrendingUp, WalletCards } from "lucide-react";
import { Link } from "react-router-dom";
import type { AccountingTaskMode, AccountingTaskModeId } from "@/lib/accounting-task-modes";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

export function AccountingTaskModeStrip({ modes }: { modes: AccountingTaskMode[] }) {
  const hasRecommendedMode = modes.some((mode) => mode.recommended);

  return (
    <nav aria-label="Accounting task modes" className="panel-surface-strong px-4 py-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="eyebrow-label">Task modes</div>
          <h3 className="mt-2 text-lg font-semibold leading-tight text-foreground">Focused Accounting routes</h3>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            Keep close triage, reconciliation casework, ledger proof, journal entry, rules, Security Master, and evidence review inside the Accounting lane.
          </p>
        </div>
        <Badge variant={hasRecommendedMode ? "warning" : "success"} dot>
          {hasRecommendedMode ? "Operator attention queued" : "No urgent mode guidance"}
        </Badge>
      </div>
      <ul className="mt-4 grid gap-2 md:grid-cols-2 xl:grid-cols-4" role="list">
        {modes.map((mode) => (
          <li
            key={mode.id}
            className={cn(
              "rounded-md border px-3 py-3",
              mode.active
                ? "border-primary/45 bg-primary/10"
                : "border-border/70 bg-secondary/20"
            )}
          >
            <div className="flex min-w-0 items-start justify-between gap-3">
              <Button asChild variant={mode.active ? "outline" : "ghost"} size="sm" className="min-w-0 justify-start">
                <Link
                  to={mode.href}
                  aria-current={mode.active ? "page" : undefined}
                  aria-label={`Open ${mode.label} accounting task mode. ${mode.detail}`}
                >
                  <AccountingTaskModeIcon id={mode.id} />
                  <span className="truncate">{mode.label}</span>
                </Link>
              </Button>
              <span className="flex shrink-0 flex-wrap justify-end gap-1.5">
                {mode.recommended ? <Badge variant="warning">Recommended</Badge> : null}
                <Badge variant={mode.badgeVariant}>{mode.statusLabel}</Badge>
              </span>
            </div>
            <p className="mt-2 text-xs leading-5 text-muted-foreground">{mode.detail}</p>
            <div className="mt-3 flex flex-wrap gap-1.5">
              <Badge variant="outline">{mode.countLabel}</Badge>
              {mode.active ? <Badge variant="default">Current mode</Badge> : null}
            </div>
          </li>
        ))}
      </ul>
    </nav>
  );
}

function AccountingTaskModeIcon({ id }: { id: AccountingTaskModeId }) {
  const className = "h-4 w-4 shrink-0";
  switch (id) {
    case "close-cockpit":
      return <TrendingUp className={className} aria-hidden="true" />;
    case "reconciliation-casework":
      return <Network className={className} aria-hidden="true" />;
    case "ledger-explorer":
      return <Table2 className={className} aria-hidden="true" />;
    case "journal-entry":
      return <BookCheck className={className} aria-hidden="true" />;
    case "capital-accounts":
      return <WalletCards className={className} aria-hidden="true" />;
    case "rules":
      return <Landmark className={className} aria-hidden="true" />;
    case "security-master":
      return <ShieldCheck className={className} aria-hidden="true" />;
    case "evidence":
      return <Paperclip className={className} aria-hidden="true" />;
    default:
      return <ShieldCheck className={className} aria-hidden="true" />;
  }
}
