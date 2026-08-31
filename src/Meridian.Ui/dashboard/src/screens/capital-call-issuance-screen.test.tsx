import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import * as capitalCallApi from "@/lib/api/capital-call-issuance.api";
import type { CapitalCallIssuanceIntakeRunResult } from "@/lib/api/capital-call-issuance.api";
import { CapitalCallIssuanceScreen } from "@/screens/capital-call-issuance-screen";
import type { ManualJournalEntryDraft } from "@/types";

vi.mock("@/lib/api/capital-call-issuance.api", async () => {
  const actual = await vi.importActual<typeof capitalCallApi>("@/lib/api/capital-call-issuance.api");
  return {
    ...actual,
    runCapitalCallIssuanceIntake: vi.fn()
  };
});

const runIntakeMock = vi.mocked(capitalCallApi.runCapitalCallIssuanceIntake);

const LEDGER_BOOK_ID = "3f7c8a52-1a44-4f4e-9a2b-6f0d5a7c9e11";

const FIRST_CALL_WARNING =
  "No posted private-capital activity exists for capital account 'cap-lp1'; the operator-attested commitment register is the sole basis for this first call.";

function createdDraft(): ManualJournalEntryDraft {
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
    }
  };
}

function readyRunResult(): CapitalCallIssuanceIntakeRunResult {
  return {
    producerSkips: [],
    intake: { created: [createdDraft()], skipped: [], needsFixCount: 0 },
    evidenceAssessments: {
      "capital-call|fund-alpha|call-2026-03|commit-lp1": {
        assessmentCode: "capital-call-commitment-corroboration",
        confidenceScore: 0.8,
        quality: "Medium",
        requiresInvestigation: false,
        summary: "Server-recomputed uncalled commitment 1000000.00 USD for 'commit-lp1' from 0 posted fund-event step(s); call 250000.00 is 25.00% of the requested 250000.00.",
        reasons: [FIRST_CALL_WARNING],
        evidenceLinks: ["evidence://register/lp1"]
      }
    },
    readiness: 0,
    readinessBlockers: []
  };
}

function blockedRunResult(): CapitalCallIssuanceIntakeRunResult {
  return {
    producerSkips: [],
    intake: { created: [], skipped: [], needsFixCount: 0 },
    evidenceAssessments: {},
    readiness: 2,
    readinessBlockers: [
      "Commitment 'commit-lp1' carries no retained commitment-register evidence; the attested total cannot back a capital call."
    ]
  };
}

function renderScreen() {
  return render(
    <MemoryRouter initialEntries={["/accounting/capital-calls"]}>
      <CapitalCallIssuanceScreen />
    </MemoryRouter>
  );
}

function fillValidForm() {
  fireEvent.change(screen.getByLabelText("Fund profile id"), { target: { value: "fund-alpha" } });
  fireEvent.change(screen.getByLabelText("Ledger book id (GUID)"), { target: { value: LEDGER_BOOK_ID } });
  fireEvent.change(screen.getByLabelText("Currency (ISO)"), { target: { value: "USD" } });
  fireEvent.change(screen.getByLabelText("Capital-call id"), { target: { value: "call-2026-03" } });
  fireEvent.change(screen.getByLabelText("Amount to call"), { target: { value: "250000" } });
  fireEvent.change(screen.getByLabelText("Notice date"), { target: { value: "2026-03-01" } });
  fireEvent.change(screen.getByLabelText("Due date"), { target: { value: "2026-03-15" } });
  fireEvent.change(screen.getByLabelText("Commitment id"), { target: { value: "commit-lp1" } });
  fireEvent.change(screen.getByLabelText("Capital account id"), { target: { value: "cap-lp1" } });
  fireEvent.change(screen.getByLabelText("Investor id"), { target: { value: "lp-1" } });
  fireEvent.change(screen.getByLabelText("Total commitment"), { target: { value: "1000000" } });
  fireEvent.change(screen.getByLabelText("Commitment date"), { target: { value: "2025-06-30" } });
  fireEvent.change(screen.getByLabelText("Evidence link"), { target: { value: "evidence://register/lp1" } });
}

describe("CapitalCallIssuanceScreen", () => {
  beforeEach(() => {
    runIntakeMock.mockReset();
  });

  it("shows validation issues instead of posting when the form is incomplete", async () => {
    renderScreen();

    fireEvent.click(screen.getByRole("button", { name: "Queue issuance drafts" }));

    const issues = await screen.findByTestId("capital-call-validation-issues");
    expect(issues).toHaveTextContent("Fund profile identifier is required.");
    expect(issues).toHaveTextContent("Ledger book is required — without it the drafts land in the queue as book-missing.");
    expect(runIntakeMock).not.toHaveBeenCalled();
  });

  it("requires the armed confirm before the intake is posted", async () => {
    runIntakeMock.mockResolvedValue(readyRunResult());
    renderScreen();
    fillValidForm();

    fireEvent.click(screen.getByRole("button", { name: "Queue issuance drafts" }));

    // First activation arms; nothing is sent yet.
    const confirmButton = await screen.findByRole("button", { name: "Confirm — queue issuance drafts" });
    expect(runIntakeMock).not.toHaveBeenCalled();
    expect(screen.getByText(/Nothing is posted\. Select Confirm to proceed\./)).toBeInTheDocument();

    fireEvent.click(confirmButton);

    await waitFor(() => {
      expect(runIntakeMock).toHaveBeenCalledTimes(1);
    });
    const request = runIntakeMock.mock.calls[0][0];
    expect(request.allocationBasis).toBe(0);
    expect(request.commitments[0].status).toBe(0);
    expect(request.ledgerBookId).toBe(LEDGER_BOOK_ID);
    expect("actor" in request).toBe(false);
  });

  it("renders created drafts with amounts and the Medium first-call warning verbatim", async () => {
    runIntakeMock.mockResolvedValue(readyRunResult());
    renderScreen();
    fillValidForm();

    fireEvent.click(screen.getByRole("button", { name: "Queue issuance drafts" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm — queue issuance drafts" }));

    const created = await screen.findByTestId("capital-call-created-drafts");
    expect(created).toHaveTextContent("Capital call call-2026-03 — LP 1");
    expect(created).toHaveTextContent("250,000.00 USD");
    expect(created).toHaveTextContent("Evidence Medium");
    expect(created).toHaveTextContent(FIRST_CALL_WARNING);
    expect(screen.getByText("1 issuance draft queued for approval")).toBeInTheDocument();
    expect(screen.getByText(/nothing has been posted/)).toBeInTheDocument();
  });

  it("renders a blocked run as blocked with every server reason verbatim", async () => {
    runIntakeMock.mockResolvedValue(blockedRunResult());
    renderScreen();
    fillValidForm();

    fireEvent.click(screen.getByRole("button", { name: "Queue issuance drafts" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm — queue issuance drafts" }));

    expect(await screen.findByText("Run blocked — no drafts entered the approval queue")).toBeInTheDocument();
    const blockers = screen.getByTestId("capital-call-blockers");
    expect(blockers).toHaveTextContent(
      "Commitment 'commit-lp1' carries no retained commitment-register evidence; the attested total cannot back a capital call."
    );
    expect(screen.queryByTestId("capital-call-created-drafts")).not.toBeInTheDocument();
    expect(screen.queryByText(/queued for approval/)).not.toBeInTheDocument();
  });

  it("adds and removes commitment lines", async () => {
    renderScreen();

    fireEvent.click(screen.getByRole("button", { name: /Add commitment/ }));
    expect(screen.getByTestId("commitment-row-1")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Remove commitment line 2" }));
    await waitFor(() => {
      expect(screen.queryByTestId("commitment-row-1")).not.toBeInTheDocument();
    });
  });

  it("shows the server 400 reason as an error state", async () => {
    const { ApiError } = await import("@/lib/api-errors");
    runIntakeMock.mockRejectedValue(new ApiError({
      path: "/api/ledger/journal-automation/capital-call-issuance-intake",
      status: 400,
      detail: "Amount to call must be positive."
    }));
    renderScreen();
    fillValidForm();

    fireEvent.click(screen.getByRole("button", { name: "Queue issuance drafts" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm — queue issuance drafts" }));

    const alerts = await screen.findAllByRole("alert");
    expect(alerts.some((alert) => alert.textContent?.includes("Amount to call must be positive."))).toBe(true);
  });
});
