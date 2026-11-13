import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { MetricPassport, validateMetricPassport } from "./metric-passport";
import { apiGetJson } from "@/lib/api";
import type { EvidencePacket, NumberPassportReference } from "@/types";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));
const reference: NumberPassportReference = { schemaVersion: 1, subjectType: "ledger-amount", subjectId: "journal:line:debit",
  scope: { tenantId: "tenant", companyId: "company", fundProfileId: "fund", ledgerBookId: "book", periodId: "period" }, amount: "100.00", currency: "USD" };
const packet = (): EvidencePacket => ({ subject: { subjectKind: "ledger-amount", subjectId: reference.subjectId },
  completeness: { status: "Ready" }, ledgerAmount: { subjectId: reference.subjectId, scope: reference.scope,
    amount: 100, currency: "USD", status: "Ready", evidence: [], warnings: [] } } as unknown as EvidencePacket);

describe("headline figure passports", () => {
  it("shows declared context and missing proof on keyboard focus without an API request", () => {
    vi.mocked(apiGetJson).mockClear();
    render(<MetricPassport metric={{ id: "value", label: "Value", value: "$100", delta: "", tone: "default", basis: "Posted GAAP" }} />);
    const trigger = screen.getByRole("button", { name: "Inspect Value basis and evidence" });
    fireEvent.focus(trigger);
    expect(screen.getByRole("dialog", { name: "Value figure context" })).toHaveTextContent("Posted GAAP");
    expect(screen.getByRole("dialog")).toHaveTextContent("Unverified");
    expect(apiGetJson).not.toHaveBeenCalled();
    fireEvent.keyDown(trigger, { key: "Escape" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });
  it("compares exact decimal identity without rounding the reference", () => {
    expect(validateMetricPassport(reference, packet()).error).toBeNull();
    expect(validateMetricPassport(reference, packet()).proof?.status).toBe("ReviewRequired");
    expect(validateMetricPassport({ ...reference, amount: "100.000000000000000001" }, packet()).error).toContain("differs");
  });
  it("states an undeclared basis in the opened passport", () => {
    vi.mocked(apiGetJson).mockClear();
    render(<MetricPassport metric={{ id: "orders", label: "Orders", value: "2", delta: "", tone: "default" }} />);
    fireEvent.focus(screen.getByRole("button", { name: "Inspect Orders basis and evidence" }));
    const dialog = screen.getByRole("dialog", { name: "Orders figure context" });
    expect(dialog).toHaveTextContent("Basis not declared by source");
    expect(dialog).not.toHaveTextContent("Basis unavailable");
    expect(apiGetJson).not.toHaveBeenCalled();
  });
  it("dismisses hover-only context with Escape without moving focus", () => {
    render(<MetricPassport metric={{ id: "cash", label: "Cash", value: "$100", delta: "", tone: "default" }} />);
    fireEvent.mouseEnter(screen.getByRole("button", { name: "Inspect Cash basis and evidence" }).parentElement!);
    expect(screen.getByRole("dialog", { name: "Cash figure context" })).toBeInTheDocument();
    fireEvent.keyDown(document.body, { key: "Escape" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(document.activeElement).toBe(document.body);
  });
  it("withholds foreign scope, currency and subject packets", () => {
    expect(validateMetricPassport({ ...reference, scope: { ...reference.scope!, tenantId: "foreign" } }, packet()).error).toContain("scope");
    expect(validateMetricPassport({ ...reference, currency: "EUR" }, packet()).error).toContain("currency");
    expect(validateMetricPassport({ ...reference, subjectId: "other" }, packet()).error).toContain("subject");
  });
  it("does not certify generic subjects or ambiguous evidence", () => {
    const generic = packet(); generic.subject.subjectKind = "run";
    expect(validateMetricPassport({ ...reference, subjectType: "run" }, generic).error).toContain("does not verify");
    const ambiguous = packet(); ambiguous.ledgerAmount!.evidence = [{ evidenceId: "a" }, { evidenceId: "a" }] as never;
    expect(validateMetricPassport(reference, ambiguous).error).toContain("ambiguous");
    expect(validateMetricPassport({ ...reference, subjectId: "report:run:amount" }, {
      ...packet(), subject: { ...packet().subject, subjectId: "report:run:amount" }
    }).error).toContain("contributor binding");
  });
});
