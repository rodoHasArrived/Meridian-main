import type {
  SecurityAssetProfileDefinition,
  SecurityAssetProfileFieldDefinition
} from "@/types";

/**
 * Form state for the profile-backed security creation panel on the settings screen: the pinned
 * profile selection, the primary InternalCode identifier plus any ADDITIONAL required identifier
 * kinds the profile declares, and the profile-governed field values.
 */
export interface ProfileBackedSecurityState {
  profileId: string;
  displayName: string;
  internalCode: string;
  currency: string;
  /** Values for the profile's ADDITIONAL required identifier kinds (beyond InternalCode), keyed by kind. */
  identifierValues: Record<string, string>;
  /**
   * Provider namespaces for identifier kinds that require one (ProviderSymbol), keyed by kind. A
   * ProviderSymbol submitted without its provider constructs an identifier the write-side command
   * validation rejects (the kind requires a nonblank namespace), so the form must collect it.
   */
  identifierProviders: Record<string, string>;
  fieldValues: Record<string, string>;
  rationale: string;
  busy: boolean;
  message: string | null;
  details: string[];
  tone: "default" | "success" | "danger" | "warning";
}

export function createProfileBackedSecurityState(
  profile: SecurityAssetProfileDefinition | null
): ProfileBackedSecurityState {
  return {
    profileId: profile?.profileId ?? "",
    displayName: "",
    internalCode: "",
    currency: "USD",
    identifierValues: {},
    identifierProviders: {},
    fieldValues: profile ? buildProfileFieldValueState(profile, {}) : {},
    rationale: "Create profile-backed custom asset with approved Security Master profile version.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  };
}

/**
 * A profile version is selectable for NEW writes when it is Approved or Superseded AND its
 * effective window covers today - the same window write-time governance enforces, so the creation
 * form never advertises a write that validation will reject. The Superseded arm matters because
 * governance marks the predecessor Superseded the moment a replacement is approved, even when
 * that replacement carries a FUTURE effectiveFrom; until the replacement activates, the
 * superseded predecessor is the only version write-time validation accepts. The window check
 * applies to Approved versions too: a freshly approved profile whose effectiveFrom is still in
 * the future cannot back a write today and must not enable the form.
 */
export function isWriteSelectableAssetProfile(
  profile: SecurityAssetProfileDefinition,
  today: Date = new Date()
): boolean {
  if (profile.status !== "Approved" && profile.status !== "Superseded") return false;
  const isoToday = today.toISOString().slice(0, 10);
  return profile.effectiveFrom <= isoToday
    && (profile.effectiveTo == null || isoToday <= profile.effectiveTo);
}

/**
 * Details for the profile's required identifier inputs that are still missing: the identifier
 * value itself, and — for ProviderSymbol — the provider namespace, since the write-side command
 * validation rejects that kind with a blank provider and the form must catch it up front.
 */
export function missingRequiredIdentifierDetails(
  preferences: { kind: string }[],
  state: ProfileBackedSecurityState
): string[] {
  const missingValues = preferences
    .filter((preference) => !state.identifierValues[preference.kind]?.trim())
    .map((preference) => `${preference.kind} identifier`);
  const missingProviders = preferences
    .filter((preference) => preference.kind === "ProviderSymbol"
      && !!state.identifierValues[preference.kind]?.trim()
      && !state.identifierProviders[preference.kind]?.trim())
    .map((preference) => `${preference.kind} provider namespace`);
  return [...missingValues, ...missingProviders];
}

export function buildProfileFieldValueState(
  profile: SecurityAssetProfileDefinition,
  previous: Record<string, string>
): Record<string, string> {
  return Object.fromEntries(profile.fields.map((field) => [
    field.key,
    previous[field.key] ?? defaultProfileFieldValue(field)
  ]));
}

function defaultProfileFieldValue(field: SecurityAssetProfileFieldDefinition): string {
  // Only a REQUIRED Boolean defaults to an asserted false: an optional Boolean the operator never
  // touched must stay absent from the payload (buildProfileFieldPayload skips blanks), not assert
  // a potentially meaningful negative value.
  if (field.fieldType === "Boolean" && field.isRequired) return "false";
  return "";
}

/**
 * Canonical text form of a plain decimal input ("+" stripped, leading integer zeros collapsed,
 * trailing fractional zeros and a bare "." dropped), or null when the input is not plain decimal
 * notation. Used to detect binary rounding: JavaScript Number is an IEEE double, so an input whose
 * canonical form differs from the parsed double's exact decimal expansion was silently altered.
 */
function canonicalDecimalText(raw: string): string | null {
  const match = /^([+-]?)(\d*)(?:\.(\d*))?$/.exec(raw);
  if (!match || (match[2] === "" && (match[3] ?? "") === "")) return null;
  const sign = match[1] === "-" ? "-" : "";
  let intPart = (match[2] ?? "").replace(/^0+(?=\d)/, "");
  const fracPart = (match[3] ?? "").replace(/0+$/, "");
  if (intPart === "") intPart = "0";
  if (intPart === "0" && fracPart === "") return "0";
  return fracPart ? `${sign}${intPart}.${fracPart}` : `${sign}${intPart}`;
}

/**
 * The parsed double's round-trip string expanded to PLAIN decimal notation ("1e-7" becomes
 * "0.0000001"), so small exact values that JavaScript renders in exponent form still compare
 * equal to the operator's plain-decimal input - the comparison is numeric fidelity, not spelling.
 */
function toPlainDecimalString(value: number): string {
  const text = String(value);
  const match = /^(-?)(\d+)(?:\.(\d+))?e([+-]\d+)$/i.exec(text);
  if (!match) return text;
  const sign = match[1];
  const digits = (match[2] ?? "") + (match[3] ?? "");
  const pointIndex = (match[2] ?? "").length + Number(match[4]);
  if (pointIndex <= 0) {
    return `${sign}0.${"0".repeat(-pointIndex)}${digits}`;
  }
  if (pointIndex >= digits.length) {
    return `${sign}${digits}${"0".repeat(pointIndex - digits.length)}`;
  }
  return `${sign}${digits.slice(0, pointIndex)}.${digits.slice(pointIndex)}`;
}

/** Whether the operator's text and the double it parsed to assert the SAME exact decimal value. */
function numberRoundTripsExactly(raw: string, parsed: number): boolean {
  const canonical = canonicalDecimalText(raw);
  return canonical !== null && toPlainDecimalString(parsed) === canonical;
}

/**
 * Builds the profile field payload for security creation. Values that fail to parse are reported
 * in invalidFields instead of being emitted - Number.parseFloat("") is NaN, which JSON.stringify
 * would silently serialize as null, and prefix-parsers would truncate values like "12,5" to 12.
 * Numeric values must additionally round-trip EXACTLY through the JavaScript Number used to
 * serialize them: the server contract is .NET decimal, so an input the IEEE double silently
 * rounds (9007199254740993, long fractional commitments) would persist different economics than
 * the operator entered - such values are rejected rather than altered.
 */
export function buildProfileFieldPayload(
  fields: SecurityAssetProfileFieldDefinition[],
  values: Record<string, string>
): { payload: Record<string, unknown>; invalidFields: string[] } {
  const payload: Record<string, unknown> = {};
  const invalidFields: string[] = [];
  for (const field of fields) {
    const raw = values[field.key]?.trim() ?? "";
    if (!raw) {
      if (field.isRequired) {
        invalidFields.push(`${field.label}: a value is required.`);
      }
      continue;
    }

    switch (field.fieldType) {
      case "Decimal": {
        const parsed = Number(raw);
        if (!Number.isFinite(parsed)) {
          invalidFields.push(`${field.label}: enter a valid number.`);
          break;
        }
        if (!numberRoundTripsExactly(raw, parsed)) {
          invalidFields.push(`${field.label}: this value cannot be submitted exactly (the browser would round it); enter fewer significant digits.`);
          break;
        }
        payload[field.key] = parsed;
        break;
      }
      case "Integer": {
        const parsed = Number(raw);
        if (!Number.isInteger(parsed)) {
          invalidFields.push(`${field.label}: enter a whole number.`);
          break;
        }
        if (!numberRoundTripsExactly(raw, parsed)) {
          invalidFields.push(`${field.label}: this value cannot be submitted exactly (the browser would round it); enter a smaller whole number.`);
          break;
        }
        payload[field.key] = parsed;
        break;
      }
      case "Boolean":
        payload[field.key] = raw === "true";
        break;
      default:
        payload[field.key] = raw;
        break;
    }
  }
  return { payload, invalidFields };
}
