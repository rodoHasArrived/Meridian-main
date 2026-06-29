import { Link2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { FinancialRecordNumberPassport } from "@/components/meridian/financial-record-number-passport";
import { financialRecordToneTextClass, financialRecordToneToBadge } from "@/lib/financial-record-explorer-tone";
import { cn } from "@/lib/utils";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerRowDto,
  FinancialRecordExplorerSelectedRecordDto
} from "@/types";

export function FinancialRecordProofDrawer({
  id,
  record,
  row,
  explorer,
  blockedReason
}: {
  id?: string;
  record: FinancialRecordExplorerSelectedRecordDto | null;
  row: FinancialRecordExplorerRowDto | null;
  explorer: FinancialRecordExplorerDto | null;
  blockedReason: string;
}) {
  if (!record) {
    return (
      <aside id={id} className="rounded-md border border-border/70 bg-background/60 p-4 text-sm text-muted-foreground">
        {blockedReason || "Select a source-backed row to inspect fields, proof actions, Used In, and Impacts."}
      </aside>
    );
  }

  return (
    <aside id={id} className="space-y-3 rounded-md border border-border/70 bg-background/60 p-4" aria-label={`${record.title} proof detail`}>
      <div>
        <Badge variant={financialRecordToneToBadge(record.tone)}>{record.recordType}</Badge>
        <h3 className="mt-3 text-base font-semibold text-foreground">{record.title}</h3>
        <p className="mt-1 text-xs text-muted-foreground">{record.subtitle}</p>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">{record.description}</p>
      </div>
      <FinancialRecordNumberPassport record={record} row={row} explorer={explorer} />
      <ProofActionList actions={record.proofActions} />
      <FactList title="Fields" items={record.fields} />
      <RelationshipList title="Used In" items={record.usedIn} />
      <RelationshipList title="Impacts" items={record.impacts} />
      {record.fullRecordHref ? (
        <Button asChild size="sm" variant="outline">
          <a href={record.fullRecordHref}>
            <Link2 className="h-3.5 w-3.5" aria-hidden="true" />
            Full record
          </a>
        </Button>
      ) : null}
    </aside>
  );
}

function ProofActionList({ actions }: { actions: FinancialRecordExplorerSelectedRecordDto["proofActions"] }) {
  if (actions.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap gap-2">
      {actions.map((action) => <FinancialRecordProofActionButton key={action.actionId} action={action} />)}
    </div>
  );
}

export function FinancialRecordProofActionButton({ action }: { action: FinancialRecordExplorerSelectedRecordDto["proofActions"][number] }) {
  if (!action.isEnabled || !action.href) {
    return (
      <Button size="sm" variant="outline" disabled disabledReason={action.disabledReason || action.description}>
        {action.label}
      </Button>
    );
  }

  return (
    <Button asChild size="sm" variant="outline">
      <a href={action.href}>{action.label}</a>
    </Button>
  );
}

function FactList({ title, items }: { title: string; items: FinancialRecordExplorerSelectedRecordDto["fields"] }) {
  if (items.length === 0) {
    return null;
  }

  return (
    <dl className="grid gap-2">
      <dt className="text-xs font-semibold uppercase text-muted-foreground">{title}</dt>
      {items.map((item) => (
        <div key={`${item.label}-${item.value}`} className="rounded-md border border-border/60 px-3 py-2">
          <dt className="text-[11px] text-muted-foreground">{item.label}</dt>
          <dd className={cn("mt-1 font-mono text-sm", financialRecordToneTextClass(item.tone))}>{item.value}</dd>
          {item.detail ? <p className="mt-1 text-xs text-muted-foreground">{item.detail}</p> : null}
        </div>
      ))}
    </dl>
  );
}

function RelationshipList({
  title,
  items
}: {
  title: string;
  items: FinancialRecordExplorerSelectedRecordDto["usedIn"];
}) {
  if (items.length === 0) {
    return null;
  }

  return (
    <section>
      <h4 className="text-xs font-semibold uppercase text-muted-foreground">{title}</h4>
      <div className="mt-2 space-y-2">
        {items.map((item) => (
          <div key={item.relationshipId} className="rounded-md border border-border/60 px-3 py-2">
            <div className="flex items-center justify-between gap-2">
              <span className="font-medium text-foreground">{item.label}</span>
              <Badge variant={financialRecordToneToBadge(item.tone)}>{item.tone}</Badge>
            </div>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.description}</p>
          </div>
        ))}
      </div>
    </section>
  );
}
