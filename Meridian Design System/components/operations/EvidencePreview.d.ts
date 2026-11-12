import * as React from "react";
export type EvidenceType = "statement" | "confirmation" | "extract" | "custodian" | "pricing" | "model" | "policy" | "approval" | "correspondence" | "snapshot" | "document" | (string & {});
export interface EvidencePreviewProps extends Omit<React.HTMLAttributes<HTMLElement>, "title"> {
  title: React.ReactNode;
  source?: React.ReactNode;
  sourceSystem?: React.ReactNode;
  documentId?: React.ReactNode;
  type?: EvidenceType;
  page?: React.ReactNode;
  section?: React.ReactNode;
  /** Text excerpt: `before` + highlighted `highlight` + `after`. */
  before?: string;
  highlight?: string;
  after?: string;
  /** Image crop instead of a text excerpt. Requires `imageAlt` or `textEquivalent`. */
  image?: string;
  imageAlt?: string;
  /** The excerpt in words — mandatory whenever evidence is only visual. */
  textEquivalent?: React.ReactNode;
  asOf?: React.ReactNode;
  statementDate?: React.ReactNode;
  receivedAt?: React.ReactNode;
  /** Extraction status, e.g. "Parsed · 3 fields matched" or "Manual entry". */
  extraction?: React.ReactNode;
  extractionStatus?: React.ReactNode;
  completeness?: React.ReactNode;
  confidence?: number;
  /** One of the eleven data conditions. Restricted withholds excerpt, image, text equivalent,
   * extraction details and custom metadata; opening and comparing remain unavailable. */
  condition?: string;
  superseded?: boolean;
  supersededBy?: React.ReactNode;
  /** true, or the redaction description. */
  redacted?: boolean | string;
  /** Retention/export/sensitivity restriction stated on the face of the preview. */
  restriction?: React.ReactNode;
  permissions?: { canOpen?: boolean; canExport?: boolean };
  /** Formal citation line, e.g. "Custodian statement 2026-07 · p.12 · acct 408219". */
  citation?: React.ReactNode;
  metadata?: { label: React.ReactNode; value: React.ReactNode }[];
  /** Zero-based position within the evidence set; renders "Evidence 2 of 6". */
  index?: number;
  total?: number;
  onPrevious?: () => void;
  onNext?: () => void;
  onOpenOriginal?: () => void;
  onCopyCitation?: () => void;
  onCompare?: () => void;
  onLink?: () => void;
  onMarkSuperseded?: () => void;
}
export declare function EvidencePreview(props: EvidencePreviewProps): React.JSX.Element;
