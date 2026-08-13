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
    fieldValues: profile ? buildProfileFieldValueState(profile, {}) : {},
    rationale: "Create profile-backed custom asset with approved Security Master profile version.",
    busy: false,
    message: null,
    details: [],
    tone: "default"
  };
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
  if (field.fieldType === "Boolean") return "false";
  return "";
}

/**
 * Builds the profile field payload for security creation. Values that fail to parse are reported
 * in invalidFields instead of being emitted - Number.parseFloat("") is NaN, which JSON.stringify
 * would silently serialize as null, and prefix-parsers would truncate values like "12,5" to 12.
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
        payload[field.key] = parsed;
        break;
      }
      case "Integer": {
        const parsed = Number(raw);
        if (!Number.isInteger(parsed)) {
          invalidFields.push(`${field.label}: enter a whole number.`);
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
