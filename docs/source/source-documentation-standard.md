# Source Documentation Standard

## Canonicalization and Determinism (Normative)

Source documentation generation tooling **MUST** comply with the following:

1. YAML parser/emitter
   - Use `ruamel.yaml` pinned to `0.18.x`.
2. Unicode normalization
   - Normalize all textual inputs/outputs to `NFC`.
3. Locale/timezone independence
   - Enforce `TZ=UTC` during render runs.
   - Perform locale-insensitive ordering/comparisons.
4. Strict date parsing
   - Accept only `YYYY-MM-DD` for date-only fields.
   - Reject datetime-containing values and locale-specific date formats.
5. Stable serializer ordering
   - Emit deterministic key ordering based on normalized key strings.
   - Preserve string types for identifiers and numeric-like strings.

## Rejection Rules

Renderers **MUST** reject ambiguous input where type intent is unclear, including:
- mixed unicode normalization forms that are not normalized before processing,
- numeric-like strings passed where numeric coercion would change semantics,
- unordered map semantics that would cause non-deterministic output,
- implicit datetime coercion in date-only fields.
