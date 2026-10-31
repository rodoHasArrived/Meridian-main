import { type FormEvent, useEffect, useRef, useState } from "react";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { StatusBanner } from "@/components/ui/status-banner";
import { describeApiError } from "@/lib/api-errors";
import { getBondIssuerLadderPage, getBondMaturityLadderPage } from "@/lib/api/bond-reference.api";
import type { BondReferencePage, BondReferenceRow } from "@/types/bond-reference.types";

type LadderMode = "issuer" | "maturity";
const PAGE_SIZE = 100;

export interface BondReferenceLadderPanelProps {
  onSelectSecurity?: (securityId: string) => void;
  api?: {
    issuerPage?: typeof getBondIssuerLadderPage;
    maturityPage?: typeof getBondMaturityLadderPage;
  };
}

/**
 * Issuer and maturity ladders over the bounded keyset routes. Rows accumulate page by page behind an
 * explicit "Load more", the opaque cursor is bound to the query that produced it, and changing the query
 * discards the accumulated rows rather than mixing pages from different queries.
 */
export function BondReferenceLadderPanel({ onSelectSecurity, api = {} }: BondReferenceLadderPanelProps) {
  const issuerPage = api.issuerPage ?? getBondIssuerLadderPage;
  const maturityPage = api.maturityPage ?? getBondMaturityLadderPage;
  const [mode, setMode] = useState<LadderMode>("issuer");
  const [issuerName, setIssuerName] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [rows, setRows] = useState<BondReferenceRow[]>([]);
  const [cursor, setCursor] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [busy, setBusy] = useState<"first" | "more" | null>(null);
  const [error, setError] = useState<string | null>(null);
  const inFlight = useRef<AbortController | null>(null);

  useEffect(() => () => inFlight.current?.abort(), []);

  function edit(change: () => void) {
    inFlight.current?.abort();
    setBusy(null);
    setRows([]);
    setCursor(null);
    setLoaded(false);
    setError(null);
    change();
  }

  async function fetchPage(next: string | null, signal: AbortSignal): Promise<BondReferencePage> {
    return mode === "issuer"
      ? issuerPage({ issuerName, pageSize: PAGE_SIZE, cursor: next }, { signal })
      : maturityPage({ from, to, pageSize: PAGE_SIZE, cursor: next }, { signal });
  }

  async function load(kind: "first" | "more") {
    inFlight.current?.abort();
    const controller = new AbortController();
    inFlight.current = controller;
    setBusy(kind);
    setError(null);
    try {
      const page = await fetchPage(kind === "first" ? null : cursor, controller.signal);
      if (controller.signal.aborted) return;
      setRows((prev) => {
        const seen = new Set(kind === "first" ? [] : prev.map((row) => row.securityId));
        const fresh = page.items.filter((row) => !seen.has(row.securityId));
        return kind === "first" ? fresh : [...prev, ...fresh];
      });
      setCursor(page.nextCursor);
      setLoaded(true);
    } catch (failure) {
      if (!controller.signal.aborted) setError(describeApiError(failure, "Bond reference ladder could not be loaded.").summary);
    } finally {
      if (!controller.signal.aborted) setBusy(null);
    }
  }

  function submit(event: FormEvent) {
    event.preventDefault();
    if (mode === "issuer" ? !issuerName.trim() : !from || !to) {
      setError(mode === "issuer" ? "Enter an issuer name." : "Enter the maturity window start and end dates.");
      return;
    }
    void load("first");
  }

  const columns: DenseDataTableColumn<BondReferenceRow>[] = [
    {
      id: "name", label: "Bond",
      render: (row) => onSelectSecurity
        ? <button type="button" className="text-left font-medium text-foreground underline-offset-2 hover:underline" onClick={() => onSelectSecurity(row.securityId)}>{row.displayName}</button>
        : <span className="font-medium text-foreground">{row.displayName}</span>
    },
    { id: "identifier", label: "Identifier", render: (row) => <span className="font-mono">{row.primaryIdentifier}</span> },
    { id: "issuer", label: "Issuer", render: (row) => row.issuerName ?? "Unknown" },
    { id: "currency", label: "Currency", render: (row) => row.currency },
    { id: "maturity", label: "Maturity", render: (row) => row.lifecycle?.maturityDate ?? "Unknown" },
    { id: "callable", label: "Callable", render: (row) => row.lifecycle ? (row.lifecycle.isCallable ? `Yes${row.lifecycle.callDate ? ` (${row.lifecycle.callDate})` : ""}` : "No") : "Unknown" },
    { id: "version", label: "Version", align: "right", render: (row) => row.version }
  ];

  return (
    <section className="grid gap-3 rounded-lg border border-border/70 bg-background/40 p-4 text-xs" aria-label="Bond reference ladders">
      <div>
        <h3 className="text-sm font-semibold text-foreground">Bond reference ladders</h3>
        <p className="text-muted-foreground">Bounded keyset pages by issuer or maturity window; each page continues from the previous cursor and a changed query starts over.</p>
      </div>
      <form onSubmit={submit} aria-label="Bond ladder query" className="flex flex-wrap items-end gap-2">
        <label className="grid gap-1 font-medium text-muted-foreground">Ladder
          <Select value={mode} aria-label="Ladder kind" onChange={(e) => edit(() => setMode(e.target.value as LadderMode))}>
            <option value="issuer">Issuer</option>
            <option value="maturity">Maturity window</option>
          </Select>
        </label>
        {mode === "issuer" ? (
          <label className="grid gap-1 font-medium text-muted-foreground">Issuer name
            <Input value={issuerName} aria-label="Issuer name" onChange={(e) => edit(() => setIssuerName(e.target.value))} className="min-w-[16rem]" />
          </label>
        ) : (
          <>
            <label className="grid gap-1 font-medium text-muted-foreground">Maturing from
              <Input type="date" value={from} aria-label="Maturity window start" onChange={(e) => edit(() => setFrom(e.target.value))} />
            </label>
            <label className="grid gap-1 font-medium text-muted-foreground">Maturing to
              <Input type="date" value={to} aria-label="Maturity window end" onChange={(e) => edit(() => setTo(e.target.value))} />
            </label>
          </>
        )}
        <Button type="submit" size="sm" disabled={busy !== null} aria-busy={busy === "first"}>{busy === "first" ? "Loading…" : "Load ladder"}</Button>
        <Button type="button" size="sm" variant="outline" disabled={!cursor || busy !== null} aria-busy={busy === "more"} onClick={() => void load("more")}>
          {busy === "more" ? "Loading…" : "Load more"}
        </Button>
      </form>
      {error ? <StatusBanner tone="danger" role="alert" title="Ladder request refused" detail={error} /> : null}
      {loaded ? (
        <>
          <DenseDataTable rows={rows} getRowId={(row) => row.securityId} columns={columns} ariaLabel="Bond reference ladder rows" caption={null}
            emptyText="No retained bonds match this ladder query." />
          <p className="text-muted-foreground" role="status" aria-live="polite">
            {rows.length} row{rows.length === 1 ? "" : "s"} loaded{cursor ? "; more pages available." : "; end of ladder."}
          </p>
        </>
      ) : null}
    </section>
  );
}
