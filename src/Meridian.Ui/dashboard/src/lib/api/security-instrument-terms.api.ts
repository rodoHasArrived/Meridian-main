import { apiGetJson, type ApiRequestOptions } from "@/lib/api";
import { securityInstrumentTermsEndpoint } from "@/lib/workstation-endpoints";
import type { SecurityInstrumentTerms } from "@/types/security-instrument-terms";

export interface SecurityInstrumentTermsRequestOptions extends ApiRequestOptions {
  expectedVersion?: number;
  expectedFingerprint?: string;
}

function record(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

const text = (value: unknown): value is string => typeof value === "string";
const nullableText = (value: unknown) => value === null || text(value);
const version = (value: unknown) => Number.isSafeInteger(value) && (value as number) >= 0;
const oneOf = (value: unknown, allowed: readonly string[]) => text(value) && allowed.includes(value);

/** Validate the transport shape without interpreting or normalizing retained financial strings. */
function validTerms(value: unknown): value is SecurityInstrumentTerms {
  if (!record(value) || !record(value.schema) || !record(value.source)) return false;
  const schema = value.schema;
  const source = value.source;
  if (
    ![
      value.securityId,
      value.assetClass,
      value.currency,
      value.retrievedAtUtc,
      value.fingerprint,
      value.termsFormat
    ].every(text) ||
    !version(value.version) ||
    !version(value.assetClassTermsVersion) ||
    !nullableText(value.effectiveFrom) ||
    !nullableText(value.effectiveTo) ||
    !oneOf(value.status, ["Ready", "ReviewRequired", "Unsupported"]) ||
    !/^[a-f0-9]{64}$/i.test(value.fingerprint as string) ||
    !Number.isFinite(Date.parse(value.retrievedAtUtc as string)) ||
    !text(schema.assetClass) ||
    !version(schema.writeVersion) ||
    !version(schema.readVersion) ||
    ![schema.lotUnit, schema.cashFlowBehavior, schema.profileId, schema.profileStatus].every(nullableText) ||
    !(schema.profileVersion === null || version(schema.profileVersion)) ||
    !oneOf(source.status, ["Matched", "Unavailable", "VersionMismatch"]) ||
    !text(source.summary) ||
    ![source.provenanceJson, value.commonTermsJson, value.assetSpecificTermsJson].every(nullableText) ||
    !Array.isArray(value.diagnostics) ||
    !value.diagnostics.every(text) ||
    !Array.isArray(value.fields)
  )
    return false;

  let count = 0;
  const fieldIsValid = (field: unknown, depth: number): boolean => {
    if (++count > 20_000 || depth > 64 || !record(field)) return false;
    return (
      [field.path, field.key, field.label, field.group, field.jsonType].every(text) &&
      (field.path as string).startsWith("/") &&
      [
        field.declaredType,
        field.role,
        field.valueText,
        field.rawJson,
        field.unit,
        field.unitPath,
        field.validationMessage
      ].every(nullableText) &&
      oneOf(field.state, ["Present", "Missing", "Null", "Invalid", "Ambiguous"]) &&
      oneOf(field.unitStatus, ["Explicit", "Unspecified", "NotApplicable"]) &&
      typeof field.isDeclared === "boolean" &&
      typeof field.required === "boolean" &&
      Array.isArray(field.children) &&
      field.children.every((child) => fieldIsValid(child, depth + 1))
    );
  };
  return value.fields.every((field) => fieldIsValid(field, 0));
}

export async function getSecurityInstrumentTerms(
  securityId: string,
  options: SecurityInstrumentTermsRequestOptions = {}
): Promise<SecurityInstrumentTerms> {
  const { expectedVersion, expectedFingerprint, ...requestOptions } = options;
  const response: unknown = await apiGetJson<unknown>(securityInstrumentTermsEndpoint(securityId), {
    ...requestOptions,
    allowDevelopmentFallback: false
  });
  if (!validTerms(response)) throw new Error("The instrument terms response is malformed. Reload the retained terms.");
  if (response.securityId !== securityId) throw new Error("Instrument terms do not belong to the selected security.");
  if (expectedVersion !== undefined && response.version !== expectedVersion) {
    throw new Error(
      "Instrument terms belong to a different economic-definition version. Refresh the asset before reviewing terms."
    );
  }
  if (expectedFingerprint !== undefined && response.fingerprint.toLowerCase() !== expectedFingerprint.toLowerCase()) {
    throw new Error(
      "Instrument terms changed from the expected fingerprint. Refresh the asset before reviewing terms."
    );
  }
  return response;
}
