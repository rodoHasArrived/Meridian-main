import { createContext, useCallback, useContext, useEffect, useMemo, useState, type MouseEvent, type ReactNode } from "react";
import { Link, useInRouterContext, useLocation } from "react-router-dom";
import { Drawer, DrawerBody, DrawerFooter } from "@/components/ui/drawer";
import { ProofDrawer } from "@/components/meridian/proof-drawer";
import { getFinancialRecordExplorerRecord } from "@/lib/api";
import type { FinancialRecordExplorerDto, FinancialRecordExplorerId, FinancialRecordExplorerSelectedRecordDto } from "@/types";

export type EntityKind = "record" | "symbol" | "security" | "account" | "run" | "journal" | "entity";

/** Explicit identity and facts from an already-authorized, loaded source. Display labels are never IDs. */
export interface EntityInspectorRecord {
  kind: EntityKind;
  id: string;
  label: string;
  workspaceHref: string;
  summary: Array<{ label: string; value: string }>;
  activity?: Array<{ id: string; label: string; occurredAt: string }>;
  evidence?: Array<{ evidenceId: string; label: string; href: string }>;
  /** Exact explorer record binding; never derive this from a symbol, title, slug or URL. */
  proof?: { explorer: FinancialRecordExplorerDto; recordId: string };
}

interface Selection {
  record: EntityInspectorRecord;
  contextKey: string;
  sessionIdentity: unknown;
}
const EntityInspectorContext = createContext<((record: EntityInspectorRecord) => void) | null>(null);
const explorerIds: readonly string[] = ["ledger", "portfolio", "security-instrument", "report-line-provenance"];

function isWorkspaceHref(href: string) {
  return href.startsWith("/") && !href.startsWith("//") && !href.startsWith("/api/") && !Array.from(href).some((character) => character === "\\" || character.charCodeAt(0) < 32);
}

/** One inspector per application. Session identity invalidates state, and is never an authorization credential. */
export function EntityInspectorProvider({ children, scopeKey, sessionIdentity, refreshSequence = 0 }: {
  children: ReactNode;
  scopeKey: string;
  sessionIdentity: unknown;
  refreshSequence?: string | number;
}) {
  const location = useLocation();
  const contextKey = JSON.stringify([location.pathname, location.search, location.hash, scopeKey, refreshSequence]);
  const [selection, setSelection] = useState<Selection | null>(null);
  // This comparison withholds old details in the same render as a route, scope, session or refresh change.
  const current = selection?.contextKey === contextKey && selection.sessionIdentity === sessionIdentity ? selection : null;
  useEffect(() => {
    setSelection((value) => value && (value.contextKey !== contextKey || value.sessionIdentity !== sessionIdentity) ? null : value);
  }, [contextKey, sessionIdentity]);
  const open = useCallback((record: EntityInspectorRecord) => {
    if (!record.id.trim() || !isWorkspaceHref(record.workspaceHref)) return;
    setSelection({ record, contextKey, sessionIdentity });
  }, [contextKey, sessionIdentity]);
  const close = useCallback(() => setSelection(null), []);
  const record = current?.record;
  return <EntityInspectorContext.Provider value={sessionIdentity ? open : null}>
    {children}
    <Drawer open={Boolean(record)} onClose={close} title={record ? `${record.label} details` : "Entity details"} className="max-w-[30rem]">
      {record ? <>
        <DrawerBody>
          <p className="text-xs text-muted-foreground">{record.kind} · Loaded workspace snapshot</p>
          <section aria-label="Entity summary" className="space-y-2">
            <h3 className="font-semibold">Summary</h3>
            <dl className="divide-y divide-border">
              {record.summary.map((field, index) => <div key={index} className="grid grid-cols-2 gap-3 py-2">
                <dt className="text-muted-foreground">{field.label}</dt><dd className="text-right tabular-nums">{field.value}</dd>
              </div>)}
            </dl>
          </section>
          <section aria-label="Entity recent activity" className="space-y-2">
            <h3 className="font-semibold">Recent activity</h3>
            {record.activity === undefined ? <p className="text-muted-foreground">This source has not supplied activity history.</p>
              : record.activity.length === 0 ? <p>No activity was returned by this source.</p>
                : <ol className="space-y-2">{record.activity.map((item) => <li key={item.id}>{item.label} <time className="block text-xs text-muted-foreground" dateTime={item.occurredAt}>{item.occurredAt}</time></li>)}</ol>}
          </section>
          <section aria-label="Entity linked evidence" className="space-y-2">
            <h3 className="font-semibold">Linked evidence</h3>
            {record.evidence?.length ? <ul className="space-y-2">{record.evidence.map((item) => <li key={item.evidenceId}>
              {isWorkspaceHref(item.href) ? <Link className="underline decoration-dotted underline-offset-4" to={item.href}>{item.label}</Link> : <span>{item.label} · Evidence route unavailable</span>}
            </li>)}</ul> : <p className="text-muted-foreground">No linked evidence was supplied. This snapshot does not establish provenance completeness.</p>}
            {record.proof ? <EntityExplorerProof key={`${contextKey}:${record.kind}:${record.id}`} binding={record.proof} /> : null}
          </section>
          <details>
            <summary className="cursor-pointer text-xs text-muted-foreground">Technical reference</summary>
            <p className="mt-2 break-all font-mono text-xs">{record.kind}: {record.id}</p>
          </details>
        </DrawerBody>
        <DrawerFooter><Link className="text-sm font-semibold underline underline-offset-4" to={record.workspaceHref}>Open in workspace</Link></DrawerFooter>
      </> : null}
    </Drawer>
  </EntityInspectorContext.Provider>;
}

/** Uses ordinary link navigation outside the application provider and for modified clicks. */
export function EntityLink({ record, children, className, ariaLabel }: { record: EntityInspectorRecord; children?: ReactNode; className?: string; ariaLabel?: string }) {
  const open = useContext(EntityInspectorContext);
  const inRouter = useInRouterContext();
  const onClick = (event: MouseEvent<HTMLAnchorElement>) => {
    if (!open || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    event.stopPropagation();
    open(record);
  };
  if (!record.id.trim() || !isWorkspaceHref(record.workspaceHref)) return <span className={className}>{children ?? record.label}</span>;
  const linkClass = className ?? "font-medium text-foreground underline decoration-dotted underline-offset-4 hover:decoration-solid focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2";
  if (!inRouter) return <a href={record.workspaceHref} className={linkClass} aria-label={ariaLabel ?? `Inspect ${record.label}`}>{children ?? record.label}</a>;
  return <Link to={record.workspaceHref} onClick={onClick} className={className ?? "font-medium text-foreground underline decoration-dotted underline-offset-4 hover:decoration-solid focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"} aria-label={ariaLabel ?? `Inspect ${record.label}`}>
    {children ?? record.label}
  </Link>;
}

function EntityExplorerProof({ binding }: { binding: NonNullable<EntityInspectorRecord["proof"]> }) {
  const explorerId = binding.explorer.explorerId;
  const recordId = binding.recordId;
  const key = JSON.stringify([explorerId, recordId]);
  const [result, setResult] = useState<{ key: string; record: FinancialRecordExplorerSelectedRecordDto | null; error: string | null } | null>(null);
  const authorizedBinding = useMemo(() => explorerIds.includes(explorerId) && binding.explorer.rows.some((row) => row.recordId === recordId), [binding.explorer, explorerId, recordId]);
  useEffect(() => {
    if (!authorizedBinding) return;
    const controller = new AbortController();
    let active = true;
    void getFinancialRecordExplorerRecord(explorerId as FinancialRecordExplorerId, recordId, { signal: controller.signal, allowDevelopmentFallback: false }).then((record) => {
      if (!active) return;
      const valid = record.recordId === recordId && Array.isArray(record.fields) && Array.isArray(record.proofActions)
        && Array.isArray(record.usedIn) && Array.isArray(record.impacts);
      setResult({ key, record: valid ? record : null, error: valid ? null : "Evidence response does not match this exact record." });
    }).catch(() => {
      if (active) setResult({ key, record: null, error: "Authoritative record evidence is unavailable. Open its workspace to investigate." });
    });
    return () => { active = false; controller.abort(); };
  }, [authorizedBinding, explorerId, recordId, key]);
  if (!authorizedBinding) return <p role="alert">Evidence binding is missing from the authorized source. No proof was requested.</p>;
  if (!result || result.key !== key) return <p role="status">Loading authoritative record evidence…</p>;
  if (result.error || !result.record) return <p role="alert">{result.error}</p>;
  return <ProofDrawer explorer={binding.explorer} record={result.record} blockedReason={binding.explorer.blockedReason} />;
}
