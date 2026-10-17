import { apiGetJson, apiPostJson, getLedgerCloseManagementPeriodPlan, getOperationsContinuityWorkflows } from "@/lib/api";
import { captureClosePlanTemplate, createPreparedClosePlan, getClosePreparationBooks, getClosePreparationPeriods, getClosePreparationSource, listClosePlanTemplates, listClosePreparationSources, previewClosePreparation } from "./close-preparation.api";
import { preparationPreview, retainedTemplate, sourcePlan, targetBook, targetPeriod } from "@/screens/accounting-screen.prepare-next-period.test-fixtures";

vi.mock("@/lib/api", () => ({ apiGetJson: vi.fn(), apiPostJson: vi.fn(), getLedgerCloseManagementPeriodPlan: vi.fn(), getOperationsContinuityWorkflows: vi.fn() }));
beforeEach(() => vi.resetAllMocks());
const previewRequest = { templateId: retainedTemplate.templateId, templateVersion: 3, targetLedgerBookId: targetBook.ledgerBookId, targetPeriodId: targetPeriod.periodId,
  ownerMappings: [{ sourceOwner: "Alex", targetOwner: "Sam" }], acknowledgePolicyChange: true };

it("loads authoritative source identity and retains read failures without development fallback", async () => {
  vi.mocked(getLedgerCloseManagementPeriodPlan).mockResolvedValue(sourcePlan);
  await listClosePreparationSources();
  await getClosePreparationSource(sourcePlan.workflowId!);
  expect(getOperationsContinuityWorkflows).toHaveBeenCalledWith({}, expect.objectContaining({ allowDevelopmentFallback: false }));
  expect(getLedgerCloseManagementPeriodPlan).toHaveBeenCalledWith(sourcePlan.workflowId, expect.objectContaining({ allowDevelopmentFallback: false }));
  vi.mocked(getLedgerCloseManagementPeriodPlan).mockResolvedValue({ ...sourcePlan, workflowId: "wrong-source" });
  await expect(getClosePreparationSource(sourcePlan.workflowId!)).rejects.toThrow("does not match");
  vi.mocked(getLedgerCloseManagementPeriodPlan).mockRejectedValue(new Error("Unavailable"));
  await expect(getClosePreparationSource(sourcePlan.workflowId!)).rejects.toThrow("Unavailable");
});

it("derives target-book fund scope from the authoritative source book", async () => {
  vi.mocked(apiGetJson).mockResolvedValueOnce(targetBook).mockResolvedValueOnce([targetBook]);
  await getClosePreparationBooks(targetBook.ledgerBookId);
  expect(apiGetJson).toHaveBeenNthCalledWith(1, `/api/ledger/books/${targetBook.ledgerBookId}`, expect.objectContaining({ allowDevelopmentFallback: false }));
  expect(apiGetJson).toHaveBeenNthCalledWith(2, "/api/ledger/books?fundProfileId=actual-fund", expect.objectContaining({ allowDevelopmentFallback: false }));
});

it("rejects target books or periods outside the selected scope", async () => {
  vi.mocked(apiGetJson).mockResolvedValueOnce(targetBook).mockResolvedValueOnce([{ ...targetBook, fundProfileId: "other" }]);
  await expect(getClosePreparationBooks(targetBook.ledgerBookId)).rejects.toThrow("do not match");
  vi.mocked(apiGetJson).mockResolvedValueOnce([{ ...targetPeriod, ledgerBookId: "other" }]);
  await expect(getClosePreparationPeriods(targetBook.ledgerBookId)).rejects.toThrow("do not match");
});

it("reads retained versions only for the selected source", async () => {
  vi.mocked(apiGetJson).mockResolvedValueOnce([retainedTemplate]);
  await listClosePlanTemplates(retainedTemplate.sourceWorkflowId);
  expect(apiGetJson).toHaveBeenCalledWith(`/api/ledger/close-management/templates?sourceWorkflowId=${retainedTemplate.sourceWorkflowId}`, expect.objectContaining({ allowDevelopmentFallback: false }));
  vi.mocked(apiGetJson).mockResolvedValueOnce([{ ...retainedTemplate, sourceWorkflowId: "other" }]);
  await expect(listClosePlanTemplates(retainedTemplate.sourceWorkflowId)).rejects.toThrow("do not match");
});

it("uses authoritative capture, preview and creation endpoints", async () => {
  vi.mocked(apiPostJson).mockResolvedValue(preparationPreview);
  const capture = { sourceWorkflowId: retainedTemplate.sourceWorkflowId, name: retainedTemplate.name, calendar: retainedTemplate.calendar, deadlineRules: retainedTemplate.tasks.map(task => task.deadlineRule) };
  await captureClosePlanTemplate(capture);
  await previewClosePreparation(previewRequest);
  await createPreparedClosePlan({ previewId: preparationPreview.previewId, idempotencyKey: "retry-same" });
  expect(apiPostJson).toHaveBeenNthCalledWith(1, "/api/ledger/close-management/templates", capture, expect.objectContaining({ allowDevelopmentFallback: false }));
  expect(apiPostJson).toHaveBeenNthCalledWith(2, "/api/ledger/close-management/prepare/preview", previewRequest, expect.objectContaining({ allowDevelopmentFallback: false }));
  expect(apiPostJson).toHaveBeenNthCalledWith(3, "/api/ledger/close-management/prepare/create", { previewId: preparationPreview.previewId, idempotencyKey: "retry-same" }, expect.objectContaining({ allowDevelopmentFallback: false }));
});

it.each([
  { templateVersion: 99 }, { templateId: "other" }, { targetBook: { ...targetBook, ledgerBookId: "other" } },
  { targetPeriod: { ...targetPeriod, periodId: "other" } }, { targetPeriod: { ...targetPeriod, ledgerBookId: "other" } },
])("rejects a mismatched preparation response %j", async mismatch => {
  vi.mocked(apiPostJson).mockResolvedValueOnce({ ...preparationPreview, ...mismatch });
  await expect(previewClosePreparation(previewRequest)).rejects.toThrow("does not match");
});
