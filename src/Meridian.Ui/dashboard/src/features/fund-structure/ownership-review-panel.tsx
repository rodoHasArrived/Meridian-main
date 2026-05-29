import { useEffect, useMemo, useState } from "react";
import { AlertTriangle, CheckCircle2, GitBranch, RefreshCw } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { getFundStructureGraph, type FundStructureGraph, type FundStructureGraphNode, type FundStructureOwnershipLink } from "@/lib/api";

export type OwnershipValidationState = "Valid" | "Warning" | "Blocking" | "Inactive";

export interface OwnershipReviewRow {
  ownershipLinkId: string;
  nodeDisplayLabel: string;
  parentLabel: string;
  childLabel: string;
  relationshipLabel: string;
  percentLabel: string;
  isPrimary: boolean;
  effectiveWindowLabel: string;
  isActiveAsOf: boolean;
  validationState: OwnershipValidationState;
  blockingMessages: string[];
  suggestedRemediationActions: string[];
}

export interface OwnershipReviewSummary {
  totalLinkCount: number;
  activeLinkCount: number;
  invalidLinkCount: number;
  blockingLinkCount: number;
  primaryLinkCount: number;
  primaryOwnershipPercentTotal: number | null;
  statusLabel: string;
}

export function OwnershipReviewPanel() {
  const today = new Date().toISOString().slice(0, 10);
  const [asOfDate, setAsOfDate] = useState(today);
  const [activeOnly, setActiveOnly] = useState(false);
  const [graph, setGraph] = useState<FundStructureGraph | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = () => {
    const controller = new AbortController();
    setLoading(true);
    setError(null);
    getFundStructureGraph({ activeOnly, asOf: asOfDate ? new Date(`${asOfDate}T00:00:00.000Z`).toISOString() : null }, { signal: controller.signal })
      .then((next) => {
        setGraph(next);
        const first = projectOwnershipReview(next, new Date(`${asOfDate}T00:00:00.000Z`), activeOnly).rows[0]?.ownershipLinkId ?? null;
        setSelectedId((current) => current && next.ownershipLinks.some((link) => link.ownershipLinkId === current) ? current : first);
      })
      .catch((reason: unknown) => {
        if ((reason as { name?: string }).name !== "AbortError") {
          setError(reason instanceof Error ? reason.message : "Unable to load ownership graph.");
        }
      })
      .finally(() => setLoading(false));

    return () => controller.abort();
  };

  useEffect(load, []); // initial operator review load; explicit button handles changed filters.

  const review = useMemo(
    () => projectOwnershipReview(graph ?? { nodes: [], ownershipLinks: [] }, new Date(`${asOfDate}T00:00:00.000Z`), activeOnly),
    [activeOnly, asOfDate, graph]
  );
  const selected = review.rows.find((row) => row.ownershipLinkId === selectedId) ?? review.rows[0] ?? null;

  return (
    <section className="space-y-4" aria-labelledby="ownership-review-title">
      <Card>
        <CardHeader>
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div>
              <CardTitle id="ownership-review-title" className="flex items-center gap-2">
                <GitBranch className="h-5 w-5" aria-hidden="true" /> Ownership review
              </CardTitle>
              <CardDescription>Operator ownership graph/table backed by /api/fund-structure/graph and lifecycle link endpoints.</CardDescription>
            </div>
            <Badge variant={review.summary.blockingLinkCount > 0 ? "danger" : review.summary.invalidLinkCount > 0 ? "warning" : "success"}>{review.summary.statusLabel}</Badge>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex flex-wrap items-end gap-3">
            <label className="grid gap-1 text-sm">
              <span className="text-muted-foreground">As-of date</span>
              <input className="rounded-md border border-border bg-background px-3 py-2" type="date" value={asOfDate} onChange={(event) => setAsOfDate(event.target.value)} />
            </label>
            <label className="flex items-center gap-2 rounded-md border border-border px-3 py-2 text-sm">
              <input type="checkbox" checked={activeOnly} onChange={(event) => setActiveOnly(event.target.checked)} /> Active links only
            </label>
            <Button type="button" onClick={load} disabled={loading}>
              <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" /> Refresh graph
            </Button>
          </div>

          {error ? <div role="alert" className="rounded-md border border-danger/40 bg-danger/10 p-3 text-sm text-danger">{error}</div> : null}

          <div className="grid gap-3 md:grid-cols-4">
            <Metric label="Total links" value={review.summary.totalLinkCount} />
            <Metric label="Active" value={review.summary.activeLinkCount} />
            <Metric label="Invalid" value={review.summary.invalidLinkCount} />
            <Metric label="Primary rollup" value={review.summary.primaryOwnershipPercentTotal === null ? "n/a" : `${review.summary.primaryOwnershipPercentTotal.toFixed(2)}%`} />
          </div>
        </CardContent>
      </Card>

      {review.rows.length === 0 ? (
        <Card><CardContent className="p-6 text-sm text-muted-foreground">No ownership links exist yet. Add ownership links before setup completion so operators can review control, advisory, custody, and allocation state.</CardContent></Card>
      ) : (
        <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(320px,1fr)]">
          <Card>
            <CardHeader>
              <CardTitle>Ownership graph table</CardTitle>
              <CardDescription>Invalid-link warnings stay visible even before setup completion.</CardDescription>
            </CardHeader>
            <CardContent className="overflow-x-auto">
              <table className="w-full min-w-[760px] text-left text-sm">
                <thead className="text-xs uppercase text-muted-foreground">
                  <tr><th className="py-2">Relationship</th><th>Percent</th><th>Window</th><th>Primary</th><th>Validation</th></tr>
                </thead>
                <tbody>
                  {review.rows.map((row) => (
                    <tr key={row.ownershipLinkId} className="border-t border-border">
                      <td className="py-2">
                        <button type="button" className="text-left font-medium hover:underline" onClick={() => setSelectedId(row.ownershipLinkId)}>{row.nodeDisplayLabel}</button>
                      </td>
                      <td>{row.percentLabel}</td>
                      <td>{row.effectiveWindowLabel}</td>
                      <td>{row.isPrimary ? "Primary" : "Secondary"}</td>
                      <td><ValidationBadge state={row.validationState} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </CardContent>
          </Card>

          <Card>
            <CardHeader>
              <CardTitle>Selected-link inspector</CardTitle>
              <CardDescription>{selected?.nodeDisplayLabel ?? "Select a link"}</CardDescription>
            </CardHeader>
            <CardContent className="space-y-4 text-sm">
              {selected ? <>
                <ValidationBadge state={selected.validationState} />
                <div><div className="font-medium">Warnings</div>{selected.blockingMessages.length ? <ul className="mt-1 list-disc pl-5">{selected.blockingMessages.map((message) => <li key={message}>{message}</li>)}</ul> : <p className="text-muted-foreground">No invalid-link warnings.</p>}</div>
                <div><div className="font-medium">Suggested remediation</div>{selected.suggestedRemediationActions.length ? <ul className="mt-1 list-disc pl-5">{selected.suggestedRemediationActions.map((action) => <li key={action}>{action}</li>)}</ul> : <p className="text-muted-foreground">No remediation required.</p>}</div>
                <div className="flex flex-wrap gap-2">
                  <Button type="button" disabled={!selected}>Edit</Button>
                  <Button type="button" variant="secondary" disabled={!selected}>Expire</Button>
                  <Button type="button" variant="secondary" disabled={!selected}>Replace</Button>
                </div>
              </> : null}
            </CardContent>
          </Card>
        </div>
      )}
    </section>
  );
}

function Metric({ label, value }: { label: string; value: string | number }) {
  return <div className="rounded-lg border border-border p-3"><div className="text-xs uppercase text-muted-foreground">{label}</div><div className="mt-1 text-2xl font-semibold">{value}</div></div>;
}

function ValidationBadge({ state }: { state: OwnershipValidationState }) {
  const icon = state === "Valid" ? <CheckCircle2 className="mr-1 h-3 w-3" aria-hidden="true" /> : <AlertTriangle className="mr-1 h-3 w-3" aria-hidden="true" />;
  const variant = state === "Valid" ? "success" : state === "Blocking" ? "danger" : "warning";
  return <Badge variant={variant}>{icon}{state}</Badge>;
}

export function projectOwnershipReview(graph: FundStructureGraph, asOf: Date, activeOnly = false): { rows: OwnershipReviewRow[]; summary: OwnershipReviewSummary } {
  const nodes = new Map(graph.nodes.map((node) => [node.nodeId, node]));
  const rows = graph.ownershipLinks
    .filter((link) => !activeOnly || isActive(link, asOf))
    .sort((a, b) => `${a.parentNodeId}-${a.childNodeId}-${a.relationshipType}-${a.ownershipLinkId}`.localeCompare(`${b.parentNodeId}-${b.childNodeId}-${b.relationshipType}-${b.ownershipLinkId}`))
    .map((link) => projectRow(link, nodes, asOf));
  const activeLinkCount = rows.filter((row) => row.isActiveAsOf).length;
  const invalidLinkCount = rows.filter((row) => row.validationState === "Warning" || row.validationState === "Blocking").length;
  const blockingLinkCount = rows.filter((row) => row.validationState === "Blocking").length;
  const primaryRows = rows.filter((row) => row.isPrimary);
  const primaryOwnershipPercentTotal = primaryRows.length
    ? graph.ownershipLinks.filter((link) => link.isPrimary && isActive(link, asOf) && link.ownershipPercent !== null && link.ownershipPercent !== undefined).reduce((sum, link) => sum + Number(link.ownershipPercent), 0)
    : null;
  return {
    rows,
    summary: {
      totalLinkCount: rows.length,
      activeLinkCount,
      invalidLinkCount,
      blockingLinkCount,
      primaryLinkCount: primaryRows.length,
      primaryOwnershipPercentTotal,
      statusLabel: rows.length === 0 ? "No ownership links to review" : blockingLinkCount > 0 ? `${blockingLinkCount} blocking ownership issue(s)` : invalidLinkCount > 0 ? `${invalidLinkCount} ownership warning(s)` : "Ownership review ready"
    }
  };
}

function projectRow(link: FundStructureOwnershipLink, nodes: Map<string, FundStructureGraphNode>, asOf: Date): OwnershipReviewRow {
  const parent = nodes.get(link.parentNodeId);
  const child = nodes.get(link.childNodeId);
  const isActiveAsOf = isActive(link, asOf);
  const blockingMessages: string[] = [];
  const suggestedRemediationActions: string[] = [];
  if (!parent) { blockingMessages.push("Parent node is missing from the graph snapshot."); suggestedRemediationActions.push("Refresh /api/fund-structure/graph or recreate the link with a valid parent node."); }
  if (!child) { blockingMessages.push("Child node is missing from the graph snapshot."); suggestedRemediationActions.push("Refresh /api/fund-structure/graph or recreate the link with a valid child node."); }
  if (!isActiveAsOf) { blockingMessages.push(new Date(link.effectiveFrom) > asOf ? "Ownership link is not effective yet for the selected as-of date." : "Ownership link is expired for the selected as-of date."); suggestedRemediationActions.push("Change the as-of date, expire the stale link, or replace it with an active relationship."); }
  if (link.ownershipPercent !== null && link.ownershipPercent !== undefined && (link.ownershipPercent < 0 || link.ownershipPercent > 100)) { blockingMessages.push("Ownership percent must be between 0 and 100."); suggestedRemediationActions.push("Edit the link and enter a percent from 0 to 100."); }
  if ((link.relationshipType === "Owns" || link.relationshipType === "AllocatesTo") && (link.ownershipPercent === null || link.ownershipPercent === undefined)) { blockingMessages.push("Economic ownership or allocation links should carry an explicit percent before setup completion."); suggestedRemediationActions.push("Edit the link and add the ownership percent, or replace it with a non-economic relationship type."); }
  const validationState: OwnershipValidationState = !parent || !child ? "Blocking" : !isActiveAsOf ? "Inactive" : blockingMessages.length ? "Warning" : "Valid";
  const parentLabel = label(parent, link.parentNodeId);
  const childLabel = label(child, link.childNodeId);
  const relationshipLabel = relationship(link.relationshipType);
  return {
    ownershipLinkId: link.ownershipLinkId,
    nodeDisplayLabel: `${parentLabel} ${relationshipLabel} ${childLabel}`,
    parentLabel,
    childLabel,
    relationshipLabel,
    percentLabel: link.ownershipPercent === null || link.ownershipPercent === undefined ? "Not specified" : `${link.ownershipPercent}%`,
    isPrimary: link.isPrimary,
    effectiveWindowLabel: `${link.effectiveFrom.slice(0, 10)} → ${link.effectiveTo?.slice(0, 10) ?? "open-ended"}`,
    isActiveAsOf,
    validationState,
    blockingMessages,
    suggestedRemediationActions: [...new Set(suggestedRemediationActions)]
  };
}

function label(node: FundStructureGraphNode | undefined, fallbackId: string) {
  return node ? `${node.kind} ${node.code} — ${node.name}` : `Missing node ${fallbackId}`;
}

function relationship(value: string) {
  return ({ Owns: "owns", Advises: "advises", Operates: "operates", ClearsFor: "clears for", CustodiesFor: "custodies for", AllocatesTo: "allocates to" } as Record<string, string>)[value] ?? value;
}

function isActive(link: FundStructureOwnershipLink, asOf: Date) {
  const from = new Date(link.effectiveFrom);
  const to = link.effectiveTo ? new Date(link.effectiveTo) : null;
  return from <= asOf && (!to || to > asOf);
}
