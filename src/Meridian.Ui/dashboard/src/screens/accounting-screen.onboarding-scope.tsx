import { useEffect, useState } from "react";
import { apiGetJson } from "@/lib/api";
import { UI_API_ROUTES } from "@/lib/ui-api-routes.generated";
import type { LedgerBook } from "@/types";
import { FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Button } from "@/components/ui/button";

interface ScopeEntity { entityId: string; name: string; isActive: boolean }
interface ScopeAccount {
  accountId: string; displayName: string; entityId: string | null; fundId: string | null;
  sleeveId: string | null; vehicleId: string | null; baseCurrency: string; isActive: boolean;
}
interface ScopeGraph { entities: ScopeEntity[]; accounts: ScopeAccount[] }
export function accountBelongsToBook(account: ScopeAccount, book: LedgerBook) {
  const key = ({ Account: "accountId", Entity: "entityId", Fund: "fundId", Sleeve: "sleeveId", Vehicle: "vehicleId" } as const)[book.fundStructureNodeKind as "Account"];
  return Boolean(key && account[key]?.toLowerCase() === book.fundStructureNodeId.toLowerCase());
}

/** Discovery is read-only and authoritative; manual identities never imply access. */
export function OnboardingScopeFields() {
  const [catalog, setCatalog] = useState<{ graph: ScopeGraph; books: LedgerBook[] } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [attempt, setAttempt] = useState(0);
  const [manual, setManual] = useState(false);
  const [entityId, setEntityId] = useState("");
  const [bookId, setBookId] = useState("");
  const [accountIds, setAccountIds] = useState<string[]>([]);
  useEffect(() => {
    const controller = new AbortController();
    setCatalog(null); setError(null); setEntityId(""); setBookId(""); setAccountIds([]);
    const options = { signal: controller.signal, allowDevelopmentFallback: false };
    Promise.all([
      apiGetJson<ScopeGraph>("/api/fund-structure/graph?activeOnly=true", options),
      apiGetJson<LedgerBook[]>(UI_API_ROUTES.LedgerBooks, options)
    ]).then(([graph, books]) => {
      if (controller.signal.aborted) return;
      if (!Array.isArray(graph.entities) || !Array.isArray(graph.accounts) || !Array.isArray(books)) throw new Error("Scope discovery returned an unsupported response.");
      setCatalog({ graph, books });
    }).catch(reason => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "Scope discovery unavailable."); });
    return () => controller.abort();
  }, [attempt]);
  const entities = catalog?.graph.entities.filter(entity => entity.isActive) ?? [];
  const accounts = catalog?.graph.accounts.filter(account => account.isActive && account.entityId === entityId) ?? [];
  const books = catalog?.books.filter(book => accounts.some(account => accountBelongsToBook(account, book))) ?? [];
  const book = books.find(item => item.ledgerBookId === bookId);
  const eligibleAccounts = book ? accounts.filter(account => accountBelongsToBook(account, book)) : [];
  return <fieldset className="col-span-full space-y-3" aria-label="Onboarding operating scope">
    <legend className="text-sm font-semibold">Entity, book and account population</legend>
    {error ? <div role="status" className="text-sm"><p>Authorized scope discovery unavailable: {error}</p><Button type="button" variant="outline" size="sm" onClick={() => setAttempt(value => value + 1)}>Retry scope discovery</Button></div> : !catalog ? <p role="status">Loading authorized scope choices…</p> : null}
    <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={manual} onChange={event => setManual(event.target.checked)} />Advanced: enter exact scope identities</label>
    {manual ? <div className="grid gap-3 md:grid-cols-2">
      {[["entityId", "Entity ID"], ["fundProfileId", "Book profile ID"], ["ledgerBookId", "Ledger book ID"], ["accountIds", "Financial account IDs"]].map(([name, label]) => <FormRow key={name} label={label} labelFor={`onboarding-${name}`}><Input id={`onboarding-${name}`} name={name} required /></FormRow>)}
      <p className="col-span-full text-xs text-muted-foreground">Account IDs are comma-separated. The server verifies ownership and membership; entering an identity grants no authority.</p>
    </div> : <>
      <div className="grid gap-3 md:grid-cols-2">
        <FormRow label="Entity" labelFor="onboarding-entity"><Select id="onboarding-entity" name="entityId" value={entityId} required disabled={!catalog} onChange={event => { setEntityId(event.target.value); setBookId(""); setAccountIds([]); }}><option value="">Select an entity</option>{entities.map(entity => <option key={entity.entityId} value={entity.entityId}>{entity.name}</option>)}</Select></FormRow>
        <FormRow label="Ledger book" labelFor="onboarding-book"><Select id="onboarding-book" name="ledgerBookId" value={bookId} required disabled={!entityId} onChange={event => { setBookId(event.target.value); setAccountIds([]); }}><option value="">Select a compatible book</option>{books.map(item => <option key={item.ledgerBookId} value={item.ledgerBookId}>{item.displayName} · {item.baseCurrency} · {item.fundProfileId}</option>)}</Select></FormRow>
      </div>
      <input type="hidden" name="fundProfileId" value={book?.fundProfileId ?? ""} />
      <input type="hidden" name="accountIds" value={accountIds.join(",")} />
      <fieldset className="space-y-2"><legend className="text-sm font-medium">Included financial accounts</legend>{eligibleAccounts.map(account => <label key={account.accountId} className="flex items-center gap-2 text-sm"><input type="checkbox" checked={accountIds.includes(account.accountId)} onChange={event => setAccountIds(current => event.target.checked ? [...current, account.accountId] : current.filter(id => id !== account.accountId))} />{account.displayName} · {account.baseCurrency}</label>)}{!eligibleAccounts.length ? <p className="text-xs text-muted-foreground">Select an entity and book to discover compatible accounts.</p> : null}</fieldset>
      <p className="text-xs text-muted-foreground">{accountIds.length} accounts selected. Scope is fixed after creation. Review entity, book, accounts and dates before creating.</p>
    </>}
  </fieldset>;
}
