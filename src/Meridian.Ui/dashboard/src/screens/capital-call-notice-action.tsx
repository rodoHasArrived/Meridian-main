import { useEffect, useMemo, useState } from "react";
import { Button } from "@/components/ui/button";
import { TechnicalDetails } from "@/components/ui/technical-details";
import { generatePrivateCapitalNotice, isConfirmedPrivateCapitalNoticeFailure } from "@/lib/api/private-capital.api";
import {
  completePendingNoticeOperation, noticeOperationRunId, noticeOperationStorageKey, noticeOperationTarget,
  readPendingNoticeOperation, replaceFailedNoticeOperation, retainPendingNoticeOperation, type PendingNoticeOperation
} from "@/lib/private-capital-notice-intent";
import type { CapitalMutation } from "@/screens/private-capital-presenters";
import type { GeneratePrivateCapitalNoticeRequest, PrivateCapitalScope } from "@/types/private-capital";

interface RecoveryState { key: string; operation: PendingNoticeOperation | null; error: string | null; clearedMalformed: boolean; failedOperationId: string | null }
export function CapitalCallNoticeAction({ scope, callId, investorId, hasNotice, locked, mutate }: {
  scope: PrivateCapitalScope; callId: string; investorId: string; hasNotice: boolean; locked: boolean; mutate: CapitalMutation
}) {
  const target = useMemo(() => noticeOperationTarget(scope, callId, investorId), [scope, callId, investorId]);
  const key = noticeOperationStorageKey(target);
  const [recovery, setRecovery] = useState<RecoveryState>({ key: "", operation: null, error: null, clearedMalformed: false, failedOperationId: null });
  useEffect(() => {
    try { setRecovery({ key, ...readPendingNoticeOperation(target), error: null, failedOperationId: null }); }
    catch (cause) { setRecovery({ key, operation: null, clearedMalformed: false, failedOperationId: null, error: cause instanceof Error ? cause.message : "Notice recovery storage is unavailable." }); }
  }, [key, target]);
  const ready = recovery.key === key && !recovery.error;
  const pending = recovery.key === key ? recovery.operation : null;
  const failedOriginal = pending && recovery.failedOperationId === pending.operationId ? pending : null;
  function generate() {
    if (!ready) return;
    let operation: PendingNoticeOperation | undefined;
    let request: GeneratePrivateCapitalNoticeRequest | undefined;
    void mutate(failedOriginal ? "New notice after confirmed failure" : pending ? "Original notice operation recovery" : hasNotice ? "Notice draft regeneration" : "Notice draft generation", async signal => {
      if (!operation) {
        const retained = readPendingNoticeOperation(target).operation;
        if (failedOriginal && retained?.operationId !== failedOriginal.operationId) throw new Error("The confirmed failed notice intent changed. Recover the retained operation again before starting a new notice.");
        operation = !failedOriginal && retained ? retained : { version: 1, target, operationId: crypto.randomUUID() };
        request = Object.freeze({ operationId: operation.operationId });
      }
      // Retain before every attempt, including uncertain retries. A failed write never sends.
      if (failedOriginal) replaceFailedNoticeOperation(failedOriginal, operation);
      else retainPendingNoticeOperation(operation);
      setRecovery({ key, operation, clearedMalformed: false, error: null, failedOperationId: null });
      let result;
      try { result = await generatePrivateCapitalNotice(scope.ledgerBookId, callId, investorId, request!, { signal }); }
      catch (cause) {
        if (!signal.aborted && isConfirmedPrivateCapitalNoticeFailure(cause, operation.operationId)) setRecovery({ key, operation, clearedMalformed: false, error: null, failedOperationId: operation.operationId });
        throw cause;
      }
      if (!signal.aborted) {
        if (result?.run?.runId !== noticeOperationRunId(operation.operationId)) throw new Error("The notice result does not match the original operation. Recover that operation before starting another run.");
        completePendingNoticeOperation(operation);
        setRecovery({ key, operation: null, clearedMalformed: false, error: null, failedOperationId: null });
      }
      return result;
    });
  }
  const action = failedOriginal ? "Generate new notice after confirmed failure" : pending ? "Recover original notice" : hasNotice ? "Regenerate notice" : "Generate notice";
  return <div className="space-y-1">
    <Button variant="outline" disabled={locked || !ready} disabledReason={locked ? "Wait for the current operation or resolve its unconfirmed outcome." : !ready ? recovery.error ?? "Loading notice recovery for this investor." : undefined} onClick={generate}>{action} for {investorId}</Button>
    {pending ? <><p role="status" className="text-xs text-muted-foreground">{failedOriginal ? "The original notice run failed; its audit remains retained. A new notice requires the explicit action above." : "An original notice operation remains unconfirmed. Recover it before starting another run."}</p>
      <TechnicalDetails label="Pending notice operation reference"><p className="break-all text-xs">{pending.operationId}</p></TechnicalDetails></> : null}
    {recovery.error ? <p role="alert" className="text-xs text-danger">{recovery.error}</p> : null}
    {recovery.clearedMalformed ? <p role="status" className="text-xs text-warning">An invalid notice recovery record was cleared. Inspect retained notice runs before starting another run.</p> : null}
  </div>;
}
