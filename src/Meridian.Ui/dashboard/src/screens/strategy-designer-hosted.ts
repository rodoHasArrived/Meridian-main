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
  StrategyDesignRunBacktestRequest,
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

type DesignerCommand = "saving" | "opening" | "running";

interface DesignerCommandToken {
  id: number;
  kind: DesignerCommand;
  runEpoch: number | null;
}

interface DesignerRunReceipt {
  epoch: number;
  outcome: StrategyDesignRunBacktestResponse;
}

function hasConflictingResponseAccount(outcome: StrategyDesignRunBacktestResponse, submittedAccount: string): boolean {
  // The current response contract has no account field. An absent claim stays unknown;
  // check only this explicit claim if a response supplies it, never an inferred alias.
  const retained = (outcome as StrategyDesignRunBacktestResponse & { fundAccountId?: unknown }).fundAccountId;
  if (retained == null) return false;
  return typeof retained !== "string"
    || normalizeFundAccountGuid(retained)?.toLowerCase() !== submittedAccount.toLowerCase();
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
  const [busy, setBusy] = useState<DesignerCommand | null>(null);
  const pending = useRef<DesignerCommandToken | null>(null);
  const commandSequence = useRef(0);
  const mounted = useRef(true);
  const [messageValue, setMessage] = useState("");
  const [errorValue, setError] = useState("");
  const [statusEpoch, setStatusEpoch] = useState<number | null>(null);
  const [runReceipt, setRunReceipt] = useState<DesignerRunReceipt | null>(null);
  const priceFilter = document.cells.find(cell => cell.cellId === "price-filter");
  const fixedShares = document.cells.find(cell => cell.cellId === "fixed-shares");
  const supportedTemplate = Boolean(priceFilter && fixedShares);
  const request: StrategyDesignRunBacktestRequest = {
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
  };
  const requestJson = JSON.stringify(request);
  const scopeKey = JSON.stringify([requestJson, Boolean(document.dirty)]);
  const scope = useRef({ key: scopeKey, epoch: 0 });
  // Withhold earlier proof in this render. A monotonic epoch also rejects A → B → A,
  // without driving state updates or replacing the operator's draft on a scope change.
  if (scope.current.key !== scopeKey) scope.current = { key: scopeKey, epoch: scope.current.epoch + 1 };
  const epoch = scope.current.epoch;
  const run = runReceipt?.epoch === epoch ? runReceipt.outcome : null;
  const message = statusEpoch === null || statusEpoch === epoch ? messageValue : "";
  const error = statusEpoch === null || statusEpoch === epoch ? errorValue : "";
  const previousRunPending = pending.current?.kind === "running" && pending.current.runEpoch !== epoch;

  useEffect(() => {
    mounted.current = true;
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
    return () => { mounted.current = false; controller.abort(); };
  }, []);

  const blocker = previousRunPending ? "A backtest from a previous scope is still running. Wait for it to finish before starting another."
    : !supportedTemplate ? "Load the equity price filter template for this backtest."
    : !document.revision || document.dirty ? "Save the current design before running."
    : !account ? "Select a brokerage account in the workspace scope."
    : !from || !to || from > to ? "Select a valid historical date range."
    : !criteria.trim() ? "Enter an acceptance criterion."
    : !/^evidence:\/\/evidence-vault\/ev-[0-9a-f]{24}$/i.test(vaultReference.trim())
      ? "Select or enter a retained Evidence Vault reference."
      : null;

  async function command(kind: DesignerCommand, task: (isCurrent: () => boolean) => Promise<void>) {
    if (pending.current) return;
    if (kind === "running" && scope.current.epoch !== epoch) return;
    const token: DesignerCommandToken = { id: ++commandSequence.current, kind, runEpoch: kind === "running" ? epoch : null };
    pending.current = token;
    setBusy(kind);
    setStatusEpoch(token.runEpoch);
    setError("");
    setMessage("");
    const isCurrent = () => mounted.current && pending.current?.id === token.id
      && (token.runEpoch === null || scope.current.epoch === token.runEpoch);
    try {
      await task(isCurrent);
    } catch (cause) {
      if (isCurrent()) setError(errorText(cause, "The Designer command failed."));
    } finally {
      if (pending.current?.id === token.id) {
        pending.current = null;
        if (mounted.current) setBusy(null);
      }
    }
  }

  function replaceDraft(next: StrategyBuilderDocument) {
    applyDocument(next);
    setRunReceipt(null);
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
    if (blocker || !account || scope.current.epoch !== epoch) return;
    // Capture exactly the serialized request, independently of later draft graph edits.
    const submittedRequest = JSON.parse(requestJson) as StrategyDesignRunBacktestRequest;
    void command("running", async (isCurrent) => {
      setRunReceipt(null);
      const outcome = await runStrategyDesignerBacktest(submittedRequest);
      if (!isCurrent()) return;
      if (outcome.strategyId !== submittedRequest.document.documentId || hasConflictingResponseAccount(outcome, account))
        throw new Error("The backtest response did not match the submitted design or account scope.");
      if (!outcome.success || !outcome.runId)
        throw new Error(outcome.runtimeError ?? "The backtest did not produce retained evidence.");
      // This receipt binds the local request epoch, not retained account ownership.
      setRunReceipt({ epoch, outcome });
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
