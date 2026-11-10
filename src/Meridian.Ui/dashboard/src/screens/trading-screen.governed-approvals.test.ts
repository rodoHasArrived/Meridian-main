import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import {
  useGovernedApprovalsViewModel,
  type GovernedApprovalServices
} from "@/screens/trading-screen.governed-approvals";
import type { RiskEscalation } from "@/types";

function escalation(overrides: Partial<RiskEscalation> = {}): RiskEscalation {
  return {
    escalationId: "esc-1",
    symbol: "AAPL",
    side: "Buy",
    type: "Limit",
    quantity: 200,
    limitPrice: 100,
    reason: "Order notional is inside the governed-approval band.",
    ruleName: "OrderNotional",
    status: "PendingApproval",
    parkedAt: "2026-07-28T12:00:00Z",
    resolvedBy: null,
    resolutionReason: null,
    resolvedAt: null,
    ...overrides
  };
}

function services(overrides: Partial<GovernedApprovalServices> = {}): GovernedApprovalServices {
  return {
    getRiskEscalations: vi.fn().mockResolvedValue([escalation()]),
    approveRiskEscalation: vi.fn().mockResolvedValue({
      escalation: escalation({ status: "Released" }),
      releaseResult: { success: true, orderId: "ord-1", reason: null }
    }),
    denyRiskEscalation: vi.fn().mockResolvedValue(escalation({ status: "Denied" })),
    ...overrides
  };
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((promiseResolve, promiseReject) => {
    resolve = promiseResolve;
    reject = promiseReject;
  });

  return { promise, resolve, reject };
}

describe("useGovernedApprovalsViewModel", () => {
  it("discards an older poll response that arrives after a newer one", async () => {
    const older = deferred<RiskEscalation[]>();
    const newer = deferred<RiskEscalation[]>();
    const api = services({
      getRiskEscalations: vi.fn()
        .mockReturnValueOnce(older.promise)
        .mockReturnValueOnce(newer.promise)
    });
    const { result } = renderHook(() => useGovernedApprovalsViewModel(api));

    let secondPoll!: Promise<void>;
    act(() => {
      secondPoll = result.current.refresh();
    });
    expect(api.getRiskEscalations).toHaveBeenCalledTimes(2);

    // The newer read shows the escalation already resolved elsewhere.
    await act(async () => {
      newer.resolve([]);
      await secondPoll;
    });
    expect(result.current.escalations).toEqual([]);

    // The older read still lists it and must not bring it back.
    await act(async () => {
      older.resolve([escalation()]);
      await older.promise;
    });
    expect(result.current.escalations).toEqual([]);
  });

  it("does not let an in-flight poll restore an escalation resolved locally", async () => {
    const stalePoll = deferred<RiskEscalation[]>();
    const api = services({
      getRiskEscalations: vi.fn()
        .mockResolvedValueOnce([escalation()])
        .mockReturnValueOnce(stalePoll.promise)
        .mockResolvedValue([])
    });
    const { result } = renderHook(() => useGovernedApprovalsViewModel(api));
    await waitFor(() => expect(result.current.escalations).toHaveLength(1));

    // A poll goes out and is still in flight while the operator approves.
    act(() => {
      void result.current.refresh();
    });
    act(() => result.current.setReason("esc-1", "cleared with the desk"));
    await act(async () => {
      await result.current.approve("esc-1");
    });
    expect(result.current.escalations).toEqual([]);

    await act(async () => {
      stalePoll.resolve([escalation()]);
      await stalePoll.promise;
    });
    expect(result.current.escalations).toEqual([]);
    expect(result.current.statusText).toBe("Approved and released.");
  });

  it("ignores a failure from a superseded poll", async () => {
    const older = deferred<RiskEscalation[]>();
    const api = services({
      getRiskEscalations: vi.fn()
        .mockReturnValueOnce(older.promise)
        .mockResolvedValue([escalation()])
    });
    const { result } = renderHook(() => useGovernedApprovalsViewModel(api));

    await act(async () => {
      await result.current.refresh();
    });
    expect(result.current.escalations).toHaveLength(1);

    await act(async () => {
      older.reject(new Error("gateway timeout"));
      await older.promise.catch(() => undefined);
    });
    expect(result.current.errorText).toBeNull();
    expect(result.current.escalations).toHaveLength(1);
  });

  it("lets a poll slower than the interval land instead of superseding it on every tick", async () => {
    const slow = deferred<RiskEscalation[]>();
    const api = services({
      getRiskEscalations: vi.fn()
        .mockReturnValueOnce(slow.promise)
        .mockResolvedValue([])
    });
    const { result } = renderHook(() => useGovernedApprovalsViewModel(api, true, 20));

    // Several intervals pass while the first read is still unanswered.
    await new Promise((resolve) => setTimeout(resolve, 90));
    expect(api.getRiskEscalations).toHaveBeenCalledTimes(1);

    await act(async () => {
      slow.resolve([escalation()]);
      await slow.promise;
    });
    expect(result.current.escalations).toHaveLength(1);

    // Once it has answered, the interval polls again.
    await waitFor(() => expect(vi.mocked(api.getRiskEscalations).mock.calls.length).toBeGreaterThan(1));
  });

  it("loads the caller's parked escalations", async () => {
    const { result } = renderHook(() => useGovernedApprovalsViewModel(services()));

    await waitFor(() => expect(result.current.escalations).toHaveLength(1));
    expect(result.current.escalations[0].escalationId).toBe("esc-1");
  });

  it("requires a reason before resolving", async () => {
    const api = services();
    const { result } = renderHook(() => useGovernedApprovalsViewModel(api));
    await waitFor(() => expect(result.current.escalations).toHaveLength(1));

    await act(async () => {
      await result.current.approve("esc-1");
    });

    // Reason is the audit record for a governed decision; there is no unreasoned approval.
    expect(api.approveRiskEscalation).not.toHaveBeenCalled();
  });

  it("approves with the operator's reason and reports the release", async () => {
    const api = services();
    const { result } = renderHook(() => useGovernedApprovalsViewModel(api));
    await waitFor(() => expect(result.current.escalations).toHaveLength(1));

    act(() => result.current.setReason("esc-1", "cleared with the desk"));
    await act(async () => {
      await result.current.approve("esc-1");
    });

    expect(api.approveRiskEscalation).toHaveBeenCalledWith("esc-1", "cleared with the desk");
    expect(result.current.statusText).toBe("Approved and released.");
    expect(result.current.errorText).toBeNull();
  });

  it("reports an approved escalation whose release was still refused", async () => {
    const api = services({
      approveRiskEscalation: vi.fn().mockResolvedValue({
        escalation: escalation({ status: "Approved" }),
        releaseResult: { success: false, orderId: "ord-1", reason: null, errorMessage: "Gross exposure limit exceeded." }
      })
    });
    const { result } = renderHook(() => useGovernedApprovalsViewModel(api));
    await waitFor(() => expect(result.current.escalations).toHaveLength(1));

    act(() => result.current.setReason("esc-1", "cleared"));
    await act(async () => {
      await result.current.approve("esc-1");
    });

    // The approval landed but the order did not route: reporting an unqualified success
    // would tell the desk an order is working that is not.
    expect(result.current.statusText).toContain("release was refused");
    expect(result.current.statusText).toContain("Gross exposure limit exceeded.");
  });

  it("surfaces a segregation-of-duties refusal as an error", async () => {
    const api = services({
      approveRiskEscalation: vi.fn().mockRejectedValue(
        new Error("The submitting operator cannot approve their own escalation; a distinct approver is required."))
    });
    const { result } = renderHook(() => useGovernedApprovalsViewModel(api));
    await waitFor(() => expect(result.current.escalations).toHaveLength(1));

    act(() => result.current.setReason("esc-1", "self-serve"));
    await act(async () => {
      await result.current.approve("esc-1");
    });

    expect(result.current.errorText).toContain("cannot approve their own escalation");
    expect(result.current.statusText).toBeNull();
  });

  it("denies with the operator's reason", async () => {
    const api = services();
    const { result } = renderHook(() => useGovernedApprovalsViewModel(api));
    await waitFor(() => expect(result.current.escalations).toHaveLength(1));

    act(() => result.current.setReason("esc-1", "breaches the mandate"));
    await act(async () => {
      await result.current.deny("esc-1");
    });

    expect(api.denyRiskEscalation).toHaveBeenCalledWith("esc-1", "breaches the mandate");
    expect(result.current.statusText).toContain("withdrawn");
  });
});
