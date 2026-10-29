import { useId } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { Search } from "lucide-react";
import { DenseRowDetailPanel } from "@/components/meridian/dense-row-detail-accessibility";
import { DenseDataTable, type DenseDataTableColumn } from "@/components/meridian/ui-kit-primitives";
import { WorkspaceWorkbenchShell } from "@/components/meridian/workspace-workbench-shell";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import "@/styles/workspace-task-workbench.css";

export interface WorkspaceTaskItem {
  id: string;
  title: string;
  description: string;
  href: string;
  actionLabel: string;
  status?: string;
  facts?: { label: string; value: string }[];
}

const columns: DenseDataTableColumn<WorkspaceTaskItem>[] = [
  { id: "task", label: "Work item", render: (item) => <div><strong>{item.title}</strong><p className="workspace-task-description">{item.description}</p></div> },
  { id: "status", label: "Context", render: (item) => <span>{item.status ?? "Available task"}</span> },
  { id: "open", label: "Action", render: (item) => <TaskLink item={item} /> }
];

function TaskLink({ item }: { item: WorkspaceTaskItem }) {
  const location = useLocation();
  const [pathname, targetSearch = ""] = item.href.split("?");
  const params = new URLSearchParams(location.search);
  new URLSearchParams(targetSearch).forEach((value, key) => params.set(key, value));
  return <Button asChild size="sm" variant="outline"><Link to={{ pathname, search: params.toString(), hash: location.hash }} aria-label={`${item.actionLabel}: ${item.title}`}>{item.actionLabel}</Link></Button>;
}

/** Presentation-only browsing: selection is never permission, readiness, or approval. */
export function WorkspaceTaskWorkbench({
  title, description, items, stateKey, emptyText = "No work items are available."
}: {
  title: string;
  description: string;
  items: WorkspaceTaskItem[];
  stateKey: string;
  emptyText?: string;
}) {
  const location = useLocation();
  const navigate = useNavigate();
  const detailId = useId();
  const searchId = useId();
  const params = new URLSearchParams(location.search);
  const selectedId = params.get(stateKey);
  const search = params.get(`${stateKey}Search`) ?? "";
  const selected = selectedId === null ? items[0] : items.find((item) => item.id === selectedId);
  const query = search.trim().toLowerCase();
  const rows = items.filter((item) => `${item.title} ${item.description} ${item.status ?? ""}`.toLowerCase().includes(query));
  const outsideResults = selected && !rows.some((item) => item.id === selected.id);
  const update = (key: string, value: string, replace = false) => {
    const next = new URLSearchParams(location.search);
    if (value) next.set(key, value); else next.delete(key);
    navigate({ pathname: location.pathname, search: next.toString(), hash: location.hash }, { replace });
  };

  return (
    <WorkspaceWorkbenchShell
      label={title}
      switchInspector
      inspectorRole="region"
      inspectorLabel={`${title} inspector`}
      inspector={(
        <DenseRowDetailPanel id={detailId} ariaLabel={selected ? `${selected.title} detail` : "Selection unavailable"} className="workspace-task-inspector">
          <p className="eyebrow-label">Selected work</p>
          {selected ? <>
            <h3>{selected.title}</h3>
            <TaskLink item={selected} />
            <p>{selected.description}</p>
            {outsideResults ? <div role="status"><p>Selected item is outside the current results.</p><Button type="button" size="sm" variant="outline" onClick={() => update(`${stateKey}Search`, "", true)}>Clear search</Button></div> : null}
            <dl>
              {selected.status ? <div><dt>Context</dt><dd>{selected.status}</dd></div> : null}
              {selected.facts?.map((fact) => <div key={fact.label}><dt>{fact.label}</dt><dd>{fact.value}</dd></div>)}
            </dl>
          </> : <div role="status"><h3>{selectedId === null ? "No selection" : "Selection unavailable"}</h3><p>{selectedId === null ? emptyText : "This item is no longer in the loaded results. Select another item to continue."}</p></div>}
        </DenseRowDetailPanel>
      )}
    >
      <section className="workspace-task-list" aria-label={`${title} list`}>
        <header className="workspace-task-heading"><h3>{title}</h3><p>{description}</p></header>
        <div className="workspace-task-toolbar">
          <label htmlFor={searchId}>Search {title.toLowerCase()}<Input id={searchId} type="search" value={search} onChange={(event) => update(`${stateKey}Search`, event.target.value, true)} leadingIcon={<Search className="size-4" />} placeholder="Find work…" /></label>
          <p role="status">{rows.length} of {items.length} loaded</p>
          {search ? <Button type="button" size="sm" variant="ghost" onClick={() => update(`${stateKey}Search`, "", true)}>Clear search</Button> : null}
        </div>
        <DenseDataTable columns={columns} rows={rows} getRowId={(item) => item.id} getRowAriaLabel={(item) => item.title} getRowAriaControls={() => detailId} getRowAriaExpanded={(item) => selected?.id === item.id} selectedRowId={selected?.id} onRowSelect={(item) => update(stateKey, item.id)} emptyText={items.length ? "No work items match this search." : emptyText} ariaLabel={title} caption="Select a row to inspect its context. Enter opens its detail; Escape returns to the list." />
      </section>
    </WorkspaceWorkbenchShell>
  );
}
