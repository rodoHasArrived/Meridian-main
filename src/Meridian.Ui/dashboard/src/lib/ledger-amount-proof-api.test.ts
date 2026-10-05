import { apiGetJson } from "@/lib/api";
import { getLedgerAmountProof } from "@/lib/ledger-amount-proof-api";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn() }));

it("addresses the exact posting subject with book, period and fund scope and prohibits demo fallback", () => {
  const signal = new AbortController().signal;
  getLedgerAmountProof({ subjectId: "journal:entry/debit:debit", ledgerBookId: "book-1", periodId: "period-1", fundProfileId: "fund & two", amount: 50, currency: "USD", label: "Cash AAPL" }, { signal, allowDevelopmentFallback: true });
  expect(apiGetJson).toHaveBeenCalledWith(
    "/api/workstation/evidence/subjects/ledger-amount/journal%3Aentry%2Fdebit%3Adebit/packet?ledgerBookId=book-1&periodId=period-1&fundProfileId=fund+%26+two",
    { signal, allowDevelopmentFallback: false }
  );
});
