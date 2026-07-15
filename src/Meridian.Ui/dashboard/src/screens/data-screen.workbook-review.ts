import type {
  DataUploadValidationIssue,
  DataUploadWorkbookPreviewResult,
  DataUploadWorkbookSheetPreview
} from "@/types/workstation-3";

/**
 * Pure projection of a workbook preview response into the staged-review surface described by the
 * Excel onboarding brainstorm (Idea 2): one tab per sheet with a status chip, a bounded preview
 * grid, and an issue list addressed by cell. The commit action stays disabled until every
 * non-empty sheet is ready, so the operator always knows what stands between them and a commit.
 */
export type DataUploadWorkbookTone = "success" | "warning" | "danger" | "paper";

export interface DataUploadWorkbookIssueState {
  id: string;
  severity: string;
  tone: "danger" | "warning" | "paper";
  message: string;
  location: string;
}

export interface DataUploadWorkbookPreviewCellState {
  id: string;
  label: string;
  value: string;
}

export interface DataUploadWorkbookPreviewRowState {
  id: string;
  values: DataUploadWorkbookPreviewCellState[];
}

export interface DataUploadWorkbookSheetTabState {
  id: string;
  sheetName: string;
  templateLabel: string;
  domainLabel: string;
  statusLabel: string;
  statusTone: DataUploadWorkbookTone;
  parsedRowCountLabel: string;
  previewHeaders: string[];
  previewRows: DataUploadWorkbookPreviewRowState[];
  issues: DataUploadWorkbookIssueState[];
  errorCount: number;
  warningCount: number;
}

export interface DataUploadWorkbookReviewState {
  hasResult: boolean;
  busy: boolean;
  errorSummary: string | null;
  statusLabel: string;
  statusTone: DataUploadWorkbookTone;
  summary: string;
  nextAction: string;
  retainedPath: string | null;
  sheetTabs: DataUploadWorkbookSheetTabState[];
  crossSheetIssues: DataUploadWorkbookIssueState[];
  errorCount: number;
  warningCount: number;
  commitDisabled: boolean;
  commitDisabledReason: string | null;
}

/**
 * Transient request status for the workbook upload, surfaced by the panel so a failed or
 * in-flight preview is not hidden behind the empty "no workbook previewed" copy.
 */
export interface DataUploadWorkbookRequestStatus {
  busy?: boolean;
  errorSummary?: string | null;
  fileName?: string | null;
}

const emptyWorkbookReviewState: DataUploadWorkbookReviewState = {
  hasResult: false,
  busy: false,
  errorSummary: null,
  statusLabel: "No workbook previewed",
  statusTone: "paper",
  summary: "Upload the onboarding workbook to review each sheet before committing.",
  nextAction: "Download the onboarding workbook, fill in its data tabs, then upload it here.",
  retainedPath: null,
  sheetTabs: [],
  crossSheetIssues: [],
  errorCount: 0,
  warningCount: 0,
  commitDisabled: true,
  commitDisabledReason: "Upload a workbook to review its sheets."
};

function isError(issue: DataUploadValidationIssue): boolean {
  return issue.severity.toLowerCase() === "error";
}

function isWarning(issue: DataUploadValidationIssue): boolean {
  return issue.severity.toLowerCase() === "warning";
}

function issueTone(issue: DataUploadValidationIssue): "danger" | "warning" | "paper" {
  if (isError(issue)) {
    return "danger";
  }

  return isWarning(issue) ? "warning" : "paper";
}

function issueLocation(issue: DataUploadValidationIssue): string {
  if (issue.cellReference && issue.cellReference.trim().length > 0) {
    return issue.cellReference;
  }

  if (issue.sheetName && issue.sheetName.trim().length > 0) {
    return issue.rowNumber != null ? `${issue.sheetName} row ${issue.rowNumber}` : issue.sheetName;
  }

  if (issue.rowNumber != null) {
    return `${issue.field} row ${issue.rowNumber}`;
  }

  return issue.field;
}

function mapIssues(
  issues: DataUploadValidationIssue[],
  keyPrefix: string
): DataUploadWorkbookIssueState[] {
  return issues.map((issue, index) => ({
    id: `${keyPrefix}-${index}`,
    severity: issue.severity,
    tone: issueTone(issue),
    message: issue.message,
    location: issueLocation(issue)
  }));
}

function sheetStatusTone(status: string): DataUploadWorkbookTone {
  switch (status) {
    case "ReadyForReview":
      return "success";
    case "NeedsRepair":
      return "danger";
    case "Empty":
      return "paper";
    default:
      return "warning";
  }
}

function sheetStatusLabel(status: string): string {
  switch (status) {
    case "ReadyForReview":
      return "Ready for review";
    case "NeedsRepair":
      return "Needs repair";
    case "Empty":
      return "Empty";
    default:
      return status;
  }
}

function buildSheetTab(
  sheet: DataUploadWorkbookSheetPreview,
  maxPreviewColumns: number,
  maxPreviewRows: number
): DataUploadWorkbookSheetTabState {
  const previewHeaders = sheet.headers.slice(0, maxPreviewColumns);
  const previewRows = sheet.previewRows.slice(0, maxPreviewRows).map((row, rowIndex) => ({
    id: `${sheet.sheetName}-row-${rowIndex}`,
    values: previewHeaders.map((header) => ({
      id: `${sheet.sheetName}-${header}`,
      label: header,
      value: row[header] ?? ""
    }))
  }));

  return {
    id: sheet.sheetName,
    sheetName: sheet.sheetName,
    templateLabel: sheet.templateLabel ?? "Unmatched sheet",
    domainLabel: sheet.dataDomain ?? "Unknown domain",
    statusLabel: sheetStatusLabel(sheet.status),
    statusTone: sheetStatusTone(sheet.status),
    parsedRowCountLabel:
      sheet.parsedRowCount === sheet.previewRowCount
        ? `${sheet.parsedRowCount.toLocaleString()} rows parsed`
        : `${sheet.parsedRowCount.toLocaleString()} rows parsed, showing first ${sheet.previewRowCount.toLocaleString()}`,
    previewHeaders,
    previewRows,
    issues: mapIssues(sheet.issues, `${sheet.sheetName}-issue`),
    errorCount: sheet.issues.filter(isError).length,
    warningCount: sheet.issues.filter(isWarning).length
  };
}

export function buildDataUploadWorkbookReviewState(
  result: DataUploadWorkbookPreviewResult | null | undefined,
  status: DataUploadWorkbookRequestStatus = {},
  maxPreviewColumns = 6,
  maxPreviewRows = 3
): DataUploadWorkbookReviewState {
  const busy = status.busy === true;
  const errorSummary = status.errorSummary?.trim() ? status.errorSummary.trim() : null;

  if (busy) {
    return {
      ...emptyWorkbookReviewState,
      busy: true,
      statusLabel: "Previewing",
      statusTone: "paper",
      summary: `Previewing ${status.fileName?.trim() || "workbook"}.`,
      commitDisabledReason: "Workbook preview is running."
    };
  }

  if (!result) {
    if (errorSummary) {
      return {
        ...emptyWorkbookReviewState,
        errorSummary,
        statusLabel: "Preview failed",
        statusTone: "danger",
        summary: errorSummary,
        commitDisabledReason: "Resolve the workbook preview error before committing."
      };
    }

    return emptyWorkbookReviewState;
  }

  const sheetTabs = result.sheets.map((sheet) =>
    buildSheetTab(sheet, maxPreviewColumns, maxPreviewRows)
  );
  const crossSheetIssues = mapIssues(result.crossSheetIssues, "cross-sheet-issue");

  const errorCount =
    sheetTabs.reduce((total, tab) => total + tab.errorCount, 0) +
    result.crossSheetIssues.filter(isError).length;
  const warningCount =
    sheetTabs.reduce((total, tab) => total + tab.warningCount, 0) +
    result.crossSheetIssues.filter(isWarning).length;

  const ready = result.status === "ReadyForReview" && errorCount === 0;
  const statusTone: DataUploadWorkbookTone = ready ? "success" : errorCount > 0 ? "danger" : "warning";
  const statusLabel = ready ? "Ready for review" : "Needs repair";

  return {
    hasResult: true,
    busy: false,
    errorSummary: null,
    statusLabel,
    statusTone,
    summary: `${result.sheetCount.toLocaleString()} sheets, ${result.totalParsedRowCount.toLocaleString()} rows parsed from ${result.fileName}.`,
    nextAction: result.nextAction,
    retainedPath: result.retainedPath,
    sheetTabs,
    crossSheetIssues,
    errorCount,
    warningCount,
    commitDisabled: !ready,
    commitDisabledReason: ready
      ? null
      : errorCount > 0
        ? `Resolve ${errorCount.toLocaleString()} blocking issue${errorCount === 1 ? "" : "s"} before committing this workbook.`
        : "Every non-empty sheet must be ready for review before committing."
  };
}
