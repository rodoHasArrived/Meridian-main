import { useMemo, useState } from "react";
import { History } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import {
  EMPTY_HISTORICAL_WINDOW_DRAFT,
  HISTORICAL_WINDOW_IDENTIFIER_KINDS,
  historicalWindowKindLabel,
  historicalWindowRequiresProvider,
  utcToday,
  validateHistoricalWindowDraft,
  type HistoricalWindowDraft
} from "./security-passport-historical-window.view-model";

export interface HistoricalWindowSectionProps {
  /** True while any workbench request is in flight. */
  isBusy: boolean;
  /** When set, proposing is unavailable and this explains why (e.g. another revision is in progress). */
  unavailableReason: string | null;
  /** Stages the validated draft; resolves true when the draft was created. */
  onPropose: (draft: HistoricalWindowDraft) => Promise<boolean>;
  /** Injectable clock for tests; defaults to the current UTC date. */
  today?: string;
}

/**
 * Collapsible form for proposing a governed historical identifier window. Staging it creates a Draft
 * revision; the editor's existing Submit, Approve and Publish actions then carry it through the
 * maker-checker gate. The proposer cannot be the reviewer or the approver.
 */
export function HistoricalWindowSection({ isBusy, unavailableReason, onPropose, today }: HistoricalWindowSectionProps) {
  const [open, setOpen] = useState(false);
  const [draft, setDraft] = useState<HistoricalWindowDraft>(EMPTY_HISTORICAL_WINDOW_DRAFT);
  const todayUtc = today ?? utcToday();
  const validation = useMemo(() => validateHistoricalWindowDraft(draft, todayUtc), [draft, todayUtc]);
  const requiresProvider = historicalWindowRequiresProvider(draft.kind);
  const blockedReason = unavailableReason ?? (validation.valid ? null : validation.reason);

  const update = (patch: Partial<HistoricalWindowDraft>) => setDraft((current) => ({ ...current, ...patch }));

  if (!open) {
    return (
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={() => setOpen(true)}
        disabled={isBusy}
      >
        <History className="mr-1 h-3.5 w-3.5" aria-hidden /> Propose historical identifier…
      </Button>
    );
  }

  return (
    <section className="space-y-3 rounded-md border px-3 py-3" aria-label="Historical identifier window">
      <div className="space-y-1">
        <h3 className="text-sm font-medium">Historical identifier window</h3>
        <p className="text-xs text-muted-foreground">
          Imports record identifiers as valid from the ingest date. Use this to add an identifier the security
          carried earlier, backed by evidence. An independent reviewer must approve it before it is published; you
          cannot review or approve your own proposal.
        </p>
      </div>

      <fieldset className="grid gap-3 sm:grid-cols-2" disabled={isBusy}>
        <label className="space-y-1 text-sm">
          <span className="text-muted-foreground">Identifier kind</span>
          <Select
            className="h-9 w-full text-sm"
            aria-label="Identifier kind"
            value={draft.kind}
            onChange={(e) => update({ kind: e.target.value })}
          >
            {HISTORICAL_WINDOW_IDENTIFIER_KINDS.map((kind) => (
              <option key={kind} value={kind}>
                {historicalWindowKindLabel(kind)}
              </option>
            ))}
          </Select>
        </label>
        <label className="space-y-1 text-sm">
          <span className="text-muted-foreground">Identifier value</span>
          <Input aria-label="Identifier value" value={draft.value} onChange={(e) => update({ value: e.target.value })} placeholder="FB" />
        </label>
        <label className="space-y-1 text-sm">
          <span className="text-muted-foreground">Provider{requiresProvider ? " (required)" : " (optional)"}</span>
          <Input aria-label="Provider" value={draft.provider} onChange={(e) => update({ provider: e.target.value })} />
        </label>
        <div className="hidden sm:block" aria-hidden />
        <label className="space-y-1 text-sm">
          <span className="text-muted-foreground">Valid from</span>
          <Input
            type="date"
            aria-label="Valid from"
            max={todayUtc}
            value={draft.validFrom}
            onChange={(e) => update({ validFrom: e.target.value })}
          />
        </label>
        <label className="space-y-1 text-sm">
          <span className="text-muted-foreground">Valid to (leave empty if still valid)</span>
          <Input type="date" aria-label="Valid to" value={draft.validTo} onChange={(e) => update({ validTo: e.target.value })} />
        </label>
        <label className="space-y-1 text-sm">
          <span className="text-muted-foreground">Evidence Vault reference (required)</span>
          <Input
            aria-label="Evidence Vault reference"
            value={draft.evidenceReference}
            onChange={(e) => update({ evidenceReference: e.target.value })}
            placeholder="evidence-vault://…"
          />
        </label>
        <label className="space-y-1 text-sm">
          <span className="text-muted-foreground">Justification (required)</span>
          <Input
            aria-label="Window justification"
            value={draft.justification}
            onChange={(e) => update({ justification: e.target.value })}
            placeholder="Exchange ticker-change notice…"
          />
        </label>
      </fieldset>

      <div className="flex flex-wrap items-center gap-2">
        <Button
          type="button"
          size="sm"
          disabled={isBusy || blockedReason !== null}
          title={blockedReason ?? undefined}
          onClick={() => {
            void onPropose(draft).then((created) => {
              if (created) {
                setDraft(EMPTY_HISTORICAL_WINDOW_DRAFT);
                setOpen(false);
              }
            });
          }}
        >
          Stage window draft
        </Button>
        <Button type="button" size="sm" variant="ghost" onClick={() => setOpen(false)} disabled={isBusy}>
          Cancel
        </Button>
        {blockedReason ? <span className="text-xs text-muted-foreground">{blockedReason}</span> : null}
      </div>
    </section>
  );
}
