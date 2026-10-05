import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { axe } from "jest-axe";
import { LedgerAmountProofDrawer } from "@/components/meridian/proof-drawer";
import { getLedgerAmountProof } from "@/lib/ledger-amount-proof-api";
import type { EvidencePacket } from "@/types";
import { createLedgerAmountProofPacket as packet, ledgerAmountSelection as selection } from "@/test/ledger-amount-proof-fixtures";

vi.mock("@/lib/ledger-amount-proof-api", () => ({ getLedgerAmountProof: vi.fn() }));

describe("scoped ledger amount proof drawer", () => {
  beforeEach(() => vi.resetAllMocks());

  it("loads only the selected stable subject and scope, shows retained evidence and closes on Escape", async () => {
    vi.mocked(getLedgerAmountProof).mockResolvedValue(packet());
    const user = userEvent.setup();
    const onClose = vi.fn();
    const { container } = render(<LedgerAmountProofDrawer selection={selection} onClose={onClose} />);
    expect(await screen.findByRole("link", { name: "Open Retained journal line" })).toHaveAttribute("href", packet().ledgerAmount!.evidence[0]!.route);
    expect(getLedgerAmountProof).toHaveBeenCalledWith(selection, { signal: expect.any(AbortSignal) });
    expect(screen.getByRole("region", { name: `${selection.label} Number Passport` })).toHaveTextContent("tenant-1");
    expect(screen.getByRole("region", { name: "Retained supporting evidence" })).toHaveTextContent("a".repeat(64));
    expect((await axe(container)).violations).toEqual([]);
    await user.keyboard("{Escape}");
    expect(onClose).toHaveBeenCalledOnce();
  });

  it("matches the server canonical scope escaping for special-character fund IDs", async () => {
    const special = { ...selection, fundProfileId: "fund / alpha!'()*" };
    const response = packet(special);
    expect(response.nodes[0]!.artifactRefs[0]!.canonicalSubjectId).toContain("fund%20%2F%20alpha%21%27%28%29%2A:");
    vi.mocked(getLedgerAmountProof).mockResolvedValue(response);
    render(<LedgerAmountProofDrawer selection={special} onClose={vi.fn()} />);
    expect(await screen.findByRole("link", { name: "Open Retained journal line" })).toBeInTheDocument();
  });

  it.each(["fundProfileId", "ledgerBookId", "periodId"] as const)("blocks a same-name same-symbol packet from a foreign %s", async (field) => {
    const response = packet();
    response.ledgerAmount!.scope[field] = "foreign-scope";
    vi.mocked(getLedgerAmountProof).mockResolvedValue(response);
    render(<LedgerAmountProofDrawer selection={selection} onClose={vi.fn()} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("scope does not match");
    expect(screen.queryByText("Retained journal line")).not.toBeInTheDocument();
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it.each(["subject", "amount", "currency", "tenant", "company", "ambiguous", "packet-status"])("blocks invalid %s evidence without displaying it", async (mismatch) => {
    const response = packet();
    if (mismatch === "packet-status") response.completeness.status = "Blocked";
    if (mismatch === "subject") response.ledgerAmount!.subjectId = "same-name-other-posting";
    if (mismatch === "amount") response.ledgerAmount!.amount = 999;
    if (mismatch === "currency") response.ledgerAmount!.currency = "EUR";
    if (mismatch === "tenant") response.ledgerAmount!.scope.tenantId = "";
    if (mismatch === "company") response.ledgerAmount!.scope.companyId = "";
    if (mismatch === "ambiguous") response.ledgerAmount!.evidence.push({ ...response.ledgerAmount!.evidence[0]! });
    vi.mocked(getLedgerAmountProof).mockResolvedValue(response);
    render(<LedgerAmountProofDrawer selection={selection} onClose={vi.fn()} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Blocked:");
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it.each(["missing", "stale", "not-retained", "invalid-timestamp", "future-timestamp", "ledger-only", "blocked"])("shows review or blocked posture for %s evidence", async (condition) => {
    const response = packet();
    if (condition === "missing") response.ledgerAmount!.evidence = [];
    if (condition === "stale") response.ledgerAmount!.evidence[0]!.status = "Stale";
    if (condition === "future-timestamp") response.ledgerAmount!.evidence[0]!.retainedAt = "2099-01-01T00:00:00Z";
    if (condition === "invalid-timestamp") response.ledgerAmount!.evidence[0]!.retainedAt = "not-a-time";
    if (condition === "not-retained") response.ledgerAmount!.evidence[0]!.retainedAt = null;
    if (condition === "ledger-only") response.ledgerAmount!.evidence[0]!.kind = "ledger-record";
    if (condition === "blocked") { response.ledgerAmount!.status = "Blocked"; response.completeness.status = "Blocked"; }
    vi.mocked(getLedgerAmountProof).mockResolvedValue(response);
    render(<LedgerAmountProofDrawer selection={selection} onClose={vi.fn()} />);
    await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent(condition === "blocked" ? "Blocked" : "Review required"));
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it.each(["arbitrary-route", "external-route", "invalid-digest", "missing-guard", "extra-guard", "duplicate-guard", "foreign-fund-guard", "foreign-subject-guard", "wrong-hash-guard", "missing-artifact", "foreign-canonical-subject", "duplicate-artifact", "unretained-artifact", "wrong-artifact-hash", "foreign-node-subject", "malformed-route", "duplicate-node", "missing-nodes", "missing-artifacts"])("blocks a same-scope payload with %s", async (condition) => {
    const response = packet();
    const item = response.ledgerAmount!.evidence[0]!;
    const node = response.nodes[0]!;
    const artifact = node.artifactRefs[0]!;
    if (condition === "arbitrary-route") item.route = "/retained/unrelated-document";
    if (condition === "external-route") item.route = `https://foreign.example${item.route}`;
    if (condition === "invalid-digest") item.contentHash = "sha256:display-label";
    const url = new URL(item.route!, "https://meridian.invalid");
    if (condition === "missing-guard") url.searchParams.delete("periodId");
    if (condition === "extra-guard") url.searchParams.set("unrelatedCase", "case-2");
    if (condition === "duplicate-guard") url.searchParams.append("fundProfileId", selection.fundProfileId);
    if (condition === "foreign-fund-guard") url.searchParams.set("fundProfileId", "fund-2");
    if (condition === "foreign-subject-guard") url.searchParams.set("ledgerAmountSubjectId", "foreign-journal:foreign-line:debit");
    if (condition === "wrong-hash-guard") url.searchParams.set("expectedContentHash", "b".repeat(64));
    if (condition.endsWith("-guard")) item.route = `${url.pathname}${url.search}`;
    if (condition === "malformed-route") item.route = "http://[";
    artifact.route = item.route;
    if (condition === "missing-artifact") node.artifactRefs = [];
    if (condition === "foreign-canonical-subject") artifact.canonicalSubjectId = `foreign-fund:${artifact.canonicalSubjectId}`;
    if (condition === "duplicate-artifact") node.artifactRefs.push({ ...artifact });
    if (condition === "unretained-artifact") artifact.retained = false;
    if (condition === "wrong-artifact-hash") artifact.hash = "b".repeat(64);
    if (condition === "duplicate-node") response.nodes.push({ ...node, artifactRefs: [] });
    if (condition === "missing-nodes") response.nodes = undefined as unknown as EvidencePacket["nodes"];
    if (condition === "missing-artifacts") node.artifactRefs = undefined as unknown as EvidencePacket["nodes"][number]["artifactRefs"];
    if (condition === "foreign-node-subject") node.subject = { ...node.subject, subjectId: "foreign-subject" };
    vi.mocked(getLedgerAmountProof).mockResolvedValue(response);
    render(<LedgerAmountProofDrawer selection={selection} onClose={vi.fn()} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Blocked: supporting evidence");
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
    expect(screen.queryByText("Retained journal line")).not.toBeInTheDocument();
  });

  it("blocks missing subject payloads and failed requests", async () => {
    const response = packet();
    response.ledgerAmount = null;
    vi.mocked(getLedgerAmountProof).mockResolvedValueOnce(response).mockRejectedValueOnce(new Error("404"));
    const { rerender } = render(<LedgerAmountProofDrawer selection={selection} onClose={vi.fn()} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Blocked:");
    rerender(<LedgerAmountProofDrawer selection={{ ...selection, subjectId: "missing-posting" }} onClose={vi.fn()} />);
    expect(await screen.findByRole("alert")).toHaveTextContent("unavailable");
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("hides previous proof immediately and ignores a late response after same-name cross-fund selection", async () => {
    let resolveOld!: (value: EvidencePacket) => void;
    const second = { ...selection, subjectId: "44444444-4444-4444-4444-444444444444:55555555-5555-5555-5555-555555555555:debit", fundProfileId: "fund-2", ledgerBookId: "00000000-0000-0000-0000-0000000000bb" };
    const secondPacket = packet(second);
    secondPacket.ledgerAmount!.evidence[0]!.label = "Fund two retained proof";
    vi.mocked(getLedgerAmountProof).mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve; })).mockResolvedValueOnce(secondPacket);
    const { rerender } = render(<LedgerAmountProofDrawer selection={selection} onClose={vi.fn()} />);
    rerender(<LedgerAmountProofDrawer selection={second} onClose={vi.fn()} />);
    expect(await screen.findByRole("link", { name: "Open Fund two retained proof" })).toBeInTheDocument();
    await act(async () => resolveOld(packet()));
    expect(screen.queryByText("Retained journal line")).not.toBeInTheDocument();
    expect(screen.getByRole("region", { name: `${selection.label} Number Passport` })).toHaveTextContent("fund-2");
  });
});
