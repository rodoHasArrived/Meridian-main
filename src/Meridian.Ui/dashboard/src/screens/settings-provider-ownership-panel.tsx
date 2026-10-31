import { type FormEvent, useEffect, useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError } from "@/lib/api-errors";
import {
  bindProviderRuntimeCredential,
  createOwnedProviderConnection,
  migrateProviderCredential,
  previewProviderCredentialMigration,
  rollbackProviderCredential
} from "@/lib/api/provider-ownership.api";
import type { ProviderRoutingConnection } from "@/types";
import type { ProviderCredentialMigrationReceipt, ProviderCredentialStoreStatus } from "@/types/provider-ownership.types";

export interface ProviderOwnershipPanelProps {
  providerId: string;
  displayName: string;
  /** The owned connection selected in the credential editor; null until the operator selects one. */
  connection: ProviderRoutingConnection | null;
  onChanged?: () => void | Promise<void>;
  api?: {
    create?: typeof createOwnedProviderConnection;
    bind?: typeof bindProviderRuntimeCredential;
    preview?: typeof previewProviderCredentialMigration;
    migrate?: typeof migrateProviderCredential;
    rollback?: typeof rollbackProviderCredential;
  };
}

const newMigrationId = () =>
  typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : `migration-${Date.now().toString(36)}`;

/**
 * Ownership administration over the shared provider-routing commands. Tenant and actor are resolved by
 * the server; the panel never sends a secret. A migration keeps its client-generated identity so a
 * retry or a rollback addresses the same immutable receipt.
 */
export function ProviderOwnershipPanel({ providerId, displayName, connection, onChanged, api = {} }: ProviderOwnershipPanelProps) {
  const id = useId();
  const create = api.create ?? createOwnedProviderConnection;
  const bind = api.bind ?? bindProviderRuntimeCredential;
  const preview = api.preview ?? previewProviderCredentialMigration;
  const migrate = api.migrate ?? migrateProviderCredential;
  const rollback = api.rollback ?? rollbackProviderCredential;

  const [newConnection, setNewConnection] = useState({ connectionId: "", displayName: "", externalAccountId: "", environment: "paper" });
  const [status, setStatus] = useState<ProviderCredentialStoreStatus | null>(null);
  const [migrationId, setMigrationId] = useState<string>(() => newMigrationId());
  const [receipt, setReceipt] = useState<ProviderCredentialMigrationReceipt | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [message, setMessage] = useState<string>("Select an owned connection above to preview, migrate, roll back or bind its credentials.");
  const [error, setError] = useState<string | null>(null);
  const selected = connection;
  const selectedId = connection?.connectionId ?? "";
  const generation = status?.auditMetadata?.credentialGeneration ?? null;

  // A preview, receipt or refusal belongs to one connection; changing the selection discards them.
  useEffect(() => {
    setStatus(null);
    setReceipt(null);
    setError(null);
    setMessage(selectedId ? "Preview the retained vault status before migrating." : "Select an owned connection above.");
  }, [selectedId]);

  async function run(action: string, work: () => Promise<string>) {
    if (busy) return;
    setBusy(action);
    setError(null);
    try {
      const outcome = await work();
      setMessage(outcome);
      await onChanged?.();
    } catch (failure) {
      setError(describeApiError(failure, `${displayName} ownership command was not confirmed. Reload owned connections before retrying.`).summary);
    } finally {
      setBusy(null);
    }
  }

  async function submitCreate(event: FormEvent) {
    event.preventDefault();
    const { connectionId: newId, displayName: label, externalAccountId, environment } = newConnection;
    if (!newId.trim() || !label.trim() || !externalAccountId.trim() || !environment.trim()) {
      setError("Enter the connection id, display name, the provider's actual account identity and the credential environment.");
      return;
    }
    await run("create", async () => {
      const created = await create({
        connection: { connectionId: newId.trim(), providerFamilyId: providerId, displayName: label.trim(), externalAccountId: externalAccountId.trim() },
        environment: environment.trim()
      });
      setNewConnection({ connectionId: "", displayName: "", externalAccountId: "", environment: environment.trim() });
      return `Owned connection ${created.connectionId} created for the authenticated tenant. Reload connections to select it.`;
    });
  }

  return (
    <details className="rounded-md border border-border/70 bg-secondary/10 px-3 py-2" aria-label={`${displayName} ownership administration`}>
      <summary className="cursor-pointer text-xs font-semibold">Ownership administration</summary>
      <div className="mt-2 grid gap-3 text-xs">
        <p className="text-muted-foreground">
          Requires credential, provider and configuration administration. The server resolves the tenant and the acting
          operator; nothing here reads or sends a secret. Binding or migrating does not reach running provider instances
          until they are deliberately restarted.
        </p>

        <form onSubmit={submitCreate} aria-label={`Create owned connection for ${displayName}`} className="grid gap-2 md:grid-cols-4">
          <label className="grid gap-1 font-medium text-muted-foreground">Connection id
            <Input id={`${id}-new-id`} value={newConnection.connectionId} disabled={busy !== null} aria-label={`${displayName} new connection id`}
              onChange={(e) => setNewConnection((prev) => ({ ...prev, connectionId: e.target.value }))} />
          </label>
          <label className="grid gap-1 font-medium text-muted-foreground">Display name
            <Input value={newConnection.displayName} disabled={busy !== null} aria-label={`${displayName} new connection display name`}
              onChange={(e) => setNewConnection((prev) => ({ ...prev, displayName: e.target.value }))} />
          </label>
          <label className="grid gap-1 font-medium text-muted-foreground">Provider account identity
            <Input value={newConnection.externalAccountId} disabled={busy !== null} aria-label={`${displayName} new connection account`}
              onChange={(e) => setNewConnection((prev) => ({ ...prev, externalAccountId: e.target.value }))} />
          </label>
          <label className="grid gap-1 font-medium text-muted-foreground">Credential environment
            <Input value={newConnection.environment} disabled={busy !== null} aria-label={`${displayName} new connection environment`}
              onChange={(e) => setNewConnection((prev) => ({ ...prev, environment: e.target.value }))} />
          </label>
          <Button type="submit" size="sm" variant="outline" className="w-fit" disabled={busy !== null} aria-busy={busy === "create"}>
            {busy === "create" ? "Creating…" : "Create owned connection"}
          </Button>
        </form>

        <p className="font-medium text-muted-foreground" aria-label={`${displayName} ownership connection`}>
          Owned connection: {selected ? `${selected.displayName} · ${selected.externalAccountId} · ${selected.credentialEnvironment}` : "none selected"}
        </p>

        <div className="flex flex-wrap gap-2">
          <Button size="sm" variant="outline" disabled={!selected || busy !== null} aria-busy={busy === "preview"}
            onClick={() => void run("preview", async () => {
              const next = await preview(selected!.connectionId, providerId);
              if (next.providerId.toLowerCase() !== providerId.toLowerCase()) throw new Error("The preview names another provider.");
              setStatus(next);
              setReceipt(null);
              return next.auditMetadata?.credentialGeneration
                ? `Retained vault record found (${next.credentialState}, ${next.credentialSource}); generation ${next.auditMetadata.credentialGeneration} can be migrated.`
                : `No retained provider-wide record carries a credential generation; ${next.credentialState} / ${next.credentialSource}. Nothing to migrate.`;
            })}>
            {busy === "preview" ? "Previewing…" : "Preview migration"}
          </Button>
          <Button size="sm" disabled={!selected || !generation || busy !== null} aria-busy={busy === "migrate"}
            onClick={() => void run("migrate", async () => {
              const next = await migrate(selected!.connectionId, { providerId, migrationId, expectedGeneration: generation });
              if (next.scope.connectionId.toLowerCase() !== selected!.connectionId.toLowerCase()) throw new Error("The receipt names another connection.");
              setReceipt(next);
              return `Migration ${next.migrationId} retained by ${next.actor} at generation ${next.credentialGeneration}. The record is unverified under its new ownership; re-verify before use.`;
            })}>
            {busy === "migrate" ? "Migrating…" : "Migrate to this connection"}
          </Button>
          <Button size="sm" variant="destructive" disabled={!selected || !receipt || receipt.rolledBack || busy !== null} aria-busy={busy === "rollback"}
            onClick={() => void run("rollback", async () => {
              const next = await rollback(selected!.connectionId, { providerId, migrationId: receipt!.migrationId });
              setReceipt(next);
              setMigrationId(newMigrationId());
              return `Migration ${next.migrationId} rolled back by ${next.rolledBackBy ?? "the server"}; the provider-wide record is restored and unverified.`;
            })}>
            {busy === "rollback" ? "Rolling back…" : "Roll back migration"}
          </Button>
          <Button size="sm" variant="outline" disabled={!selected || busy !== null} aria-busy={busy === "bind"}
            onClick={() => void run("bind", async () => {
              const next = await bind(selected!.connectionId, { providerId });
              if (next.connectionId.toLowerCase() !== selected!.connectionId.toLowerCase()) throw new Error("The binding names another connection.");
              return `Host runtime credentials for ${next.providerId} now select ${next.connectionId} in tenant ${next.tenantId}. Running instances keep their current credentials until restarted.`;
            })}>
            {busy === "bind" ? "Binding…" : "Bind as host runtime credential"}
          </Button>
        </div>

        {status ? (
          <dl className="grid grid-cols-2 gap-2 md:grid-cols-4" aria-label={`${displayName} migration preview`}>
            <div><dt className="text-muted-foreground">Credential state</dt><dd>{status.credentialState}</dd></div>
            <div><dt className="text-muted-foreground">Source</dt><dd>{status.credentialSource}</dd></div>
            <div><dt className="text-muted-foreground">Environment</dt><dd>{status.environment ?? "Unspecified"}</dd></div>
            <div><dt className="text-muted-foreground">Generation</dt><dd className="break-all font-mono">{generation ?? "None"}</dd></div>
          </dl>
        ) : null}
        {receipt ? (
          <p className="break-all font-mono" aria-label={`${displayName} migration receipt`}>
            Receipt {receipt.migrationId} · {receipt.rolledBack ? "rolled back" : "migrated"} · generation {receipt.credentialGeneration} · {receipt.scope.connectionId}
          </p>
        ) : null}
        {error ? <StatusBanner tone="danger" role="alert" title="Ownership command refused" detail={error} /> : null}
        <p aria-live="polite" aria-label={`${displayName} ownership status`} className="text-muted-foreground">{busy ? "Working…" : message}</p>
      </div>
    </details>
  );
}
