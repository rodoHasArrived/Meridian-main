import { useEffect, useRef, useState } from "react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { activatePostingGrant, getIntegrationMapping, getPostingGrant, revokePostingGrant } from "@/lib/api/accounting-integration.api";
import type { AccountingIntegrationScope, AutomatedPostingGrant } from "@/types/accounting-integration";
import { IntegrationScopePicker, integrationError, isGuid, scopeIdentity } from "./accounting-integration.scope";

export function AutomaticPostingAuthorityPanel() {
  const [scope, setScope] = useState<AccountingIntegrationScope | null>(null);
  return <Card aria-label="Automatic fill posting authority">
    <CardHeader><CardTitle>Automatic fill posting authority <Badge variant="outline">Synthetic only</Badge></CardTitle>
      <CardDescription>Delegate USD long cash-equity fills, commissions and identifiable fees for one account and open period. Admin maintenance permission is required.</CardDescription></CardHeader>
    <CardContent className="space-y-4"><IntegrationScopePicker onSelect={setScope} />
      {scope ? <GrantCommands key={scopeIdentity(scope)} scope={scope} /> : <p className="text-sm text-muted-foreground">Select an account, book and period to inspect or activate authority.</p>}
    </CardContent>
  </Card>;
}

function GrantCommands({ scope }: { scope: AccountingIntegrationScope }) {
  const [grantId, setGrantId] = useState<string>(() => crypto.randomUUID());
  const [grant, setGrant] = useState<AutomatedPostingGrant | null>(null);
  const [mapping, setMapping] = useState<string | null>(null);
  const [confirmed, setConfirmed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  const sameScope = grant && grant.reference.ledgerBookId === scope.book.ledgerBookId
    && grant.reference.periodId === scope.period.periodId && grant.reference.financialAccountId === scope.accountId;
  async function command(action: () => Promise<void>) {
    setBusy(true); setError(null); setNotice(null);
    try { await action(); } catch (reason) { if (alive.current) setError(integrationError(reason)); }
    finally { if (alive.current) setBusy(false); }
  }
  return <div className="space-y-3">
    <dl className="grid gap-2 text-xs md:grid-cols-2">
      <div><dt>Policy</dt><dd className="font-mono">{scope.book.accountingPolicyId} / {scope.book.accountingPolicyVersion}</dd></div>
      <div><dt>Mapping revision</dt><dd className="font-mono">{mapping ?? "Load current retained mapping before activation"}</dd></div>
    </dl>
    <div className="flex flex-wrap items-end gap-2">
      <label className="min-w-64 flex-1 space-y-1 text-xs">Grant ID
        <Input value={grantId} disabled={busy || Boolean(grant)} onChange={event => setGrantId(event.target.value.trim())} autoComplete="off" />
      </label>
      <Button variant="outline" size="sm" disabled={busy || !isGuid(grantId)} onClick={() => void command(async () => {
        const retained = await getPostingGrant(grantId);
        if (alive.current) { setGrant(retained); setNotice("Retained authority loaded. Inspect its scope before revocation."); }
      })}>Inspect grant</Button>
      <Button variant="outline" size="sm" disabled={busy} onClick={() => void command(async () => {
        const result = await getIntegrationMapping(scope.book);
        if (alive.current) { setMapping(result.configurationVersion); setConfirmed(false); }
      })}>Load current mapping</Button>
    </div>
    {grant ? <div className="rounded border border-border p-3 text-xs" aria-label="Retained posting grant">
      <p className="font-semibold">{grant.isActive ? "Active" : "Revoked"} · Version {grant.reference.version}</p>
      <p className="break-all">Account {grant.reference.financialAccountId} · Book {grant.reference.ledgerBookId} · Period {grant.reference.periodId}</p>
      <p>Activated by {grant.activatedBy} at {grant.activatedAtUtc}. {grant.revokedBy ? `Revoked by ${grant.revokedBy} at ${grant.revokedAtUtc ?? "time unavailable"}.` : ""}</p>
      {!sameScope ? <p role="alert" className="text-warning">This grant belongs to another selected scope. Revocation is unavailable here.</p> : null}
    </div> : null}
    {!grant || grant.isActive ? <Checkbox checked={confirmed} disabled={busy} onCheckedChange={setConfirmed}
      label={grant?.isActive ? "I confirm revoking this exact grant. New postings will be blocked; retained exact retries remain inspectable." : "I authorize automatic posting in this exact synthetic scope. The system records its grant and actor; it does not claim per-fill human approval."} /> : null}
    <div className="flex flex-wrap gap-2">
      {!grant ? <Button size="sm" disabled={busy || !confirmed || !mapping || !isGuid(grantId) || scope.period.status !== "Open" || scope.book.baseCurrency !== "USD"}
        onClick={() => void command(async () => {
          const result = await activatePostingGrant({ grantId, financialAccountId: scope.accountId, ledgerBookId: scope.book.ledgerBookId,
            periodId: scope.period.periodId, policyId: scope.book.accountingPolicyId, policyVersion: scope.book.accountingPolicyVersion,
            mappingRevision: mapping!, sourceMode: "Synthetic", actionOrigin: "HumanOperator" });
          if (alive.current) { setGrant(result); setConfirmed(false); setNotice("Automatic posting authority activated for the retained scope."); }
        })}>Activate synthetic posting</Button> : null}
      {grant?.isActive ? <Button variant="destructive" size="sm" disabled={busy || !confirmed || !sameScope}
        onClick={() => void command(async () => {
          const result = await revokePostingGrant(grant.reference.grantId, grant.reference.version);
          if (alive.current) { setGrant(result); setConfirmed(false); setNotice("Authority revoked. New postings require a new operator grant."); }
        })}>Revoke authority</Button> : null}
      {grant && !grant.isActive ? <Button variant="outline" size="sm" disabled={busy} onClick={() => {
        setGrant(null); setGrantId(crypto.randomUUID()); setConfirmed(false); setNotice(null);
      }}>Prepare new grant</Button> : null}
    </div>
    <p className="text-xs text-muted-foreground">Activation binds the current policy and mapping revision. After an uncertain response, inspect the same grant ID before retrying.</p>
    {busy ? <p role="status" className="text-sm">Reading or updating retained authority…</p> : null}
    {notice ? <p role="status" className="text-sm text-success">{notice}</p> : null}
    {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
  </div>;
}
