import { act, renderHook, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api-errors";
import type {
  CapitalCallIssuanceIntakeRunResult
} from "@/lib/api/capital-call-issuance.api";
import {
  buildCapitalCallIssuanceRequest,
  createInitialCapitalCallIssuanceForm,
  presentCapitalCallRunOutcome,
  presentCreatedDrafts,
  presentRunLevelAssessments,
  presentSkips,
  useCapitalCallIssuanceViewModel,
  validateCapitalCallIssuanceForm,
  type CapitalCallIssuanceFormState
} from "@/screens/capital-call-issuance-screen.view-model";
import type { ManualJournalEntryDraft } from "@/types";

const LEDGER_BOOK_ID = "3f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e11";

function validForm(): CapitalCallIssuanceFormState {
  const form = createInitialCapitalCallIssuanceForm();
  return {
    ...form,
    fundProfileId: "fund-alpha",
    ledgerBookId: LEDGER_BOOK_ID,
    currency: "usd",
    callId: "call-2026-03",
    amountToCall: "250000",
    noticeDate: "2026-03-01",
    dueDate: "2026-03-15",
    periodId: "2026-03",
    entityId: "",
    purpose: "Follow-on investment",
    allocationBasis: "pro-rata-total-commitment",
    commitments: [
      {
        ...form.commitments[0],
        commitmentId: "commit-lp1",
        capitalAccountId: "cap-lp1",
        investorId: "lp-1",
        totalCommitment: "1000000",
        commitmentDate: "2025-06-30",
        evidenceLink: "evidence://register/lp1"
      }
    ]
  };
}

function createdDraft(overrides: Partial<ManualJournalEntryDraft> = {}): ManualJournalEntryDraft {
  return {
    journalEntryId: "6a1f0000-0000-0000-0000-000000000001",
    status: "Draft",
    fundProfileId: "fund-alpha",
    ledgerBookId: LEDGER_BOOK_ID,
    accountingBasis: "Primary",
    accountingDate: "2026-03-01",
    currency: "USD",
    memo: "Capital call call-2026-03 — LP 1",
    preparedBy: "session-operator",
    createdAtUtc: "2026-03-01T00:00:00Z",
    updatedAtUtc: "2026-03-01T00:00:00Z",
    version: 1,
    lines: [],
    evidenceLinks: ["evidence://register/lp1"],
    validationIssues: [],
    totalDebits: 250000,
    totalCredits: 250000,
    imbalance: 0,
    entryType: "CapitalCall",
    treasuryContext: {
      idempotencyKey: "capital-call|fund-alpha|call-2026-03|commit-lp1",
      capitalAccountId: "cap-lp1",
      investorId: "lp-1"
    },
    ...overrides
  };
}

function readyRunResult(): CapitalCallIssuanceIntakeRunResult {
  return {
    producerSkips: [],
    intake: {
      created: [createdDraft()],
      skipped: [],
      needsFixCount: 0
    },
    evidenceAssessments: {
      "capital-call|fund-alpha|call-2026-03|commit-lp1": {
        assessmentCode: "capital-call-commitment-corroboration",
        confidenceScore: 0.8,
        quality: "Medium",
        requiresInvestigation: false,
        summary: "Server-recomputed uncalled commitment 1000000.00 USD for 'commit-lp1' from 0 posted fund-event step(s); call 250000.00 is 25.00% of the requested 250000.00.",
        reasons: [
          "No posted private-capital activity exists for capital account 'cap-lp1'; the operator-attested commitment register is the sole basis for this first call."
        ],
        evidenceLinks: ["evidence://register/lp1"]
      }
    },
    readiness: 0,
    readinessBlockers: []
  };
}

function blockedRunResult(): CapitalCallIssuanceIntakeRunResult {
  return {
    producerSkips: [{ subject: "commit-lp2", reason: "Commitment 'commit-lp2' is not callable." }],
    intake: { created: [], skipped: [], needsFixCount: 0 },
    evidenceAssessments: {
      "capital-call|fund-alpha|call-2026-03": {
        assessmentCode: "capital-call-commitment-corroboration",
        confidenceScore: 0,
        quality: "Low",
        requiresInvestigation: true,
        summary: "Capital-call issuance cannot enter approval: Commitment 'commit-lp1' carries no retained commitment-register evidence; the attested total cannot back a capital call.",
        reasons: [
          "Commitment 'commit-lp1' carries no retained commitment-register evidence; the attested total cannot back a capital call."
        ],
        evidenceLinks: []
      }
    },
    readiness: 2,
    readinessBlockers: [
      "Commitment 'commit-lp1' carries no retained commitment-register evidence; the attested total cannot back a capital call.",
      "Capital call 'call-2026-03' is not executable: allocated 0 of requested 250000 across 0 line(s)."
    ]
  };
}

describe("buildCapitalCallIssuanceRequest", () => {
  it("maps the form to the wire shape with numeric enum codes and no actor field", () => {
    const request = buildCapitalCallIssuanceRequest(validForm());

    expect(request).toEqual({
      fundProfileId: "fund-alpha",
      currency: "USD",
      callId: "call-2026-03",
      amountToCall: 250000,
      noticeDate: "2026-03-01",
      dueDate: "2026-03-15",
      allocationBasis: 1,
      ledgerBookId: LEDGER_BOOK_ID,
      purpose: "Follow-on investment",
      periodId: "2026-03",
      entityId: null,
      commitments: [
        {
          commitmentId: "commit-lp1",
          capitalAccountId: "cap-lp1",
          investorId: "lp-1",
          totalCommitment: 1000000,
          commitmentDate: "2025-06-30",
          status: 0,
          evidenceLinks: ["evidence://register/lp1"]
        }
      ]
    });

    // The server resolves the operator from the authenticated session; a client-sent
    // actor would be ignored, so the wire request must not carry one.
    expect("actor" in request).toBe(false);

    // CapitalCallAllocationBasis and CommitmentStatus bind numerically at the endpoint —
    // string enum names would fail model binding.
    expect(typeof request.allocationBasis).toBe("number");
    expect(typeof request.commitments[0].status).toBe("number");
  });

  it("uses the pro-rata-by-uncalled code 0 for the default basis", () => {
    const request = buildCapitalCallIssuanceRequest({
      ...validForm(),
      allocationBasis: "pro-rata-uncalled"
    });

    expect(request.allocationBasis).toBe(0);
  });
});

describe("validateCapitalCallIssuanceForm", () => {
  it("accepts a fully specified form", () => {
    expect(validateCapitalCallIssuanceForm(validForm())).toEqual([]);
  });

  it("requires every server-validated field, without fabricating amounts", () => {
    const issues = validateCapitalCallIssuanceForm(createInitialCapitalCallIssuanceForm());

    expect(issues).toContain("Fund profile identifier is required.");
    expect(issues).toContain("Ledger book is required — without it the drafts land in the queue as book-missing.");
    expect(issues).toContain("Capital-call accounting currency must be a three-letter ISO code.");
    expect(issues).toContain("Capital-call identifier is required.");
    expect(issues).toContain("Amount to call must be a positive number.");
    expect(issues).toContain("Notice date is required.");
    expect(issues).toContain("Due date is required.");
    expect(issues.some((issue) => issue.includes("commitment identifier is required"))).toBe(true);
    expect(issues.some((issue) => issue.includes("evidence link is required"))).toBe(true);
  });

  it("rejects a due date before the notice date", () => {
    const issues = validateCapitalCallIssuanceForm({
      ...validForm(),
      noticeDate: "2026-03-15",
      dueDate: "2026-03-01"
    });

    expect(issues).toContain("Capital-call due date cannot precede the notice date.");
  });

  it("rejects a non-GUID ledger book and non-positive amounts", () => {
    const form = validForm();
    const issues = validateCapitalCallIssuanceForm({
      ...form,
      ledgerBookId: "book-1",
      amountToCall: "-5",
      commitments: [{ ...form.commitments[0], totalCommitment: "0" }]
    });

    expect(issues).toContain("Ledger book identifier must be a GUID (find it in the Ledger explorer).");
    expect(issues).toContain("Amount to call must be a positive number.");
    expect(issues).toContain("Commitment line 1: total commitment must be a positive number.");
  });

  it("requires at least one commitment line", () => {
    const issues = validateCapitalCallIssuanceForm({ ...validForm(), commitments: [] });

    expect(issues).toContain("At least one commitment-register line is required.");
  });
});

describe("presentCapitalCallRunOutcome", () => {
  it("presents a blocked run as blocked with every reason verbatim, never success-toned", () => {
    const outcome = presentCapitalCallRunOutcome(blockedRunResult());

    expect(outcome.tone).toBe("danger");
    expect(outcome.title).toBe("Run blocked — no drafts entered the approval queue");
    expect(outcome.blockers).toEqual([
      "Commitment 'commit-lp1' carries no retained commitment-register evidence; the attested total cannot back a capital call.",
      "Capital call 'call-2026-03' is not executable: allocated 0 of requested 250000 across 0 line(s)."
    ]);
  });

  it("presents a ready run with drafts as queued for approval, not posted", () => {
    const outcome = presentCapitalCallRunOutcome(readyRunResult());

    expect(outcome.tone).toBe("success");
    expect(outcome.title).toBe("1 issuance draft queued for approval");
    expect(outcome.detail).toContain("nothing has been posted");
  });

  it("does not celebrate a ready run that created nothing", () => {
    const result = readyRunResult();
    result.intake.created = [];
    result.intake.skipped = [{
      journalEntryId: "00000000-0000-0000-0000-000000000000",
      idempotencyKey: "capital-call|fund-alpha|call-2026-03|commit-lp1",
      reason: "Draft already exists for this event.",
      disposition: 1
    }];

    const outcome = presentCapitalCallRunOutcome(result);

    expect(outcome.tone).toBe("warning");
    expect(outcome.title).toBe("Run completed but created no new drafts");
  });

  it("flags the needs-fix count when created drafts require mapping fixes", () => {
    const result = readyRunResult();
    result.intake.needsFixCount = 1;

    const outcome = presentCapitalCallRunOutcome(result);

    expect(outcome.tone).toBe("success");
    expect(outcome.detail).toContain("1 draft needs account-mapping fixes before submission.");
  });
});

describe("draft and skip presenters", () => {
  it("joins created drafts with their evidence assessments and keeps the Medium first-call warning verbatim", () => {
    const drafts = presentCreatedDrafts(readyRunResult());

    expect(drafts).toHaveLength(1);
    expect(drafts[0].amountLabel).toBe("250,000.00 USD");
    expect(drafts[0].status).toBe("Draft");
    expect(drafts[0].investorId).toBe("lp-1");
    expect(drafts[0].assessment?.quality).toBe("Medium");
    expect(drafts[0].assessment?.confidenceLabel).toBe("80%");
    expect(drafts[0].assessment?.reasons).toEqual([
      "No posted private-capital activity exists for capital account 'cap-lp1'; the operator-attested commitment register is the sole basis for this first call."
    ]);
  });

  it("surfaces the run-level assessment of a blocked run", () => {
    const assessments = presentRunLevelAssessments(blockedRunResult());

    expect(assessments).toHaveLength(1);
    expect(assessments[0].quality).toBe("Low");
    expect(assessments[0].requiresInvestigation).toBe(true);
  });

  it("lists producer and intake skips with the server reason", () => {
    const result = blockedRunResult();
    const skips = presentSkips(result);

    expect(skips).toEqual([
      { subject: "commit-lp2", reason: "Commitment 'commit-lp2' is not callable." }
    ]);
  });
});

describe("useCapitalCallIssuanceViewModel", () => {
  function fillValidForm(view: { result: { current: ReturnType<typeof useCapitalCallIssuanceViewModel> } }) {
    const source = validForm();
    act(() => {
      view.result.current.updateField("fundProfileId", source.fundProfileId);
      view.result.current.updateField("ledgerBookId", source.ledgerBookId);
      view.result.current.updateField("currency", source.currency);
      view.result.current.updateField("callId", source.callId);
      view.result.current.updateField("amountToCall", source.amountToCall);
      view.result.current.updateField("noticeDate", source.noticeDate);
      view.result.current.updateField("dueDate", source.dueDate);
      view.result.current.updateField("periodId", source.periodId);
      view.result.current.updateField("purpose", source.purpose);
      view.result.current.updateField("allocationBasis", source.allocationBasis);
    });
    const rowKey = view.result.current.form.commitments[0].key;
    act(() => {
      view.result.current.updateCommitment(rowKey, "commitmentId", "commit-lp1");
      view.result.current.updateCommitment(rowKey, "capitalAccountId", "cap-lp1");
      view.result.current.updateCommitment(rowKey, "investorId", "lp-1");
      view.result.current.updateCommitment(rowKey, "totalCommitment", "1000000");
      view.result.current.updateCommitment(rowKey, "commitmentDate", "2025-06-30");
      view.result.current.updateCommitment(rowKey, "evidenceLink", "evidence://register/lp1");
    });
  }

  it("arms on the first submit and only posts on the explicit confirm", async () => {
    const runIntake = vi.fn().mockResolvedValue(readyRunResult());
    const hook = renderHook(() => useCapitalCallIssuanceViewModel({ runIntake }));
    fillValidForm(hook);

    await act(async () => {
      await hook.result.current.submit();
    });
    expect(hook.result.current.armed).toBe(true);
    expect(runIntake).not.toHaveBeenCalled();

    await act(async () => {
      await hook.result.current.submit();
    });
    expect(runIntake).toHaveBeenCalledTimes(1);
    const sentRequest = runIntake.mock.calls[0][0];
    expect(sentRequest.allocationBasis).toBe(1);
    expect(sentRequest.commitments[0].status).toBe(0);
    expect("actor" in sentRequest).toBe(false);

    await waitFor(() => {
      expect(hook.result.current.result).not.toBeNull();
    });
    expect(hook.result.current.armed).toBe(false);
  });

  it("disarms when the form changes after arming", async () => {
    const runIntake = vi.fn().mockResolvedValue(readyRunResult());
    const hook = renderHook(() => useCapitalCallIssuanceViewModel({ runIntake }));
    fillValidForm(hook);

    await act(async () => {
      await hook.result.current.submit();
    });
    expect(hook.result.current.armed).toBe(true);

    act(() => {
      hook.result.current.updateField("amountToCall", "260000");
    });
    expect(hook.result.current.armed).toBe(false);
    expect(runIntake).not.toHaveBeenCalled();
  });

  it("blocks submission and reports issues while the form is invalid", async () => {
    const runIntake = vi.fn();
    const hook = renderHook(() => useCapitalCallIssuanceViewModel({ runIntake }));

    await act(async () => {
      await hook.result.current.submit();
    });

    expect(hook.result.current.armed).toBe(false);
    expect(hook.result.current.validationIssues.length).toBeGreaterThan(0);
    expect(runIntake).not.toHaveBeenCalled();
  });

  it("stores a blocked run result instead of treating it as an error or success", async () => {
    const runIntake = vi.fn().mockResolvedValue(blockedRunResult());
    const hook = renderHook(() => useCapitalCallIssuanceViewModel({ runIntake }));
    fillValidForm(hook);

    await act(async () => {
      await hook.result.current.submit();
    });
    await act(async () => {
      await hook.result.current.submit();
    });

    await waitFor(() => {
      expect(hook.result.current.result).not.toBeNull();
    });
    expect(hook.result.current.submitError).toBeNull();
    expect(presentCapitalCallRunOutcome(hook.result.current.result!).tone).toBe("danger");
  });

  it("surfaces the server's 400 reason when the request is rejected", async () => {
    const runIntake = vi.fn().mockRejectedValue(new ApiError({
      path: "/api/ledger/journal-automation/capital-call-issuance-intake",
      status: 400,
      detail: "Capital-call due date cannot precede the notice date."
    }));
    const hook = renderHook(() => useCapitalCallIssuanceViewModel({ runIntake }));
    fillValidForm(hook);

    await act(async () => {
      await hook.result.current.submit();
    });
    await act(async () => {
      await hook.result.current.submit();
    });

    await waitFor(() => {
      expect(hook.result.current.submitError).not.toBeNull();
    });
    expect(hook.result.current.submitError?.summary).toBe(
      "Capital-call due date cannot precede the notice date."
    );
    expect(hook.result.current.result).toBeNull();
  });
});
