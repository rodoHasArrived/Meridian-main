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

/**
 * Client-side pre-check mirroring the governed validator: required values present, numbers and
 * dates parse, and closed vocabularies hold. The server remains authoritative.
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
      return Number.isFinite(Number(trimmed)) ? null : `${field.label} must be a number.`;
    case "Integer":
      return /^-?\d+$/.test(trimmed) ? null : `${field.label} must be a whole number.`;
    case "Date":
      return /^\d{4}-\d{2}-\d{2}$/.test(trimmed) ? null : `${field.label} must be a date (YYYY-MM-DD).`;
    case "Toggle":
      return trimmed === "true" || trimmed === "false" ? null : `${field.label} must be true or false.`;
    case "Choice":
      return field.choices.includes(trimmed) ? null : `${field.label} must be one of: ${field.choices.join(", ")}.`;
    default:
      return null;
  }
}
