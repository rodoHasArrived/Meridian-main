# Security asset-pack admission

The asset-pack registry participates in canonical Security Master create and amend commands.
`SecurityAssetPackAdmission` first resolves a modeled class to its registered packs. Every matching
pack must carry that class's current writable contract, derived from `SecurityAssetTermsSchema`:
field keys and types, serialized-presence rules, vocabularies, aliases and registered nested item
shapes. A pack cannot replace a date with a string or claim a different terms version while retaining
an otherwise plausible policy description.

The Write-mode mapping validates each supplied raw member before tolerant decoding can discard
malformed array items or coerce wrong types. This pass preserves caller compatibility for omitted
members whose typed codec supplies defaults. After decoding, the canonical create/amend service
validates the complete serialized structure, including required inner members, before it appends
an event or writes a projection or conflict record. Errors name the affected term or item path.

This gate follows ADR-024: fields introduced above the node's write version do not enter the
writable schema. Existing read/write-version guards remain in force, and read reconstruction is
unchanged. Profile envelopes use the existing approved-profile validation path; the flat-class
schema does not interpret their dynamic fields. Dated schedule write semantics are checked by the
shared schedule write-rule helper after supplied shape validation.

`AssetPackContractSchema.AssetClassTerms` exposes these per-class executable structures. The older
`Terms`, `Counterparties`, `Dates`, `Currencies`, `Ownership`, `Seniority`, `Collateral`, `Rates` and
`OptionalAttributes` lists remain descriptive compatibility metadata. They do not validate a record.
Likewise, prose accounting policies and journal-template descriptions do not authorize posting,
certify evidence or grant approval. Those operations retain their own accounting and governance gates.

The operational coverage response explicitly reports `admissionEnforcement` as
`CanonicalCreateAndAmend` and `admissionSchemaSource` as `SecurityAssetTermsSchema`. Existing metadata
and aliases remain available to older consumers. Pack membership may overlap where the catalog
intentionally assigns one class to several operational groups; every matching contract is checked.

Relevant proof is in `SecurityAssetPackAdmissionTests`, `SchemaTermsValidationTests`,
`SecurityAssetClassParityGuardTests` and the existing canonical create/amend suites. This document
states the implemented boundary; deployment and a passing hosted integration gate remain separate
proof.
