import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AccountingPreparerAttestation } from "./accounting-preparer-attestation";
import { PREPARER_ATTESTATION_DISCLOSURE } from "@/lib/single-principal-attestation-api";
import { CASH_DIVIDEND_ATTESTATION_POLICY } from "@/lib/cash-dividend-attestation-api";
import { createLedgerAmountProofPacket } from "@/test/ledger-amount-proof-fixtures";
import type { AccountingPreparerAttestationDisclosure, LedgerAmountProof } from "@/types/ledger-amount-proof";

function amount(): LedgerAmountProof {
  const proof = createLedgerAmountProofPacket().ledgerAmount!;
  const decision: AccountingPreparerAttestationDisclosure = { schemaVersion: 1, classification: "PreparerAttestation",
    disclosure: PREPARER_ATTESTATION_DISCLOSURE, sourceScope: { ...proof.scope },
    journalEntryIds: [proof.subjectId.split(":")[0]!], validationIssues: [], attestationId: "attestation-one",
    caseId: "case-one", projectionId: "projection-one", eventId: "event-one", eventVersion: 1,
    draftedCandidateFingerprint: "b".repeat(64), postingIntentHash: "c".repeat(64),
    actor: "preparer-one", recordedAt: "2026-11-12T12:00:00Z",
    policyVersion: CASH_DIVIDEND_ATTESTATION_POLICY, policyHash: "a".repeat(64), declarationId: "declaration-one", declarationRevision: 2 };
  return { ...proof, ordinaryRecordedApprovalSatisfied: false, accountingPreparerAttestations: [decision] };
}

describe("native accounting preparer classification", () => {
  it("discloses the native decision beside the amount without claiming ordinary approval", () => {
    render(<AccountingPreparerAttestation proof={amount()} />);
    expect(screen.getByText(PREPARER_ATTESTATION_DISCLOSURE)).toBeInTheDocument();
    expect(screen.getByText(/does not satisfy the ordinary-approval requirement for Verified Coverage/)).toBeInTheDocument();
    expect(screen.getByText("attestation-one")).toBeInTheDocument();
    expect(screen.queryByText(/entitlementSnapshotJson/)).not.toBeInTheDocument();
  });

  it.each(["schema", "classification", "tenant", "company", "book", "fund", "period", "journal", "policy", "incomplete", "event", "fingerprint", "intent", "validation"])("keeps %s mismatch unclassified", condition => {
    const proof = amount(); const decision = proof.accountingPreparerAttestations![0]!;
    if (condition === "schema") decision.schemaVersion = 99;
    if (condition === "classification") decision.classification = "IndependentReview";
    if (condition === "tenant") decision.sourceScope.tenantId = "foreign";
    if (condition === "company") decision.sourceScope.companyId = "foreign";
    if (condition === "book") decision.sourceScope.ledgerBookId = "foreign";
    if (condition === "fund") decision.sourceScope.fundProfileId = "foreign";
    if (condition === "period") decision.sourceScope.periodId = "foreign";
    if (condition === "journal") decision.journalEntryIds = ["unrelated-journal"];
    if (condition === "policy") decision.policyVersion = "future-policy";
    if (condition === "incomplete") decision.declarationId = null;
    if (condition === "event") decision.eventVersion = 0;
    if (condition === "fingerprint") decision.draftedCandidateFingerprint = null;
    if (condition === "intent") decision.postingIntentHash = "unsupported";
    if (condition === "validation") decision.validationIssues = ["Native decision validation unavailable"];
    render(<AccountingPreparerAttestation proof={proof} />);
    expect(screen.getByText("Accounting review classification unavailable")).toBeInTheDocument();
    expect(screen.queryByText(PREPARER_ATTESTATION_DISCLOSURE)).not.toBeInTheDocument();
  });

  it("retains earlier posting periods for report amounts with exact contributor identity", () => {
    const proof = amount(); const decision = proof.accountingPreparerAttestations![0]!;
    proof.subjectId = "report:retained-run:cash-account";
    decision.sourceScope.periodId = "earlier-retained-period";
    render(<AccountingPreparerAttestation proof={proof} sourceJournalEntryIds={decision.journalEntryIds} />);
    expect(screen.getByText(PREPARER_ATTESTATION_DISCLOSURE)).toBeInTheDocument();
  });

  it("never treats a positive legacy flag as an independent review of an attested dependency", () => {
    const proof = amount(); proof.ordinaryRecordedApprovalSatisfied = true;
    render(<AccountingPreparerAttestation proof={proof} />);
    expect(screen.getByText(PREPARER_ATTESTATION_DISCLOSURE)).toBeInTheDocument();
    expect(screen.getByText(/does not satisfy the ordinary-approval requirement/)).toBeInTheDocument();
  });

  it("distinguishes unknown approval details from an ordinary-approved claim", () => {
    const proof = amount(); proof.accountingPreparerAttestations = null;
    render(<AccountingPreparerAttestation proof={proof} />);
    expect(screen.getByText(/Ordinary recorded approval is not established/)).toBeInTheDocument();
  });

  it("leaves absent legacy data without an invented classification", () => {
    const { container } = render(<AccountingPreparerAttestation proof={createLedgerAmountProofPacket().ledgerAmount!} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("uses phrasing content in inline passports", () => {
    const { container } = render(<span><AccountingPreparerAttestation proof={amount()} inline /></span>);
    expect(screen.getByRole("group", { name: "Accounting preparer decision" })).toHaveTextContent(PREPARER_ATTESTATION_DISCLOSURE);
    expect(container.querySelector("section, div, p, dl")).toBeNull();
  });
});
