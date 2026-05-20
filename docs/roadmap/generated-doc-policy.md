# Roadmap Generated Documentation Policy

## Deterministic Serialization Requirements (Normative)

All roadmap documentation renderers **MUST** produce byte-stable output across operating systems and locales.

1. YAML parser/emitter
   - Renderers **MUST** use `ruamel.yaml`.
   - Renderers **MUST** pin the major/minor version to `0.18.x`.
2. Unicode normalization
   - All text keys and values **MUST** be normalized to Unicode `NFC` before comparison, sorting, and serialization.
3. Locale/timezone independence
   - Renderers **MUST** set `TZ=UTC` for process-level execution.
   - Renderers **MUST** use locale-insensitive comparisons (Unicode codepoint ordering after NFC normalization).
4. Date parsing
   - Date-only fields **MUST** accept only `YYYY-MM-DD`.
   - Renderers **MUST NOT** implicitly coerce datetimes (`YYYY-MM-DDTHH:MM:SS`, timezone suffixes, or localized formats) into date-only fields.
5. Stable key ordering
   - Mapping keys **MUST** be sorted deterministically at serializer level using normalized string key ordering.
   - Numeric-like strings (`"01"`, `"1"`, `"1.0"`) **MUST** remain strings and **MUST NOT** be coerced.

## Validation Expectations

Renderers **MUST** fail fast on ambiguous types (booleans-as-strings, numeric-looking identifiers, non-string mapping keys where string keys are required, mixed date/datetime values for date-only fields).
