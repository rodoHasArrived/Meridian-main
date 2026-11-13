/** Immutable market inputs. Forward values are annual fractions; Discount values are factors. */
export type SecurityCurveKind = "Discount" | "Forward" | "Inflation";
export interface SecurityCurvePoint { date: string; value: string | number; tenor?: string | null }
export interface SecurityCurveDefinition {
  curveId: string; curveKey: string; kind: SecurityCurveKind; currency: string;
  indexName?: string | null; tenor?: string | null; dayCount: string; interpolation: "Linear";
  source: string; description?: string | null; createdBy: string; createdAt: string;
}
export interface SecurityCurveVersion {
  curveVersionId: string; curveId: string; curveKey: string; version: number; asOf: string;
  recordedAt: string; sourcedAt?: string | null; source: string; publishedBy: string;
  points: SecurityCurvePoint[]; contentSha256: string;
}
export type DefineSecurityCurveRequest = Omit<SecurityCurveDefinition, "curveId" | "createdBy" | "createdAt">;
export interface PublishSecurityCurveRequest { curveId: string; asOf: string; points: SecurityCurvePoint[]; source: string; sourcedAt?: string | null }
export interface ExactCurvePin { role: string; curveId: string; curveVersionId: string }
