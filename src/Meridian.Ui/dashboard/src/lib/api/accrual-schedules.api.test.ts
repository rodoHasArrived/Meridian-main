import { beforeEach, describe, expect, it, vi } from "vitest";
import { configureAccrualSchedule, previewAccrualSchedule, runAccrualSchedule, type AccrualScheduleInput } from "./accrual-schedules.api";
import * as api from "@/lib/api";
vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn() }));
const input: AccrualScheduleInput = { scheduleId: "fees", fundProfileId: "fund", ledgerBookId: "book", entityId: "entity", currency: "USD", cadence: "Monthly", anchorDate: "2026-10-01", endsOn: "2026-11-01", amount: "1.123456789123456789", debitAccount: "Expense:Fees", debitAccountType: "Expense", creditAccount: "Liability:Accruals", creditAccountType: "Liability", description: "Accrual", evidence: [], expectedScheduleVersion: 0, expectedTemplateVersion: 0 };
beforeEach(() => vi.resetAllMocks());
describe("accrual API exact contract", () => {
  it("sends decimal text and refuses numeric preview amounts", async () => {
    const result = { fingerprint: "a".repeat(64), currency: "USD", occurrences: [{ amount: input.amount, debitAccount: input.debitAccount, creditAccount: input.creditAccount }] };
    vi.mocked(api.apiPostJson).mockResolvedValue(result);
    await previewAccrualSchedule(input);
    expect(api.apiPostJson).toHaveBeenCalledWith(expect.any(String), input, expect.objectContaining({ allowDevelopmentFallback: false }));
    vi.mocked(api.apiPostJson).mockResolvedValue({ ...result, occurrences: [{ ...result.occurrences[0], amount: 1.12 }] });
    await expect(previewAccrualSchedule(input)).rejects.toThrow(/exact decimal contract/);
  });
  it("binds retention to preview and run to one version and scope", async () => {
    vi.mocked(api.apiPostJson).mockResolvedValue({});
    await configureAccrualSchedule(input, "fingerprint");
    expect(api.apiPostJson).toHaveBeenLastCalledWith(expect.any(String), { input, previewFingerprint: "fingerprint" }, expect.objectContaining({ allowDevelopmentFallback: false }));
    const scope = { fundProfileId: "fund", ledgerBookId: "book", entityId: "entity" };
    await runAccrualSchedule("accrual:fees", scope, 2);
    expect(api.apiPostJson).toHaveBeenLastCalledWith(expect.stringContaining("accrual%3Afees/run"), { ...scope, expectedVersion: 2 }, expect.objectContaining({ allowDevelopmentFallback: false }));
  });
});
