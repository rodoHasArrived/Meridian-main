/**
 * Validation issue list — the recurring Meridian `{ code, severity, message }` shape
 * (ExtensibilityValidationIssue, EvidenceValidationIssue, AccountingConfigurationValidationIssue,
 * ProviderIntegrationValidationIssue, …). Each issue is a compact row: a severity dot,
 * mono code, message, and an optional gate tag. Validation severities `Info | Warning |
 * Critical` map to info · action · blocked.
 *
 * @example
 * <ValidationIssueList issues={[
 *   { code: "RECON-204", severity: "Critical", message: "Custody cash break exceeds tolerance.", gate: "Reconciliation" },
 *   { code: "MAP-118",   severity: "Warning",  message: "Provider field 'cusip' unmapped for 12 rows.", gate: "SecurityMaster" },
 *   { code: "EVID-007",  severity: "Info",     message: "Approval evidence pack not yet generated." },
 * ]} />
 */
export interface ValidationIssue {
  /** Stable issue code (mono). */
  code?: string;
  /** Severity string — Info | Warning | Critical (or any readiness status). */
  severity: string;
  /** Human-readable issue message. */
  message: React.ReactNode;
  /** Optional gate / scope tag shown at the row end. */
  gate?: React.ReactNode;
}
export interface ValidationIssueListProps {
  issues: ValidationIssue[];
  /** Tighter row padding. @default false */
  dense?: boolean;
  /** Shown when `issues` is empty. @default "No issues" */
  emptyLabel?: React.ReactNode;
  className?: string;
}
export declare function ValidationIssueList(props: ValidationIssueListProps): JSX.Element;
