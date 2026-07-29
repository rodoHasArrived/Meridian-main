import { useCallback, useEffect, useState } from "react";

import {
  approveRiskEscalation,
  denyRiskEscalation,
  getRiskEscalations
} from "@/lib/api.risk-escalations";
import { ApiError } from "@/lib/api-errors";
import type { RiskEscalation, RiskEscalationApprovalResponse } from "@/types";

export interface GovernedApprovalServices {
  getRiskEscalations: () => Promise<RiskEscalation[]>;
  approveRiskEscalation: (escalationId: string, reason: string) => Promise<RiskEscalationApprovalResponse>;
  denyRiskEscalation: (escalationId: string, reason: string) => Promise<RiskEscalation>;
}

export interface GovernedApprovalsViewModel {
  escalations: RiskEscalation[];
  /**
   * True when the session may not act on the governed queue at all. Several supported
   * roles hold trade-read without order management, and for them this surface is simply
   * not theirs — not an error, and not something to keep re-requesting.
   */
  forbidden: boolean;
  reasons: Record<string, string>;
  pendingId: string | null;
  errorText: string | null;
  statusText: string | null;
  setReason: (escalationId: string, reason: string) => void;
  approve: (escalationId: string) => Promise<void>;
  deny: (escalationId: string) => Promise<void>;
  refresh: () => Promise<void>;
}

/**
 * Default services bound to the live API. A module-level constant on purpose: the view
 * model loads the queue in an effect keyed on its services, so a fresh object each render
 * would refetch continuously.
 */
export const LIVE_GOVERNED_APPROVAL_SERVICES: GovernedApprovalServices = {
  getRiskEscalations,
  approveRiskEscalation: (escalationId, reason) => approveRiskEscalation(escalationId, reason),
  denyRiskEscalation: (escalationId, reason) => denyRiskEscalation(escalationId, reason)
};

/** How often the approvals queue re-polls while the screen is open. */
export const DEFAULT_APPROVAL_REFRESH_MS = 15_000;

function toErrorText(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? error.message : fallback;
}

/**
 * Drives the governed-approval queue surface. Resolution outcomes are read back from the
 * server rather than assumed: an approval can be refused for segregation of duties (the
 * submitter cannot approve their own escalation) or for fund scope, and a release can
 * still be rejected by the risk gate it re-enters — all of which must reach the operator
 * as the reason they see, not an optimistic success.
 */
export function useGovernedApprovalsViewModel(
  services: GovernedApprovalServices,
  enabled = true,
  refreshIntervalMs = DEFAULT_APPROVAL_REFRESH_MS
): GovernedApprovalsViewModel {
  const [escalations, setEscalations] = useState<RiskEscalation[]>([]);
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [pendingId, setPendingId] = useState<string | null>(null);
  const [errorText, setErrorText] = useState<string | null>(null);
  const [statusText, setStatusText] = useState<string | null>(null);
  const [forbidden, setForbidden] = useState(false);

  const refresh = useCallback(async () => {
    if (!enabled) {
      return;
    }

    try {
      setEscalations(await services.getRiskEscalations());
      setForbidden(false);
      // A recovered poll must retire the outage banner. Leaving it up beside current data
      // gives an operator no way to tell a live failure from a healed one.
      setErrorText(null);
    } catch (error) {
      if (error instanceof ApiError && error.status === 403) {
        // Not an error state: this session simply does not manage orders. Latch it so the
        // poll stops re-requesting something it will never be allowed to have.
        setForbidden(true);
        setEscalations([]);
        setErrorText(null);
        return;
      }

      setErrorText(toErrorText(error, "Could not load the governed approval queue."));
    }
  }, [enabled, services]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useEffect(() => {
    // Escalations arrive from live strategy runs and other operators, not only from this
    // browser's own ticket. Without a poll the panel would show a queue frozen at mount
    // for as long as the screen stays open — but a session that may not read the queue
    // must not keep asking.
    if (forbidden) {
      return;
    }

    const timer = setInterval(() => void refresh(), refreshIntervalMs);
    return () => clearInterval(timer);
  }, [forbidden, refresh, refreshIntervalMs]);

  const setReason = useCallback((escalationId: string, reason: string) => {
    setReasons((current) => ({ ...current, [escalationId]: reason }));
  }, []);

  const resolve = useCallback(
    async (escalationId: string, approve: boolean) => {
      const reason = (reasons[escalationId] ?? "").trim();
      if (!reason || pendingId !== null) {
        return;
      }

      setPendingId(escalationId);
      setErrorText(null);
      setStatusText(null);
      try {
        if (approve) {
          const response = await services.approveRiskEscalation(escalationId, reason);
          // Approval and release are separate outcomes: the release re-enters the risk
          // gate and can still be refused, which is not an approval failure but must not
          // be reported as a routed order either.
          // Three distinct outcomes, and only one of them routed an order. A null release
          // result means the server recorded the approval but never submitted — reporting
          // that as "released" would tell the desk an order is working that is not.
          if (!response.releaseResult) {
            setStatusText("Approved. The order was not released; retry the release when execution is available.");
          } else if (response.releaseResult.success === false) {
            setStatusText(`Approved, but the release was refused: ${response.releaseResult.errorMessage ?? response.releaseResult.reason ?? "no reason given"}`);
          } else {
            setStatusText("Approved and released.");
          }
        } else {
          await services.denyRiskEscalation(escalationId, reason);
          setStatusText("Denied. The order was withdrawn and can no longer be released.");
        }

        setReasons((current) => {
          const next = { ...current };
          delete next[escalationId];
          return next;
        });
        await refresh();
      } catch (error) {
        setErrorText(toErrorText(error, approve ? "Approval failed." : "Denial failed."));
      } finally {
        setPendingId(null);
      }
    },
    [pendingId, reasons, refresh, services]
  );

  const approve = useCallback((escalationId: string) => resolve(escalationId, true), [resolve]);
  const deny = useCallback((escalationId: string) => resolve(escalationId, false), [resolve]);

  return { escalations, forbidden, reasons, pendingId, errorText, statusText, setReason, approve, deny, refresh };
}
