import { useState } from "react";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Tabs, TabPanel } from "@/components/ui/tabs";
import type { LedgerBook } from "@/types";
import type { EntityOperationCapabilities, EntityOperationGraph, EntityOperationScope } from "@/types/entity-operations";
import { entityOperationEndpoints as endpoints } from "@/lib/api/entity-operations.api";
import { EntityAccountingArrangements } from "./accounting-screen.entity-agreements";
import { EntityLifecycleTimeline } from "./accounting-screen.entity-timeline";
import { EntityBookControls } from "./accounting-screen.entity-book-controls";
import { EntityCounterpartProposals } from "./accounting-screen.entity-proposals";
import { EntityFxAuthority } from "./accounting-screen.entity-fx";
import { EntityAccountBindings } from "./accounting-screen.entity-account-bindings";
import { EntityObligationMonitor } from "./accounting-screen.entity-obligations";
import { EntityPerimeterTransition } from "./accounting-screen.entity-perimeter-transition";
import { OperationError, useEntityData } from "./accounting-screen.entity-operations.shared";

const tabs = [
  { id: "profiles", label: "Accounting profiles" }, { id: "obligations", label: "Obligation monitor" }, { id: "timeline", label: "Entity timeline" },
  { id: "controls", label: "Book controls" }, { id: "accounts", label: "Account roles" },
  { id: "proposals", label: "Counterpart proposals" }, { id: "fx", label: "FX authority" }, { id: "perimeter", label: "Perimeter cutover" }
];

/** Entity operations remain within Accounting and reuse its explicit selected-book scope. */
export function EntityOperationsPanel({ book }: { book: LedgerBook }) {
  const capabilities = useEntityData<EntityOperationCapabilities>(endpoints.capabilities);
  const graph = useEntityData<EntityOperationGraph>(capabilities.data?.canManageAgreements || capabilities.data?.canManageLifecycle ? endpoints.graph : null);
  const books = useEntityData<LedgerBook[]>(capabilities.data ? endpoints.books : null);
  const [active, setActive] = useState(() => { const requested = new URLSearchParams(window.location.search).get("entityTab"); return tabs.some((tab) => tab.id === requested) ? requested! : "profiles"; });
  const scope: EntityOperationScope | null = capabilities.data ? { book, books: books.data ?? [book], graph: graph.data, capabilities: capabilities.data } : null;
  return <Card id="entity-operations" className="panel-surface min-w-0" aria-labelledby="entity-operations-title"><CardHeader>
    <CardTitle id="entity-operations-title">Entity operations</CardTitle>
    <CardDescription>{book.displayName} · {book.baseCurrency} · accounting profiles, lifecycle, intercompany delivery and governed account/FX authority.</CardDescription>
  </CardHeader><CardContent className="space-y-4">
    <OperationError error={capabilities.error} /><OperationError error={books.error} />
    {graph.error ? <p role="status" className="text-sm text-warning">Entity graph access is unavailable: {graph.error} Book-scoped controls and evidence remain available according to your permission.</p> : null}
    {capabilities.loading ? <p role="status">Loading entity operation permissions…</p> : null}
    {capabilities.error ? <Button type="button" variant="outline" onClick={capabilities.refresh}>Retry operation permissions</Button> : null}
    {scope ? <Tabs value={active} tabs={tabs} onValueChange={setActive} aria-label="Entity operations">
      <TabPanel>{active === "profiles" ? <EntityAccountingArrangements scope={scope} /> : null}</TabPanel>
      <TabPanel>{active === "obligations" ? <EntityObligationMonitor scope={scope} /> : null}</TabPanel>
      <TabPanel>{active === "timeline" ? <EntityLifecycleTimeline key={graph.data ? "graph-ready" : "graph-pending"} scope={scope} /> : null}</TabPanel>
      <TabPanel>{active === "controls" ? <EntityBookControls scope={scope} /> : null}</TabPanel>
      <TabPanel>{active === "accounts" ? <EntityAccountBindings scope={scope} /> : null}</TabPanel>
      <TabPanel>{active === "proposals" ? <EntityCounterpartProposals scope={scope} /> : null}</TabPanel>
      <TabPanel>{active === "fx" ? <EntityFxAuthority scope={scope} /> : null}</TabPanel>
      <TabPanel>{active === "perimeter" ? <EntityPerimeterTransition scope={scope} /> : null}</TabPanel>
    </Tabs> : null}
  </CardContent></Card>;
}
