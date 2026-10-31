import { useCallback, useEffect, useRef, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { StatusBanner } from "@/components/ui/status-banner";
import { isAbortError, isApiError } from "@/lib/api-errors";
import { enableBondCalculationCutover, getBondCutoverReadiness } from "@/lib/api/bond-cutover.api";
import type {
  BondMigrationBlock,
  BondMigrationCutoverReceipt,
  BondMigrationInventory,
  BondMigrationInventoryRow
} from "@/types/bond-cutover.types";

const shortHash = (hash: string) => (hash.length > 12 ? `${hash.slice(0, 12)}…` : hash);

interface Notice {
  title: string;
  detail: string;
}

/**
 * Operator half of the bond calculation cutover procedure (bond-capability-expansion steps 5–6): read
 * the complete active inventory, name a reviewed block for every bond that still lacks supported
 * declared conventions, and submit the fresh inventory hash. Nothing is read until asked because the
 * inventory is a full store read; the server binds blocks to retained version and content hash and
 * records the authenticated actor as reviewer. Amending an enabled cutover echoes the current receipt
 * hash, so a receipt that changed underneath the operator is refused rather than overwritten.
 */
export function BondCutoverConsolePanel() {
  const [inventory, setInventory] = useState<BondMigrationInventory | null>(null);
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [reviewed, setReviewed] = useState(false);
  const [receipt, setReceipt] = useState<BondMigrationCutoverReceipt | null>(null);
  const [busy, setBusy] = useState<"load" | "submit" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const inFlight = useRef<AbortController | null>(null);

  useEffect(() => () => inFlight.current?.abort(), []);

  const refuse = useCallback((reason: unknown, fallback: string) => {
    if (isApiError(reason) && reason.status === 403) {
      setNotice({
        title: "Requires maintenance authority",
        detail: "Bond calculation cutover governs every tenant's live calculations, so it is limited to operators with AdminMaintenance."
      });
    } else if (isApiError(reason) && reason.status === 409) {
      setNotice({
        title: "Inventory or receipt changed",
        detail: `${reason.message || "The active bond inventory or the retained receipt changed after review."} Reload the inventory and review again.`
      });
    } else if (isApiError(reason) && reason.status === 429) {
      setNotice({ title: "Rate limited", detail: "The workstation's shared request budget is exhausted. Retry in a moment." });
    } else {
      setError(reason instanceof Error && reason.message ? reason.message : fallback);
    }
  }, []);

  const load = useCallback(async () => {
    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;
    setBusy("load");
    setError(null);
    setNotice(null);
    try {
      const next = await getBondCutoverReadiness({ signal: controller.signal });
      setInventory(next);
      // Reasons belong to one inventory snapshot; a changed hash restarts the review.
      setReasons((prev) => (inventory?.inventorySha256 === next.inventorySha256 ? prev : {}));
      setReviewed(false);
    } catch (reason) {
      if (isAbortError(reason) || controller.signal.aborted) return;
      setInventory(null);
      refuse(reason, "Bond cutover inventory is unavailable.");
    } finally {
      if (inFlight.current === controller) setBusy(null);
    }
  }, [inventory?.inventorySha256, refuse]);

  const undeclared = inventory?.bonds.filter((row) => !row.hasDeclaredConventions) ?? [];
  const blocks: BondMigrationBlock[] = (inventory?.bonds ?? [])
    .filter((row) => reasons[row.securityId]?.trim())
    .map((row) => ({ securityId: row.securityId, version: row.version, securityContentSha256: row.securityContentSha256, reason: reasons[row.securityId].trim() }));
  const missing = undeclared.filter((row) => !reasons[row.securityId]?.trim());
  const canSubmit = inventory !== null && busy === null && reviewed && missing.length === 0;

  const submit = useCallback(async () => {
    if (!inventory) return;
    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;
    setBusy("submit");
    setError(null);
    setNotice(null);
    try {
      const next = await enableBondCalculationCutover(
        {
          expectedInventorySha256: inventory.inventorySha256,
          // Replaced server-side with the authenticated actor; never trusted from the body.
          reviewer: "workstation",
          explicitBlocks: blocks,
          expectedPreviousReceiptSha256: inventory.currentReceiptSha256 ?? null
        },
        { signal: controller.signal }
      );
      setReceipt(next);
      setReviewed(false);
      const refreshed = await getBondCutoverReadiness({ signal: controller.signal });
      setInventory(refreshed);
    } catch (reason) {
      if (isAbortError(reason) || controller.signal.aborted) return;
      refuse(reason, "Bond calculation cutover was refused.");
    } finally {
      if (inFlight.current === controller) setBusy(null);
    }
  }, [blocks, inventory, refuse]);

  const columns: DenseDataTableColumn<BondMigrationInventoryRow>[] = [
    { id: "bond", label: "Bond", render: (row) => <span className="font-medium text-foreground">{row.displayName}</span> },
    { id: "version", label: "Version", align: "right", render: (row) => row.version },
    { id: "hash", label: "Content hash", render: (row) => <span className="font-mono" title={row.securityContentSha256}>{shortHash(row.securityContentSha256)}</span> },
    {
      id: "declared",
      label: "Declared conventions",
      render: (row) => row.hasDeclaredConventions
        ? <span>Declared</span>
        : <span className="text-amber-700 dark:text-amber-300">{row.blockedReason ?? "Undeclared"}</span>
    },
    {
      id: "block",
      label: "Reviewed block reason",
      render: (row) => (
        <Input
          value={reasons[row.securityId] ?? ""}
          disabled={busy !== null}
          placeholder={row.hasDeclaredConventions ? "Optional explicit block" : "Required before cutover"}
          aria-label={`Block reason for ${row.displayName}`}
          onChange={(event) => setReasons((prev) => ({ ...prev, [row.securityId]: event.target.value }))}
        />
      )
    }
  ];

  return (
    <section className="rounded-lg border border-border/70 bg-background/40 p-4" aria-label="Bond calculation cutover console">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="grid gap-1">
          <h3 className="text-sm font-semibold text-foreground">Bond calculation cutover</h3>
          <p className="text-xs text-muted-foreground">
            Reads the complete active-bond inventory, requires a reviewed block for every bond without supported declared
            conventions, and submits the fresh inventory hash to enable the durable live-calculation gate. Amendments
            name the current receipt hash. Historical receipts and posted journals are never rewritten.
          </p>
        </div>
        <Button size="sm" variant="outline" onClick={() => void load()} disabled={busy !== null} aria-busy={busy === "load"}
          aria-label="Load the complete active bond inventory">
          {busy === "load" ? "Loading…" : inventory ? "Reload inventory" : "Load inventory"}
        </Button>
      </div>

      {error ? <StatusBanner role="alert" tone="danger" title="Bond cutover request failed" detail={error} /> : null}
      {notice ? <StatusBanner role="status" tone="info" title={notice.title} detail={notice.detail} /> : null}

      {inventory ? (
        <div className="mt-3 grid gap-3 text-xs">
          <dl className="grid grid-cols-2 gap-2 md:grid-cols-4" aria-label="Bond cutover state">
            <div><dt className="text-muted-foreground">Inventory hash</dt><dd className="break-all font-mono" title={inventory.inventorySha256}>{shortHash(inventory.inventorySha256)}</dd></div>
            <div><dt className="text-muted-foreground">Active bonds</dt><dd>{inventory.bonds.length} ({undeclared.length} undeclared)</dd></div>
            <div><dt className="text-muted-foreground">Live gate</dt><dd>{inventory.cutoverEnabled ? "Cutover enabled" : inventory.requiresLegacyCalculations ? "Legacy calculations still required" : "Not enabled; every bond declares conventions"}</dd></div>
            <div><dt className="text-muted-foreground">Current receipt</dt><dd className="break-all font-mono" title={inventory.currentReceiptSha256 ?? undefined}>{inventory.currentReceiptSha256 ? shortHash(inventory.currentReceiptSha256) : "None"}</dd></div>
          </dl>

          <DenseDataTable rows={inventory.bonds} getRowId={(row) => `${row.securityId}:${row.version}`} columns={columns}
            ariaLabel="Active bond cutover inventory" caption={null} emptyText="No active bond carries a coupon structure." />

          <div className="flex flex-wrap items-center gap-3">
            <Checkbox checked={reviewed} disabled={busy !== null} onCheckedChange={setReviewed}
              label={`I reviewed this complete inventory (${inventory.inventorySha256.slice(0, 12)}) and every block reason`} />
            <Button size="sm" disabled={!canSubmit} aria-busy={busy === "submit"} onClick={() => void submit()}
              aria-label={inventory.cutoverEnabled ? "Amend bond calculation cutover" : "Enable bond calculation cutover"}>
              {busy === "submit" ? "Submitting…" : inventory.cutoverEnabled ? "Amend cutover" : "Enable cutover"}
            </Button>
            <span className="text-muted-foreground" role="status" aria-live="polite">
              {missing.length > 0
                ? `${missing.length} undeclared bond${missing.length === 1 ? "" : "s"} still need${missing.length === 1 ? "s" : ""} a reviewed block reason.`
                : reviewed
                  ? `Ready to submit ${blocks.length} explicit block${blocks.length === 1 ? "" : "s"} against inventory ${shortHash(inventory.inventorySha256)}.`
                  : "Confirm the review to enable submission."}
            </span>
          </div>
        </div>
      ) : null}

      {receipt ? (
        <p className="mt-3 break-all font-mono text-xs" aria-label="Bond cutover receipt">
          Receipt recorded {receipt.recordedAt} by {receipt.reviewer} · inventory {receipt.inventorySha256} · {receipt.explicitBlocks.length} explicit block{receipt.explicitBlocks.length === 1 ? "" : "s"} · {receipt.modelVersion}
          {receipt.previousReceiptSha256 ? ` · amends ${receipt.previousReceiptSha256}` : " · first cutover"}
        </p>
      ) : null}
    </section>
  );
}
