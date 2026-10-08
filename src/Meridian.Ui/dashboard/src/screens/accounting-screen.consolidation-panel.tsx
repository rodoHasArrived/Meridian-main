import { useEffect, useRef, useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { FormGrid, FormRow } from "@/components/ui/form";
import { Input } from "@/components/ui/input";
import { describeApiError, type ApiErrorDisplay } from "@/lib/api-errors";
import { createConsolidationDrafts, previewConsolidation } from "@/lib/consolidation-api";
import type { ConsolidationRequest, ConsolidationView } from "@/types/consolidation";
import { ConsolidationResults } from "./accounting-screen.consolidation-results";

interface ConsolidationPanelProps {
  initialBookId?: string | null;
  initialPeriodId?: string | null;
}

// Remount scope state when the surrounding ledger selection changes, discarding old responses.
export function ConsolidationPanel(props: ConsolidationPanelProps) {
  return <ConsolidationScopePanel key={`${props.initialBookId}:${props.initialPeriodId}`} {...props} />;
}

function ConsolidationScopePanel({ initialBookId, initialPeriodId }: ConsolidationPanelProps) {
  const [request, setRequest] = useState<ConsolidationRequest>({
    organizationId: "", ownershipRootId: "", eliminationBookId: initialBookId ?? "", periodId: initialPeriodId ?? "", asOf: ""
  });
  const [view, setView] = useState<ConsolidationView | null>(null);
  const [error, setError] = useState<ApiErrorDisplay | null>(null);
  const [busy, setBusy] = useState<"preview" | "drafts" | null>(null);
  const [message, setMessage] = useState("");
  const activeRequest = useRef<AbortController | null>(null);
  useEffect(() => () => activeRequest.current?.abort(), []);

  const ready = Object.values(request).every((value) => value.trim().length > 0);
  const canCreateDrafts = view?.canCreateDrafts === true;
  function updateField(field: keyof ConsolidationRequest, value: string) {
    setRequest((current) => ({ ...current, [field]: value }));
    setView(null);
    setError(null);
    setMessage("Scope changed. Preview this perimeter before creating review drafts.");
  }

  async function run(action: "preview" | "drafts") {
    if (!ready || activeRequest.current || (action === "drafts" && (!canCreateDrafts || !view || view.blockers.length > 0))) return;
    const controller = new AbortController();
    activeRequest.current = controller;
    setBusy(action);
    setError(null);
    setMessage("");
    try {
      const next = await (action === "preview" ? previewConsolidation : createConsolidationDrafts)(request, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setView(next);
      setMessage(action === "drafts"
        ? "Review drafts refreshed. Use the journal approval workflow to review, approve, and post."
        : "Preview refreshed. Proposed amounts remain separate from posted balances.");
    } catch (failure) {
      if (controller.signal.aborted) return;
      setView(null);
      setError(describeApiError(failure, "Consolidation could not be refreshed. Preview again before creating drafts."));
    } finally {
      if (!controller.signal.aborted) {
        activeRequest.current = null;
        setBusy(null);
      }
    }
  }

  function preview(event: FormEvent) {
    event.preventDefault();
    void run("preview");
  }

  return (
    <Card className="panel-surface min-w-0" aria-labelledby="consolidation-title">
      <CardHeader>
        <CardTitle id="consolidation-title">Group consolidation</CardTitle>
        <CardDescription>First slice: exactly two directly wholly owned entities under one ownership root, Primary basis, and one shared functional currency. Foreign currency translation, minority interests, and nested ownership are outside this slice.</CardDescription>
      </CardHeader>
      <CardContent className="space-y-5">
        <form onSubmit={preview} className="space-y-3">
          <fieldset disabled={busy !== null} className="min-w-0 space-y-3">
            <legend className="mb-3 text-sm font-semibold">Effective ownership perimeter</legend>
            <FormGrid columns={3}>
              {([
                ["organizationId", "Organization ID"], ["ownershipRootId", "Ownership root ID"],
                ["eliminationBookId", "Elimination book ID"], ["periodId", "Elimination period ID"]
              ] as const).map(([field, label]) => (
                <FormRow key={field} label={label} labelFor={`consolidation-${field}`}>
                  <Input id={`consolidation-${field}`} value={request[field]} onChange={(event) => updateField(field, event.target.value.trim())} required pattern="[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}" title="Enter a complete UUID" autoComplete="off" />
                </FormRow>
              ))}
              <FormRow label="Ownership as of" labelFor="consolidation-asOf">
                <Input id="consolidation-asOf" type="date" value={request.asOf} onChange={(event) => updateField("asOf", event.target.value)} required />
              </FormRow>
            </FormGrid>
            <p className="text-xs leading-5 text-muted-foreground">Book and period start from the current ledger selection. Select the dedicated elimination book and its period. Authoritative effective-dated ownership determines the included entities.</p>
            <div className="flex flex-wrap gap-2">
              <Button type="submit" variant="outline" disabled={!ready || busy !== null}>{busy === "preview" ? "Refreshing preview…" : "Preview consolidation"}</Button>
              <Button type="button" disabled={!canCreateDrafts || !view || view.blockers.length > 0 || busy !== null} aria-describedby={view && !canCreateDrafts ? "consolidation-draft-permission" : undefined} onClick={() => void run("drafts")}>{busy === "drafts" ? "Creating review drafts…" : "Create review drafts"}</Button>
            </div>
            {view && !canCreateDrafts ? <p id="consolidation-draft-permission" className="text-sm text-muted-foreground">You can inspect this preview. Creating review drafts requires an authenticated session with ledger management permission.</p> : null}
          </fieldset>
        </form>
        <p role="status" aria-live="polite" className="text-sm text-muted-foreground">{message || (busy ? "Refreshing authoritative consolidation evidence…" : "Preview the effective ownership perimeter to inspect balances and reciprocal matches.")}</p>
        {error ? <div role="alert" className="rounded border border-danger/35 bg-danger/10 p-3 text-sm text-danger"><p>{error.summary}</p>{error.details.length > 0 ? <ul className="mt-2 list-disc pl-5">{error.details.map((detail) => <li key={detail}>{detail}</li>)}</ul> : null}</div> : null}
        {view ? <ConsolidationResults view={view} /> : null}
      </CardContent>
    </Card>
  );
}
