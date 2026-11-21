import { useEffect, useRef, useState } from "react";
import {
  getStrategyDesignerDraft,
  getStrategyDesignerDrafts,
  getStrategyDesignerTemplates,
  listEvidenceVaultDocuments,
  runStrategyDesignerBacktest,
  saveStrategyDesignerDraft
} from "@/lib/api";
import { ApiError } from "@/lib/api-errors";
import { normalizeFundAccountGuid } from "@/lib/fund-account-scope";
import type {
  EvidenceVaultDocumentEntry,
  StrategyDesignDocument,
  StrategyDesignDraftSummary,
  StrategyDesignRunBacktestResponse
} from "@/types";
import type {
  StrategyBuilderCell,
  StrategyBuilderDocument,
  StrategyBuilderTransition
} from "@/screens/strategy-designer-screen.view-model";

function errorText(error: unknown, fallback: string) {
  if (error instanceof ApiError) {
    if (error.responseBody) {
      try {
        const body = JSON.parse(error.responseBody) as Partial<StrategyDesignRunBacktestResponse>;
        if (body.runtimeError) return body.runtimeError;
      } catch {
        // Fall through to the transport's problem detail.
      }
    }
    return error.detail ?? error.message;
  }
  return error instanceof Error ? error.message : fallback;
}

export function toStrategyDesignDocument(document: StrategyBuilderDocument): StrategyDesignDocument {
  const { dirty: _dirty, createdAt, ...fields } = document;
  return { ...fields, createdAt: createdAt ?? document.updatedAt };
}

function fromStrategyDesignDocument(document: StrategyDesignDocument): StrategyBuilderDocument {
  // Keep the full persisted fields on reopen so saving a template cannot erase its evidence
  // metadata or evaluator parameters. The workbench renders unsupported kinds as draft cells.
  return {
    ...document,
    cells: document.cells.map(cell => ({ ...cell, parameters: cell.parameters ?? undefined })) as StrategyBuilderCell[],
    transitions: document.transitions as StrategyBuilderTransition[],
    dirty: false
  };
}

export function useHostedDesigner(
  document: StrategyBuilderDocument,
  applyDocument: (value: StrategyBuilderDocument) => void,
  fundAccountId?: string | null
) {
  const account = normalizeFundAccountGuid(fundAccountId);
  const [drafts, setDrafts] = useState<StrategyDesignDraftSummary[]>([]);
  const [template, setTemplate] = useState<StrategyDesignDocument | null>(null);
  const [evidence, setEvidence] = useState<EvidenceVaultDocumentEntry[]>([]);
  const [vaultReference, setVaultReference] = useState("");
  const [criteria, setCriteria] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [busy, setBusy] = useState<"saving" | "opening" | "running" | null>(null);
  const pending = useRef(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [run, setRun] = useState<StrategyDesignRunBacktestResponse | null>(null);
  const priceFilter = document.cells.find(cell => cell.cellId === "price-filter");
  const fixedShares = document.cells.find(cell => cell.cellId === "fixed-shares");
  const supportedTemplate = Boolean(priceFilter && fixedShares);

  useEffect(() => {
    const controller = new AbortController();
    const options = { signal: controller.signal };
    void getStrategyDesignerDrafts(options).then(value => {
      if (!controller.signal.aborted) setDrafts(value);
    }).catch(() => undefined);
    void getStrategyDesignerTemplates(options).then(value => {
      if (!controller.signal.aborted)
        setTemplate(value.find(item => item.templateId === "equity-price-filter")?.document ?? null);
    }).catch(() => undefined);
    void listEvidenceVaultDocuments({ maxResults: 25 }, options).then(value => {
      if (!controller.signal.aborted) setEvidence(value);
    }).catch(() => undefined);
    return () => controller.abort();
  }, []);

  const blocker = !supportedTemplate ? "Load the equity price filter template for this backtest."
    : !document.revision || document.dirty ? "Save the current design before running."
    : !account ? "Select a brokerage account in the workspace scope."
    : !from || !to || from > to ? "Select a valid historical date range."
    : !criteria.trim() ? "Enter an acceptance criterion."
    : !/^evidence:\/\/evidence-vault\/ev-[0-9a-f]{24}$/i.test(vaultReference.trim())
      ? "Select or enter a retained Evidence Vault reference."
      : null;

  async function command(kind: NonNullable<typeof busy>, task: () => Promise<void>) {
    if (pending.current) return;
    pending.current = true;
    setBusy(kind);
    setError("");
    setMessage("");
    try {
      await task();
    } catch (cause) {
      setError(errorText(cause, "The Designer command failed."));
    } finally {
      pending.current = false;
      setBusy(null);
    }
  }

  function replaceDraft(next: StrategyBuilderDocument) {
    applyDocument(next);
    setRun(null);
  }

  function newEquityDraft() {
    if (!template || busy) return;
    if (document.dirty && !window.confirm("Discard unsaved changes to this draft?")) return;
    replaceDraft(fromStrategyDesignDocument({
      ...template,
      documentId: "strategy-design-" + crypto.randomUUID(),
      revision: null
    }));
    setError("");
    setMessage("");
  }

  function edit(field: "symbol" | "condition" | "shares", value: string) {
    if (busy) return;
    replaceDraft({
      ...document,
      dirty: true,
      universe: field === "symbol" ? [value] : document.universe,
      cells: document.cells.map(cell => {
        if (field === "condition" && cell.cellId === "price-filter") return { ...cell, source: value };
        if (field === "shares" && cell.cellId === "fixed-shares")
          return { ...cell, parameters: { ...cell.parameters, sizingValue: value } };
        return cell;
      })
    });
  }

  function openDraft(documentId: string) {
    if (!documentId || (document.dirty && !window.confirm("Discard unsaved changes to this draft?"))) return;
    void command("opening", async () => {
      const saved = await getStrategyDesignerDraft(documentId);
      replaceDraft(fromStrategyDesignDocument(saved));
      setMessage("Saved draft opened.");
    });
  }

  function save() {
    void command("saving", async () => {
      const saved = await saveStrategyDesignerDraft({
        document: toStrategyDesignDocument(document),
        expectedRevision: document.revision ?? null
      });
      replaceDraft(fromStrategyDesignDocument(saved.document));
      setDrafts(items => [saved.summary, ...items.filter(item => item.documentId !== saved.summary.documentId)]);
      setMessage(saved.validation.isValid ? "Draft saved." : "Draft saved. " + saved.validation.summary);
    });
  }

  function runBacktest() {
    if (blocker || !account) return;
    void command("running", async () => {
      setRun(null);
      const outcome = await runStrategyDesignerBacktest({
        document: toStrategyDesignDocument(document),
        fundAccountId: account,
        from,
        to,
        operatorAcceptanceCriteria: [criteria.trim()],
        retainedEvidenceReferences: [vaultReference.trim()],
        accountingRecordReferences: [],
        approvalReferences: [],
        paperValidationReferences: [],
        governedReportReferences: []
      });
      if (!outcome.success || !outcome.runId)
        throw new Error(outcome.runtimeError ?? "The backtest did not produce retained evidence.");
      setRun(outcome);
      setMessage("Backtest completed and retained.");
    });
  }

  return {
    account, busy, error, message, run, blocker, drafts, evidence, template,
    supportedTemplate, priceFilter, fixedShares,
    from, to, setFrom, setTo, criteria, setCriteria, vaultReference, setVaultReference,
    edit, newEquityDraft, openDraft, replaceDraft, save, runBacktest
  };
}

export type HostedDesigner = ReturnType<typeof useHostedDesigner>;
