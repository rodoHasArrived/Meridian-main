import type {
  SecurityAssetClassDefinition,
  SecurityAssetTermFieldDefinition
} from "@/lib/api/security-master-schema.api";

export interface PassportSchemaFieldGroup {
  group: string;
  fields: SecurityAssetTermFieldDefinition[];
}

export type PassportSchemaInputKind = "text" | "number" | "date" | "select";

export interface PassportSchemaValueEditor {
  input: PassportSchemaInputKind;
  choices: string[];
  step?: string;
}

/**
 * The scalar-editable terms of an asset class, grouped in registry order. Arrays and nested
 * objects are replaced whole through the term-block route, so they are not offered as fields.
 */
export function buildSchemaFieldGroups(definition: SecurityAssetClassDefinition | null): PassportSchemaFieldGroup[] {
  if (!definition) {
    return [];
  }

  const groups = new Map<string, SecurityAssetTermFieldDefinition[]>();
  for (const field of [...definition.fields].sort((a, b) => a.order - b.order)) {
    if (!field.scalarEditable) {
      continue;
    }

    const bucket = groups.get(field.group) ?? [];
    bucket.push(field);
    groups.set(field.group, bucket);
  }

  return [...groups.entries()].map(([group, fields]) => ({ group, fields }));
}

/** The value control a term needs: a select for vocabularies and toggles, typed inputs otherwise. */
export function buildSchemaValueEditor(field: SecurityAssetTermFieldDefinition | null): PassportSchemaValueEditor {
  switch (field?.editor) {
    case "Choice":
    case "Convention":
      return { input: "select", choices: field.choices };
    case "Toggle":
      return { input: "select", choices: ["true", "false"] };
    case "Date":
      return { input: "date", choices: [] };
    case "Decimal":
      return { input: "number", choices: [], step: "any" };
    case "Integer":
      return { input: "number", choices: [], step: "1" };
    default:
      return { input: "text", choices: [] };
  }
}

const LONG_MIN = -9223372036854775808n;
const LONG_MAX = 9223372036854775807n;

/**
 * Invariant-culture decimal text, as the shared form model's `decimal.TryParse` reads it: an
 * optional sign, digits with optional `,` group separators before the decimal point, an optional
 * fraction, and no exponent. Magnitudes beyond `decimal` range are not re-checked here; the
 * governed validator on the server stays authoritative.
 */
function isInvariantDecimal(trimmed: string): boolean {
  if (/[eE]/.test(trimmed)) {
    return false;
  }

  const dot = trimmed.indexOf(".");
  const integerPart = dot >= 0 ? trimmed.slice(0, dot) : trimmed;
  const fractionPart = dot >= 0 ? trimmed.slice(dot + 1) : "";
  const unsignedInteger = integerPart.replace(/^[+-]/, "");
  if (!/^[\d,]*$/.test(unsignedInteger) || !/^\d*$/.test(fractionPart)) {
    return false;
  }

  const integerDigits = unsignedInteger.replace(/,/g, "");
  return integerDigits.length > 0 || fractionPart.length > 0;
}

/** A whole number within `long` range, as `long.TryParse` reads it: optional sign, digits. */
function isLongInteger(trimmed: string): boolean {
  if (!/^[+-]?\d+$/.test(trimmed)) {
    return false;
  }

  const parsed = BigInt(trimmed);
  return parsed >= LONG_MIN && parsed <= LONG_MAX;
}

/** A real proleptic-Gregorian calendar date in exact `yyyy-MM-dd` form, matching `DateOnly`. */
function isCalendarDate(trimmed: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(trimmed);
  if (!match) {
    return false;
  }

  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  if (year < 1 || month < 1 || month > 12 || day < 1) {
    return false;
  }

  const leap = (year % 4 === 0 && year % 100 !== 0) || year % 400 === 0;
  const daysInMonth = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
  return day <= daysInMonth;
}

/**
 * Client-side pre-check mirroring the shared `SecurityAssetTermFormModel.Validate`: required values
 * present, numbers and dates parse under the same invariant rules, and closed vocabularies hold.
 * The parity fixture test (`security-passport-schema-fields.parity.test.ts`) pins this mirror to
 * the C# model's verdicts; the server remains authoritative.
 */
export function validateSchemaFieldValue(field: SecurityAssetTermFieldDefinition | null, value: string): string | null {
  if (!field) {
    return null;
  }

  const trimmed = value.trim();
  if (trimmed.length === 0) {
    return field.required ? `${field.label} is required.` : null;
  }

  switch (field.editor) {
    case "Decimal":
      return isInvariantDecimal(trimmed) ? null : `${field.label} must be a number.`;
    case "Integer":
      return isLongInteger(trimmed) ? null : `${field.label} must be a whole number.`;
    case "Date":
      return isCalendarDate(trimmed) ? null : `${field.label} must be a date (YYYY-MM-DD).`;
    case "Toggle":
      return trimmed === "true" || trimmed === "false" ? null : `${field.label} must be true or false.`;
    case "Choice":
      return field.choices.includes(trimmed) ? null : `${field.label} must be one of: ${field.choices.join(", ")}.`;
    default:
      return null;
  }
}
