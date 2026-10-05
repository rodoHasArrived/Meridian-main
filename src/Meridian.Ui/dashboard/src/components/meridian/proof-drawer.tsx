import { Link2 } from "lucide-react";
import { useEffect, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Drawer, DrawerBody } from "@/components/ui/drawer";
import { NumberPassport } from "@/components/meridian/number-passport";
import { hasExactRetainedSource, isCurrentRetainedEvidence } from "@/components/meridian/ledger-amount-proof-validation";
import { getLedgerAmountProof } from "@/lib/ledger-amount-proof-api";
import { cn } from "@/lib/utils";
import type { LedgerAmountProof, LedgerAmountSelection } from "@/types/ledger-amount-proof";
import type {
  FinancialRecordExplorerDto,
  FinancialRecordExplorerSelectedRecordDto,
  FinancialRecordExplorerTone
} from "@/types";

/** Browser presentation of the same subject-addressed payload consumed by WPF. */
export function LedgerAmountProofDrawer({ selection, onClose }: {
  selection: LedgerAmountSelection | null;
  onClose: () => void;
}) {
  return (
    <Drawer open={selection !== null} onClose={onClose} title={selection ? `${selection.label} proof detail` : "Amount proof detail"} className="max-w-[32rem]">
      <DrawerBody>{selection ? <LedgerAmountProofContent selection={selection} /> : null}</DrawerBody>
    </Drawer>
  );
}

function LedgerAmountProofContent({ selection }: { selection: LedgerAmountSelection }) {
  const selectionKey = JSON.stringify(selection);
  const [result, setResult] = useState<{ key: string; proof: LedgerAmountProof | null; error: string | null } | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    let current = true;
    const requested = JSON.parse(selectionKey) as LedgerAmountSelection;
    if (!requested.subjectId || !requested.ledgerBookId || !requested.periodId || !requested.fundProfileId) {
      setResult({ key: selectionKey, proof: null, error: "Blocked: the selected amount has incomplete book, period or fund identity." });
      return () => controller.abort();
    }
    void getLedgerAmountProof(requested, { signal: controller.signal }).then((packet) => {
      if (!current) return;
      const proof = packet.ledgerAmount;
      const scope = proof?.scope;
      if (!proof || packet.completeness.status !== proof.status || packet.subject.subjectKind !== "ledger-amount" || packet.subject.subjectId !== requested.subjectId ||
        proof.subjectId !== requested.subjectId || scope?.ledgerBookId !== requested.ledgerBookId ||
        scope?.periodId !== requested.periodId || scope?.fundProfileId !== requested.fundProfileId ||
        !scope.tenantId || !scope.companyId || proof.amount !== requested.amount || proof.currency !== requested.currency) {
        setResult({ key: selectionKey, proof: null, error: "Blocked: evidence identity, amount or scope does not match the selection." });
      } else if (new Set(proof.evidence.map((item) => item.evidenceId)).size !== proof.evidence.length ||
        proof.evidence.some((item) => !item.evidenceId)) {
        setResult({ key: selectionKey, proof: null, error: "Blocked: evidence identifiers are missing or ambiguous." });
      } else if (proof.evidence.some((item) => item.kind !== "ledger-record" && isCurrentRetainedEvidence(item) && !hasExactRetainedSource(packet, proof, item))) {
        setResult({ key: selectionKey, proof: null, error: "Blocked: supporting evidence does not identify this exact retained amount, scope and content digest." });
      } else {
        setResult({ key: selectionKey, proof, error: null });
      }
    }).catch(() => {
      if (current) setResult({ key: selectionKey, proof: null, error: "Blocked: retained evidence for this amount is unavailable. Review the selected book and period." });
    });
    return () => { current = false; controller.abort(); };
  }, [selectionKey]);

  // A new selection immediately hides the previous payload, before its request completes.
  if (!result || result.key !== selectionKey) return <p role="status">Loading retained amount evidence…</p>;
  if (result.error || !result.proof) return <p role="alert">{result.error}</p>;
  const proof = result.proof;
  const blocked = proof.status === "Blocked" || proof.evidence.some((item) => item.status === "Blocked");
  const retained = proof.evidence.filter(isCurrentRetainedEvidence);
  const supporting = retained.filter((item) => item.kind !== "ledger-record");
  const ready = !blocked && proof.status === "Ready" && supporting.length > 0 && retained.length === proof.evidence.length;
  const posture = blocked ? "Blocked" : ready ? "Ready" : "Review required";

  return (
    <div className="space-y-3" aria-label="Selected amount evidence">
      <p role="status"><Badge variant={blocked ? "danger" : ready ? "success" : "warning"}>{posture}</Badge></p>
      {proof.warnings.length > 0 ? <ul aria-label="Evidence warnings" className="list-disc pl-5 text-sm">{proof.warnings.map((warning, index) => <li key={index}>{warning}</li>)}</ul> : null}
      <section aria-label="Retained supporting evidence" className="space-y-2">
        <h3 className="text-sm font-semibold">Retained supporting evidence</h3>
        {blocked || supporting.length === 0 ? <p>No current retained supporting evidence is available for this amount.</p> : supporting.map((item) => (
          <article key={item.evidenceId} className="rounded-md border border-border/70 p-3">
            <p className="font-semibold">{item.label}</p>
            <p className="text-xs text-muted-foreground">{item.kind} · {item.sourceSystem} · {item.retainedAt}</p>
            <p className="break-all font-mono text-xs">{item.evidenceId}</p>
            {item.contentHash ? <p className="break-all font-mono text-xs">{item.contentHash}</p> : null}
            {item.route?.startsWith("/") && !item.route.startsWith("//") ? <a className="text-sm text-primary underline" href={item.route}>Open {item.label}</a> : null}
          </article>
        ))}
      </section>
      <NumberPassport proof={{ ...proof, status: blocked ? "Blocked" : ready ? "Ready" : "ReviewRequired" }} title={selection.label} />
    </div>
  );
}

export function ProofDrawer({
  explorer,
  record,
  blockedReason
}: {
  explorer: FinancialRecordExplorerDto;
  record: FinancialRecordExplorerSelectedRecordDto | null;
  blockedReason: string;
}) {
  if (!record) {
    return (
      <div role="status" className="rounded-md border border-border/70 bg-background/60 p-4 text-sm text-muted-foreground">
        {blockedReason || "Select a source-backed row to inspect fields, proof actions, Used In, and Impacts."}
      </div>
    );
  }

  return (
    <div role="region" className="space-y-3" aria-label={`${record.title} proof detail`}>
      <div>
        <Badge variant={toneToBadge(record.tone)}>{record.recordType}</Badge>
        <h3 className="mt-3 text-base font-semibold text-foreground">{record.title}</h3>
        <p className="mt-1 text-xs text-muted-foreground">{record.subtitle}</p>
        <p className="mt-2 text-sm leading-6 text-muted-foreground">{record.description}</p>
      </div>
      <ProofActionList actions={record.proofActions} />
      <NumberPassport explorer={explorer} record={record} />
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
    </div>
  );
}

function ProofActionList({ actions }: { actions: FinancialRecordExplorerSelectedRecordDto["proofActions"] }) {
  if (actions.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-wrap gap-2">
      {actions.map((action) => <ProofActionButton key={action.actionId} action={action} />)}
    </div>
  );
}

export function ProofActionButton({ action }: { action: FinancialRecordExplorerSelectedRecordDto["proofActions"][number] }) {
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
    <section className="grid gap-2" aria-label={title}>
      <h4 className="text-xs font-semibold uppercase text-muted-foreground">{title}</h4>
      {items.map((item) => (
        <dl key={`${item.label}-${item.value}`} className="rounded-md border border-border/60 px-3 py-2">
          <dt className="text-[11px] text-muted-foreground">{item.label}</dt>
          <dd className={cn("mt-1 font-mono text-sm", toneTextClass(item.tone))}>
            {item.value}
            {item.detail ? <span className="mt-1 block font-sans text-xs text-muted-foreground">{item.detail}</span> : null}
          </dd>
        </dl>
      ))}
    </section>
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
              <Badge variant={toneToBadge(item.tone)}>{item.tone}</Badge>
            </div>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">{item.description}</p>
          </div>
        ))}
      </div>
    </section>
  );
}

function toneToBadge(tone?: FinancialRecordExplorerTone): "default" | "outline" | "success" | "warning" | "danger" {
  switch (tone) {
    case "Success":
      return "success";
    case "Warning":
      return "warning";
    case "Danger":
      return "danger";
    case "Info":
      return "default";
    default:
      return "outline";
  }
}

function toneTextClass(tone?: FinancialRecordExplorerTone): string {
  switch (tone) {
    case "Success":
      return "text-success";
    case "Warning":
      return "text-warning";
    case "Danger":
      return "text-danger";
    default:
      return "text-foreground";
  }
}
