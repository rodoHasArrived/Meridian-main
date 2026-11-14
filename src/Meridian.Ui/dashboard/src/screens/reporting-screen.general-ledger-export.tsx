import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { downloadGeneralLedger, getGeneralLedgerExportAccess } from "@/lib/api/accounting-integration.api";
import type { AccountingIntegrationScope } from "@/types/accounting-integration";
import { IntegrationScopePicker, integrationError, scopeIdentity } from "./accounting-integration.scope";

export function GeneralLedgerExportPanel() {
  const [scope, setScope] = useState<AccountingIntegrationScope | null>(null);
  const [access, setAccess] = useState<"loading" | "allowed" | "denied" | "unavailable">("loading");
  const [accessError, setAccessError] = useState<string | null>(null);
  const [accessAttempt, setAccessAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setAccess("loading"); setAccessError(null);
    getGeneralLedgerExportAccess({ signal: controller.signal }).then(allowed => {
      if (!controller.signal.aborted) setAccess(allowed ? "allowed" : "denied");
    }).catch(reason => {
      if (!controller.signal.aborted) { setAccess("unavailable"); setAccessError(integrationError(reason)); }
    });
    return () => controller.abort();
  }, [accessAttempt]);
  return <Card aria-label="General ledger accounting export">
    <CardHeader><CardTitle>General ledger accounting export</CardTitle>
      <CardDescription>One captured ledger snapshot supplies CSV, XLSX, Parquet and the manifest in a ZIP. Report availability and accounting evidence remain server-owned.</CardDescription></CardHeader>
    <CardContent className="flex flex-col gap-4"><IntegrationScopePicker onSelect={setScope} accountRequired={false} />
      {access === "loading" ? <p role="status" className="text-sm text-muted-foreground">Checking this session's accounting export permission…</p>
        : access === "allowed" ? scope ? <ExportCommand key={scopeIdentity(scope)} scope={scope} />
          : <p className="text-sm text-muted-foreground">Select a retained book and period before downloading.</p>
          : <div className="flex flex-col gap-2"><p role={access === "unavailable" ? "alert" : "status"} className="text-sm text-muted-foreground">
            {access === "denied" ? "This session does not have ExportData permission. Ledger read access does not authorize an accounting download." : accessError}
          </p><Button type="button" variant="outline" size="sm" onClick={() => setAccessAttempt(value => value + 1)}>Recheck export permission</Button></div>}
    </CardContent>
  </Card>;
}
function ExportCommand({ scope }: { scope: AccountingIntegrationScope }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const alive = useRef(true);
  useEffect(() => { alive.current = true; return () => { alive.current = false; }; }, []);
  async function download() {
    setBusy(true); setError(null); setNotice(null);
    try {
      if (!await getGeneralLedgerExportAccess()) throw new Error("This session no longer has ExportData permission. Sign in with authorized export access before retrying.");
      const blob = await downloadGeneralLedger(scope.book.ledgerBookId, scope.period.periodId);
      if (!alive.current) return;
      if (blob.type && !blob.type.startsWith("application/zip")) throw new Error("The server did not return an accounting ZIP. Refresh retained scope before retrying.");
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url; anchor.download = `general-ledger.v1-${scope.period.periodId}.zip`;
      document.body.append(anchor); anchor.click(); anchor.remove();
      setTimeout(() => URL.revokeObjectURL(url), 0);
      setNotice("Accounting ZIP download started. Inspect the manifest for snapshot identity and report availability.");
    } catch (reason) { if (alive.current) setError(integrationError(reason)); }
    finally { if (alive.current) setBusy(false); }
  }
  return <div className="space-y-2"><Button size="sm" disabled={busy} onClick={() => void download()}>
    {busy ? "Preparing accounting ZIP…" : "Download general ledger ZIP"}</Button>
    {error ? <p role="alert" className="text-sm text-danger">{error}</p> : null}
    {notice ? <p role="status" className="text-sm text-success">{notice}</p> : null}
  </div>;
}
