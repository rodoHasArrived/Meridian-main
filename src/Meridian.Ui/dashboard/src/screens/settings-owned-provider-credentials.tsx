import { useEffect, useRef, useState } from "react";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Checkbox } from "@/components/ui/checkbox";
import { describeApiError } from "@/lib/api-errors";
import { deleteProviderCredentials, getProviderConnections, getProviderRoutingConnections, putProviderCredentials, verifyProviderConnection } from "@/lib/api";
import { credentialOwnershipKey, ownedCredentialConnections } from "@/lib/provider-credential-scope";
import type { ProviderConnectionRow, ProviderRoutingConnection, SessionInfo } from "@/types";
import type { SettingsProviderConnectionRow } from "./settings-screen.view-model";

interface Editor {
  connectionId: string;
  choices: ProviderRoutingConnection[];
  status: ProviderConnectionRow | null;
  values: Record<string, string>;
  editing: boolean;
  busy: string | null;
  message: string;
  acknowledged: boolean;
  confirmClear: boolean;
}
const emptyEditor: Editor = { connectionId: "", choices: [], status: null, values: {}, editing: false, busy: null, message: "", acknowledged: false, confirmClear: false };

/** Credential targets come only from the authenticated server's retained connection inventory. */
export function OwnedProviderCredentialPanel({ row, session, onRefresh }: {
  row: SettingsProviderConnectionRow;
  session?: SessionInfo | null;
  onRefresh?: () => void | Promise<void>;
}) {
  const [editor, setEditor] = useState<Editor>(emptyEditor);
  const epoch = useRef(0);
  const request = useRef<AbortController | null>(null);
  const selected = editor.choices.find(choice => choice.connectionId === editor.connectionId);
  const fields = editor.status?.credentialFields ?? [];
  const sessionIdentity = session ? JSON.stringify([session.displayName, session.role, session.environment]) : null;
  const ready = Boolean(sessionIdentity && selected && editor.status);
  const busy = editor.busy !== null;
  const scopeKey = selected ? credentialOwnershipKey(selected) : "";
  const isLive = selected?.credentialEnvironment === "live" || selected?.credentialEnvironment === "production";

  function begin() {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    return { epoch: ++epoch.current, controller };
  }
  function current(token: ReturnType<typeof begin>) { return epoch.current === token.epoch && !token.controller.signal.aborted; }
  function options(token: ReturnType<typeof begin>, id?: string) {
    return { signal: token.controller.signal, allowDevelopmentFallback: false, ...(id ? { connectionId: id } : {}) };
  }
  async function loadConnections() {
    const token = begin();
    setEditor({ ...emptyEditor, busy: sessionIdentity ? "connections" : null });
    if (!sessionIdentity) { setEditor({ ...emptyEditor, message: "Sign in before managing owned connection credentials." }); return; }
    try {
      const rows = await getProviderRoutingConnections(options(token));
      if (!current(token)) return;
      const choices = ownedCredentialConnections(rows, row.providerId);
      setEditor({ ...emptyEditor, choices, message: choices.length ? "Select an owned connection before editing credentials." : "No owned connection is available. Ask your provider administrator to establish the account and environment before setup." });
    } catch (error) {
      if (current(token)) setEditor({ ...emptyEditor, message: describeApiError(error, "Owned connections are unavailable. Credential actions are blocked.").summary });
    }
  }
  useEffect(() => {
    const generation = epoch;
    const pendingRequest = request;
    void loadConnections();
    return () => { ++generation.current; pendingRequest.current?.abort(); };
    // Exposed session attributes are a UI cache discriminator, never ownership evidence.
    // Ordinary data refreshes replace SessionInfo objects; retained authority is rechecked server-side.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [row.providerId, sessionIdentity]);

  async function readStatus(choice: ProviderRoutingConnection, token: ReturnType<typeof begin>) {
    const rows = await getProviderConnections(options(token, choice.connectionId));
    if (rows.length !== 1 || rows[0].providerId.toLowerCase() !== row.providerId.toLowerCase() ||
      rows[0].externalAccountId !== choice.externalAccountId || rows[0].environment !== choice.credentialEnvironment)
      throw new Error("Credential status does not match the selected retained account and environment.");
    return rows[0];
  }
  async function selectConnection(id: string) {
    const token = begin();
    const choice = editor.choices.find(item => item.connectionId === id);
    setEditor(previous => ({ ...emptyEditor, choices: previous.choices, connectionId: choice?.connectionId ?? "", busy: choice ? "status" : null }));
    if (!choice) return;
    try {
      const status = await readStatus(choice, token);
      if (current(token)) setEditor(previous => ({ ...previous, status, busy: null, message: "Selected connection status loaded." }));
    } catch (error) {
      if (current(token)) setEditor(previous => ({ ...previous, status: null, busy: null, message: describeApiError(error, "Selected connection is unavailable. Refresh owned connections before retrying.").summary }));
    }
  }
  async function mutate(action: "save" | "verify" | "clear") {
    if (!ready || busy || !selected) return;
    if (action === "save" && isLive && !editor.acknowledged) return;
    const values = { ...editor.values };
    if (action === "save" && fields.some(field => field.required && !values[field.name]?.trim())) {
      setEditor(previous => ({ ...previous, message: "Complete every required credential field before save." }));
      return;
    }
    const token = begin();
    setEditor(previous => ({ ...previous, busy: action, values: action === "save" ? {} : previous.values, confirmClear: false, message: "" }));
    try {
      // Detect deleted or changed retained targets before sending a mutation. The server still
      // authenticates and authorizes the exact connection independently on every request.
      const choices = ownedCredentialConnections(await getProviderRoutingConnections(options(token)), row.providerId);
      if (!current(token)) return;
      if (!choices.some(choice => credentialOwnershipKey(choice) === scopeKey)) {
        setEditor({ ...emptyEditor, choices, message: "Selected ownership changed or was removed. Select a current owned connection before retrying." });
        return;
      }
      const scoped = options(token, selected.connectionId);
      let message: string;
      if (action === "save") {
        const credentials = Object.fromEntries(fields.filter(field => values[field.name]?.length).map(field => [field.name, values[field.name]]));
        const result = await putProviderCredentials(row.providerId, { credentials, environment: selected.credentialEnvironment }, scoped);
        if (result.providerId.toLowerCase() !== row.providerId.toLowerCase()) throw new Error("Credential response names another provider.");
        message = `Credentials saved (${result.credentialState}).`;
      } else if (action === "verify") {
        const result = await verifyProviderConnection(row.providerId, scoped);
        if (result.providerId.toLowerCase() !== row.providerId.toLowerCase()) throw new Error("Verification response names another provider.");
        message = result.success ? "Verification succeeded." : `Verification failed. ${result.lastError ?? ""}`;
      } else {
        const result = await deleteProviderCredentials(row.providerId, scoped);
        if (result.providerId.toLowerCase() !== row.providerId.toLowerCase()) throw new Error("Credential response names another provider.");
        message = "Credentials cleared.";
      }
      if (!current(token)) return;
      const status = await readStatus(selected, token);
      if (!current(token)) return;
      setEditor(previous => ({ ...previous, status, busy: null, values: {}, editing: action === "clear", acknowledged: false, message }));
      await onRefresh?.();
    } catch (error) {
      if (current(token)) setEditor(previous => ({ ...previous, status: null, values: {}, busy: null, message: describeApiError(error, "Credential operation was not confirmed. Refresh the selected connection before retrying.").summary }));
    }
  }

  return <section className="mb-3 grid gap-3 rounded-md border border-border/70 bg-secondary/20 px-3 py-3" aria-label={`${row.displayName} inline provider actions`}>
    <div className="flex flex-wrap items-end gap-2">
      <label className="grid gap-1 text-xs font-medium text-muted-foreground">Owned connection
        <select aria-label={`${row.displayName} owned connection`} className="h-9 rounded-md border border-border/70 bg-background px-2 text-sm text-foreground"
          value={editor.connectionId} onChange={event => void selectConnection(event.target.value)}>
          <option value="">Select an owned connection</option>
          {editor.choices.map(choice => <option key={choice.connectionId} value={choice.connectionId}>{choice.displayName} · {choice.externalAccountId} · {choice.credentialEnvironment}</option>)}
        </select>
      </label>
      <Button variant="outline" size="sm" onClick={() => void loadConnections()} aria-label={`Refresh connections for ${row.displayName}`}>Refresh connections</Button>
    </div>
    {row.credentialStatus === "not-required" ? <p className="text-xs text-muted-foreground">No credential fields are required for this provider.</p> : null}
    {selected ? <p className="text-xs text-muted-foreground">Account {selected.externalAccountId} · Environment {selected.credentialEnvironment}</p> : null}
    <div className="flex flex-wrap gap-2">
      <Button variant="outline" size="sm" disabled={!ready || busy} onClick={() => setEditor(previous => ({ ...previous, editing: !previous.editing, values: {} }))} aria-label={editor.editing ? `Close editor for ${row.displayName}` : `Edit ${row.displayName} credentials`}>{editor.editing ? "Close editor" : "Edit"}</Button>
      <Button size="sm" disabled={!ready || busy || fields.length === 0 || (isLive && !editor.acknowledged)} onClick={() => void mutate("save")} aria-label={`Save ${row.displayName} credentials`}>Save</Button>
      <Button variant="outline" size="sm" disabled={!ready || busy} onClick={() => void mutate("verify")} aria-label={`Re-verify ${row.displayName} connection`}>Re-verify</Button>
      <Button variant="destructive" size="sm" disabled={!ready || busy || fields.length === 0} onClick={() => setEditor(previous => ({ ...previous, confirmClear: true }))} aria-label={`Clear credentials for ${row.displayName}`}>Clear credentials</Button>
    </div>
    {editor.editing && ready ? <div className="grid gap-3 md:grid-cols-2">
      {fields.map(field => <label key={field.name} className="grid gap-1 text-xs font-medium text-muted-foreground">{field.label}
        <Input type={field.inputKind === "Text" ? "text" : field.inputKind === "Url" ? "url" : "password"} autoComplete="off" disabled={busy}
          aria-label={`${row.displayName} ${field.label}`} value={editor.values[field.name] ?? ""}
          onChange={event => setEditor(previous => ({ ...previous, values: { ...previous.values, [field.name]: event.target.value }, message: "" }))} />
      </label>)}
      {isLive ? <Checkbox checked={editor.acknowledged} onCheckedChange={value => setEditor(previous => ({ ...previous, acknowledged: value }))} disabled={busy} label="I understand this save changes the selected live connection credentials." /> : null}
    </div> : null}
    <Dialog open={editor.confirmClear && ready} onOpenChange={open => setEditor(previous => ({ ...previous, confirmClear: open }))}><DialogContent aria-label={`Clear ${row.displayName} credentials?`}>
      <p>Remove credentials for {selected?.displayName} ({selected?.externalAccountId}, {selected?.credentialEnvironment})?</p>
      <Button variant="destructive" onClick={() => void mutate("clear")} aria-label={`Confirm clear for ${row.displayName}`}>Confirm clear</Button>
      <Button variant="outline" onClick={() => setEditor(previous => ({ ...previous, confirmClear: false }))}>Cancel</Button>
    </DialogContent></Dialog>
    <div role="status" aria-live="polite" className="text-xs text-muted-foreground">{editor.busy ? "Loading selected connection…" : editor.message}</div>
    <div className="text-xs text-muted-foreground" aria-label={`${row.displayName} readiness checks`}>Readiness checks. Provider overview: <span>Verification freshness: {row.freshnessStatus === "delayed" ? "Delayed" : row.freshnessStatus === "stale" ? "Stale" : row.freshnessStatus === "current" ? "Current" : "Not reported"}</span>. Selected connection: {editor.status ? `${editor.status.credentialState} · ${editor.status.verificationState}` : "Select a current owned connection; provider-wide readiness does not authorize credential changes."}</div>
    <p className="text-xs text-muted-foreground">Impact summary: {row.affectedWorkflowsLabel}. Changes apply only to the selected retained connection.</p>
  </section>;
}
