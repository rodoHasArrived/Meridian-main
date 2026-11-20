import { useEffect, useId, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogCloseButton, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import {
  beginTemplateSetup, discardTemplateSetup, exportConnectionTemplate, importConnectionTemplates,
  listConnectionTemplates, putTemplateSetupCredentials, saveConnectionAsTemplate, saveTemplateSetup, testTemplateSetup,
  type ProviderConnectionTemplateOption, type ProviderTemplateSetupDraft, type ProviderTemplateSetupResult,
  type SavedTemplateConnection
} from "@/lib/api/provider-connection-templates.api";
import type { ProviderRoutingConnection } from "@/types";

const selectClass = "h-9 w-full rounded-md border border-border bg-background px-3 text-sm focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-primary/40";
const buttonRow = "flex flex-wrap justify-end gap-2";

export function ProviderConnectionTemplatesDialog({ connections, onClose, onManual, onSaved }: {
  connections: readonly ProviderRoutingConnection[];
  onClose: () => void;
  onManual: () => void;
  onSaved?: (() => Promise<void> | void) | undefined;
}) {
  const id = useId();
  const [catalogue, setCatalogue] = useState<ProviderConnectionTemplateOption[]>([]);
  const [catalogueVersion, setCatalogueVersion] = useState(0);
  const [loading, setLoading] = useState(true);
  const [catalogueError, setCatalogueError] = useState<ApiErrorDisplay | null>(null);
  const [selectedName, setSelectedName] = useState("");
  const [reviewed, setReviewed] = useState<ProviderConnectionTemplateOption | null>(null);
  const [account, setAccount] = useState("");
  const [draft, setDraft] = useState<ProviderTemplateSetupDraft | null>(null);
  const [credentials, setCredentials] = useState<Record<string, string>>({});
  const [acknowledged, setAcknowledged] = useState(false);
  const [test, setTest] = useState<ProviderTemplateSetupResult | null>(null);
  const [saved, setSaved] = useState<SavedTemplateConnection | null>(null);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [busy, setBusy] = useState(false);
  const [notification, setNotification] = useState("");
  const [sourceId, setSourceId] = useState("");
  const [newName, setNewName] = useState("");
  const setupId = useRef<string | null>(null);
  const operation = useRef<AbortController | null>(null);
  const stageHeading = useRef<HTMLHeadingElement>(null);
  const selected = catalogue.find(item => item.template.name === selectedName);
  const requiresLiveAcknowledgement = reviewed?.template.connectionMode === "Live";
  const stage = saved ? "saved" : draft ? "vault" : reviewed ? "review" : "catalogue";

  useEffect(() => {
    const controller = new AbortController();
    void listConnectionTemplates(controller.signal).then(items => {
      if (controller.signal.aborted) return;
      setCatalogue(items);
      setSelectedName(name => items.some(item => item.template.name === name) ? name : (items[0]?.template.name ?? ""));
    }).catch(reason => {
      if (!controller.signal.aborted) setCatalogueError(describeApiError(reason, "Connection templates are unavailable."));
    }).finally(() => {
      if (!controller.signal.aborted) setLoading(false);
    });
    return () => controller.abort();
  }, [catalogueVersion]);

  useEffect(() => () => {
    operation.current?.abort();
    if (setupId.current) void discardTemplateSetup(setupId.current).catch(() => undefined);
  }, []);
  useEffect(() => { if (stage !== "catalogue") stageHeading.current?.focus(); }, [stage]);

  async function run(action: (signal: AbortSignal) => Promise<void>, fallback: string) {
    if (operation.current) return;
    const controller = new AbortController();
    operation.current = controller;
    setBusy(true);
    setError(null);
    setNotification("");
    try {
      await action(controller.signal);
    } catch (reason) {
      if (!controller.signal.aborted) setError(describeApiError(reason, fallback));
    } finally {
      operation.current = null;
      if (!controller.signal.aborted) setBusy(false);
    }
  }

  function reloadCatalogue() {
    setCatalogueError(null);
    setLoading(true);
    setCatalogueVersion(version => version + 1);
  }

  function leaveSetup(after: () => void) {
    if (operation.current) return;
    if (!setupId.current) { after(); return; }
    const idToDiscard = setupId.current;
    void run(async signal => {
      await discardTemplateSetup(idToDiscard, signal);
      setupId.current = null;
      setCredentials({});
      after();
    }, "The draft could not be discarded. Try again.");
  }

  function close() { leaveSetup(onClose); }

  function openVault() {
    if (!reviewed) return;
    const idToStart = setupId.current ??= crypto.randomUUID();
    void run(async signal => {
      const prepared = await beginTemplateSetup({
        setupId: idToStart, templateName: reviewed.template.name, revision: reviewed.revision,
        ...(reviewed.requiresExternalAccount ? { externalAccountId: account.trim() } : {})
      }, signal);
      setAccount("");
      setDraft(prepared);
    }, "The credential vault is unavailable.");
  }

  function testConnection() {
    if (!draft || (requiresLiveAcknowledgement && !acknowledged)) return;
    setTest(null);
    void run(async signal => {
      const entered = Object.fromEntries(Object.entries(credentials).filter(([, value]) => value.trim()));
      if (Object.keys(entered).length) {
        await putTemplateSetupCredentials(draft, entered, signal);
        setCredentials({});
      }
      setTest(await testTemplateSetup(draft.setupId, signal));
    }, "The connection test could not be completed.");
  }

  function saveConnection() {
    if (!draft || !test?.canSave) return;
    void run(async signal => {
      const result = await saveTemplateSetup(draft.setupId, signal);
      setupId.current = null;
      setCredentials({});
      setSaved(result);
      void Promise.resolve().then(onSaved).catch(() => undefined);
    }, "The connection save was not confirmed. Retry to check the same draft.");
  }

  function exportSelected() {
    if (!selected) return;
    void run(async signal => {
      const blob = await exportConnectionTemplate(selected.template.name, signal);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = `${selected.template.name.toLowerCase().replace(/ /g, "-")}.json`;
      link.click();
      window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    }, "The selected template could not be exported.");
  }

  function importFile(file: File) {
    void run(async signal => {
      if (file.size > 64 * 1024) throw new Error("Choose a JSON template file no larger than 64 KB.");
      const result = await importConnectionTemplates(await file.text(), signal);
      reloadCatalogue();
      setNotification(result.imported ? `Imported ${result.imported} template(s).` : "The templates are already available.");
    }, "No templates were imported.");
  }

  function saveSource() {
    void run(async signal => {
      const result = await saveConnectionAsTemplate(sourceId, newName.trim(), signal);
      reloadCatalogue();
      setNewName("");
      setSelectedName(result.template.name);
      setNotification(`${result.template.name} is available in the template catalog.`);
    }, "The connection could not be saved as a template.");
  }

  return (
    <Dialog open onOpenChange={open => { if (!open) close(); }}>
      <DialogContent aria-labelledby={`${id}-title`} aria-describedby={`${id}-description`}>
        <div className="flex items-start justify-between gap-3">
          <DialogHeader>
            <DialogTitle id={`${id}-title`}>Add provider connection</DialogTitle>
            <DialogDescription id={`${id}-description`}>Start with a reusable set of provider settings.</DialogDescription>
          </DialogHeader>
          <DialogCloseButton disabled={busy} onClick={close} />
        </div>
        {error ? <StatusBanner role="alert" tone="danger" title={error.summary}
          detail={error.details.length ? <ul className="mt-2 list-disc pl-5">{error.details.map(detail => <li key={detail}>{detail}</li>)}</ul> : null} /> : null}
        {notification ? <p className="mb-2 text-sm" role="status">{notification}</p> : null}
        {stage === "catalogue" ? <div className="grid gap-4">
          {catalogueError ? <div className="grid gap-2">
            <StatusBanner role="alert" tone="warning" title={catalogueError.summary} />
            <Button className="justify-self-start" size="sm" variant="outline" onClick={reloadCatalogue}>Reload templates</Button>
          </div> : null}
          <div className="grid gap-1 text-sm">
            <label htmlFor={`${id}-template`}>Connection template</label>
            <select id={`${id}-template`} className={selectClass} value={selectedName} disabled={loading || busy || !catalogue.length}
              onChange={event => setSelectedName(event.target.value)}>
              {catalogue.length ? catalogue.map(item => <option key={item.template.name}>{item.template.name}</option>)
                : <option value="">{loading ? "Loading templates…" : "No templates available"}</option>}
            </select>
          </div>
          <div className="flex flex-wrap justify-between gap-2">
            <Button disabled={!selected || busy || loading} onClick={() => { setError(null); setReviewed(selected ?? null); }}>Use template</Button>
            <Button variant="outline" disabled={busy} onClick={onManual}>Configure manually</Button>
          </div>
          {catalogue.length > 0 ? <details className="border-t border-border pt-3">
            <summary className="cursor-pointer rounded-sm text-sm font-medium focus-visible:ring-2 focus-visible:ring-primary/40">Manage connection templates</summary>
            <div className="mt-3 grid gap-3">
              <Button variant="outline" disabled={!selected || busy || loading} onClick={exportSelected}>Export selected template</Button>
              <label className="grid gap-1 text-sm">Import template JSON
                <Input type="file" accept=".json,application/json" disabled={busy || loading} onChange={event => {
                  const file = event.currentTarget.files?.[0];
                  event.currentTarget.value = "";
                  if (file) importFile(file);
                }} />
              </label>
              <div className="grid gap-1 text-sm">
                <label htmlFor={`${id}-source`}>Source connection</label>
                <select id={`${id}-source`} className={selectClass} value={sourceId} disabled={busy || !connections.length} onChange={event => setSourceId(event.target.value)}>
                  <option value="">{connections.length ? "Select an owned connection" : "No owned connections available"}</option>
                  {connections.map(row => <option key={row.connectionId} value={row.connectionId}>
                    {row.displayName} · {row.credentialEnvironment} · {row.connectionId.slice(0, 8)}
                  </option>)}
                </select>
              </div>
              <label className="grid gap-1 text-sm">New template name
                <Input value={newName} onChange={event => setNewName(event.target.value)} maxLength={64} disabled={busy} />
              </label>
              <Button variant="outline" disabled={!sourceId || !newName.trim() || busy || loading} onClick={saveSource}>Save connection as template</Button>
            </div>
          </details> : null}
        </div> : null}

        {stage === "review" && reviewed ? <div className="grid gap-4">
          <h3 ref={stageHeading} tabIndex={-1} className="font-semibold">{reviewed.template.name}</h3>
          <TemplateReview option={reviewed} />
          {reviewed.requiresExternalAccount ? <label className="grid gap-1 text-sm">Provider account ID
            <Input value={account} maxLength={512} autoComplete="off" disabled={busy} onChange={event => setAccount(event.target.value)} />
            <span className="text-xs text-muted-foreground">This identifies the account for this connection.</span>
          </label> : null}
          <div className={buttonRow}>
            <Button variant="outline" disabled={busy} onClick={() => leaveSetup(() => { setReviewed(null); setAccount(""); })}>Back</Button>
            <Button disabled={busy || (reviewed.requiresExternalAccount && !account.trim())} onClick={openVault}>Continue to vault</Button>
          </div>
        </div> : null}

        {stage === "vault" && draft && reviewed ? <div className="grid gap-4">
          <h3 ref={stageHeading} tabIndex={-1} className="font-semibold">Credential vault</h3>
          {draft.credentialFields.length ? <div className="grid gap-3">
            {draft.credentialFields.map(field => <label key={field.name} className="grid gap-1 text-sm">
              {field.label}{field.required ? " (required)" : ""}
              <Input type={field.inputKind === "Password" ? "password" : "text"}
                autoComplete={field.inputKind === "Password" ? "new-password" : "off"}
                value={credentials[field.name] ?? ""} disabled={busy} onChange={event => {
                  const value = event.target.value;
                  setCredentials(previous => ({ ...previous, [field.name]: value }));
                  setTest(null);
                }} />
            </label>)}
          </div> : <p className="text-sm text-muted-foreground">This provider requires no credentials.</p>}
          {requiresLiveAcknowledgement ? <Checkbox checked={acknowledged} disabled={busy}
            onCheckedChange={value => { setAcknowledged(value); setTest(null); }} label="I am using credentials for a live provider account." /> : null}
          <details>
            <summary className="cursor-pointer rounded-sm text-sm font-medium focus-visible:ring-2 focus-visible:ring-primary/40">Review values and verification checklist</summary>
            <div className="mt-2"><TemplateReview option={reviewed} /></div>
          </details>
          {test ? <StatusBanner role="status" tone={test.canSave ? "info" : "warning"}
            title={test.canSave ? "Test complete" : "Test needs attention"} detail={test.detail} /> : null}
          <div className={buttonRow}>
            <Button variant="outline" disabled={busy} onClick={close}>Discard draft</Button>
            <Button variant="outline" disabled={busy || (requiresLiveAcknowledgement && !acknowledged)} onClick={testConnection}>Test connection</Button>
            <Button disabled={busy || !test?.canSave} onClick={saveConnection}>Save connection</Button>
          </div>
        </div> : null}

        {saved ? <div className="grid gap-3">
          <h3 ref={stageHeading} tabIndex={-1} className="font-semibold">{saved.displayName} saved</h3>
          <p className="text-sm text-muted-foreground">Connection ID {saved.connectionId}. Routing and runtime activation require a separate binding.</p>
          <div className={buttonRow}>
            <Button variant="outline" onClick={onClose}>Done</Button>
            <Button onClick={() => { setSaved(null); setDraft(null); setReviewed(null); setTest(null); setAcknowledged(false); }}>Add another connection</Button>
          </div>
        </div> : null}
      </DialogContent>
    </Dialog>
  );
}

function TemplateReview({ option }: { option: ProviderConnectionTemplateOption }) {
  return <div className="grid gap-3">
    <table className="w-full table-fixed text-left text-sm">
      <thead className="text-xs text-muted-foreground"><tr><th className="w-2/5 px-2 py-1" scope="col">Field</th><th className="px-2 py-1" scope="col">Value to create</th></tr></thead>
      <tbody className="divide-y divide-border/70 border-y border-border">
        {option.reviewRows.map(row => <tr key={row.field}>
          <th className="px-2 py-1.5 align-top font-medium" scope="row">{row.field}</th>
          <td className="px-2 py-1.5 align-top break-words"><span aria-hidden="true" className="mr-2 text-success">+</span>{row.value}</td>
        </tr>)}
      </tbody>
    </table>
    <div>
      <h4 className="text-sm font-semibold">Verification checklist</h4>
      <ul className="mt-1 list-disc space-y-1 pl-5 text-sm text-muted-foreground">
        {option.verificationChecks.map(check => <li key={check}>{check}</li>)}
      </ul>
    </div>
  </div>;
}
