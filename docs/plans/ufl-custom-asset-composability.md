# UFL Custom Asset Composability

**Owner:** Core Team
**Audience:** Product, architecture, domain, storage, application, and workstation contributors
**Last Updated:** 2026-05-29
**Status:** target-state implementation requirement

## Summary

UFL must support user-configurable custom assets without forcing every new instrument shape to become a compiled `SecurityKind` case on day one. The target design is a composable custom-asset lane: users define governed asset profiles from approved UFL capabilities, Meridian stores those profiles as versioned configuration, and operational workflows can review, validate, project, and eventually promote heavily used profiles into first-class asset packages.

This lane complements, rather than replaces, the existing `OtherSecurity` package. `OtherSecurity` remains the controlled generic-security fallback, while custom asset profiles provide a more structured way for users to model repeatable non-standard instruments with explicit fields, validation rules, projections, and review evidence.

Custom asset profiles are profiles over the shared UFL capability model, not a separate modeling system. They must satisfy the same identity, validation, provider-isolation, projection metadata, workstation-control, and accounting-impact guardrails as compiled asset packages before any maturity claim is made.

## Evidence Boundary

### Implemented

- The UFL index links this lane as a governed custom-asset target.
- `OtherSecurity` exists as the current generic-security fallback path.

### Partially Implemented

- The custom-asset lane is documented as a target-state requirement, but profile definitions, profile-version storage, validation services, and profile-backed Security Master records are not evidenced as delivered.

### Target-State Only

- Versioned custom asset profile definitions.
- Typed field schema validation.
- Profile approval, rollback, and promotion workflows.
- Profile-backed Security Master create/amend behavior.
- Profile-field query projections and profile-version lineage.

### Explicitly Out of Scope

- User-authored arbitrary code or scripts in profile validation.
- Provider payload passthrough as a substitute for canonical terms.
- Mobile-specific custom asset flows.
- Bypassing dedicated UFL packages when a custom profile becomes a stable high-volume requirement.

## UFL Capability Profile

| Capability | Level | Current evidence | Target addition | Tests |
| --- | ---: | --- | --- | --- |
| InstrumentIdentity | L0 | target-state only. | profile-backed Security Master identity and common terms | create/amend tests |
| TermsVersioning | L0 | target-state only. | approved profile versions pinned to security records | versioning tests |
| Lifecycle | L0 | target-state only. | optional profile-defined lifecycle states and transitions | validation/projection tests |
| ProjectionRebuild | L0 | target-state only. | profile-field projections with profile version and source-event metadata | rebuild metadata tests |
| WorkstationControl | L0 | target-state only. | draft, approve, rollback, and promote profile workflows | workflow/endpoint tests |
| AccountingImpact | L0 | target-state only. | approved accounting-impact hints only, never arbitrary posting logic | validation tests |

## Current Maturity

`L0 target-state`: the custom-asset lane is documented and linked, but implementation has not yet reached L1 until governed profile definitions, validation, version pinning, and canonical reference reads exist.

## Next Milestone Contract

**Goal:** advance custom assets to L1 by introducing governed custom profile definitions, typed fields, deterministic validation, profile-version storage, and profile-backed Security Master reference reads.

**Files likely touched:**

- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.Application/SecurityMaster/`
- `src/Meridian.Contracts/SecurityMaster/`
- `src/Meridian.Ui.Shared/Endpoints/`
- `tests/Meridian.Tests/`

**Acceptance evidence:**

- profile validation tests for field keys, types, required fields, enum values, and reserved names.
- Security Master create/amend tests proving approved profile-version pinning.
- endpoint tests for profile definitions and profile-backed securities.
- rebuild metadata tests proving profile version and source event are projected.

**Exit criteria:** users can configure a governed profile and create a profile-backed security without bypassing canonical identity, validation, lineage, or provider-payload isolation.

## Provider Payload Boundary

Custom profiles are not ad hoc provider payload containers. Provider-specific data may be retained as evidence, but profile-backed assets must expose approved typed fields, deterministic validation, canonical Security Master identity, and profile-version lineage.

## Design Goals

1. **Composable:** users assemble custom assets from approved capabilities such as identity, issuer/counterparty, underlying link, lifecycle, cash-flow schedule, accrual convention, collateral, provider aliases, and accounting-impact hints.
2. **Governed:** every profile has owner, version, approval status, effective dates, change reason, and rollback metadata.
3. **Safe by default:** custom assets cannot bypass Security Master validation, provider payload isolation, lineage, or review controls.
4. **Queryable:** custom profile fields are projected into typed read surfaces where possible and metadata-backed search surfaces where not.
5. **Promotable:** repeated or business-critical custom profiles can be promoted into dedicated asset packages while preserving Security Master identity and lineage.

## Target Capability Model

| Capability | Custom-profile use | Required guardrail |
| --- | --- | --- |
| Instrument identity | Defines display name, currency, country, settlement, and status fields. | Reuse canonical Security Master identity and common terms. |
| Taxonomy | Defines custom category, subtype, and business purpose. | Must be searchable and reviewable; cannot be blank or provider-only. |
| Field schema | Adds user-defined fields with type, required flag, allowed values, and display label. | Profiles are versioned; field deletion requires migration/rollback notes. |
| Validation rules | Adds no-code rules such as required fields, ranges, date ordering, allowed enum values, and underlying-link requirements. | Rules must be deterministic and replay-safe. |
| Lifecycle | Defines custom lifecycle states and transition rules when needed. | State transitions require event lineage and reviewer metadata for governed profiles. |
| Projection | Projects custom profile values for Data, Accounting, Reporting, and search surfaces. | Projection metadata must include profile version and source event. |
| Promotion | Flags repeated custom profiles for dedicated UFL package design. | Promotion plan must preserve identity, lineage, and audit trail. |

## Configuration Shape

A custom asset profile should be stored as a versioned definition, not as ad hoc JSON inside each security record.

```fsharp
type CustomAssetProfileId = CustomAssetProfileId of Guid
type CustomAssetProfileVersion = CustomAssetProfileVersion of int

type CustomAssetFieldType =
    | Text
    | Decimal
    | Integer
    | Boolean
    | Date
    | Enum of allowedValues: string list
    | SecurityLink
    | CurrencyCode

type CustomAssetFieldDef = {
    FieldKey: string
    Label: string
    FieldType: CustomAssetFieldType
    IsRequired: bool
    Description: string option
}

type CustomAssetProfileDef = {
    ProfileId: CustomAssetProfileId
    Version: CustomAssetProfileVersion
    Name: string
    Category: string
    SubType: string option
    Capabilities: string list
    Fields: CustomAssetFieldDef list
    ApprovalStatus: string
    EffectiveFrom: DateOnly option
    EffectiveTo: DateOnly option
}
```

Security instances should reference the approved profile version used at creation or amendment time. That keeps historical records interpretable even after a profile evolves.

## Workflow

1. **Draft profile:** user creates a custom asset profile in the Data or Settings workspace using approved UFL capabilities and typed fields.
2. **Validate profile:** Meridian validates field keys, types, required fields, date rules, enum values, reserved names, and capability compatibility.
3. **Approve profile:** an authorized reviewer approves the profile version with owner, reason, and effective date metadata.
4. **Create assets:** users create custom assets against an approved profile version; Security Master stores canonical identity plus profile-backed terms.
5. **Project and review:** projection workers build taxonomy, profile-field, lifecycle, and review views with profile version lineage.
6. **Promote when needed:** repeated or operationally critical profiles become promotion candidates for first-class UFL asset packages.

## Implementation Boundaries

### In scope

- Versioned custom asset profile definitions.
- Typed field schemas and deterministic validation rules.
- Profile-backed Security Master records that retain canonical identity and lineage.
- Review, approval, rollback, and promotion-candidate workflows.
- Query/read-model support for custom profile fields and profile version metadata.

### Out of scope

- User-authored arbitrary code or scripts in profile validation.
- Provider payload passthrough as a substitute for canonical terms.
- Silent creation of new root workspaces or mobile-specific custom asset flows.
- Bypassing dedicated UFL packages when a custom profile becomes a stable high-volume product requirement.

## Relationship To `OtherSecurity`

`OtherSecurity` should remain the fallback for one-off or low-maturity generic securities. Custom asset profiles should be used when the user needs repeatable structure, typed fields, lifecycle rules, and profile-level governance. The promotion path should let Meridian graduate successful custom profiles into dedicated UFL packages without breaking existing security IDs or historical projections.

## First Implementation Milestone

**Goal:** introduce profile governance before expanding asset-specific custom behavior.

**Deliverables:**

1. custom asset profile DTOs and validation service;
2. storage for approved profile versions and draft versions;
3. Security Master create/amend path that can reference an approved profile version;
4. read endpoint for custom profile definitions and profile-backed securities;
5. tests for validation, version pinning, profile approval, and rebuild-safe projection metadata.

**Exit criteria:** users can configure a governed custom asset profile, create a profile-backed security, query it through canonical reference surfaces, and see profile version lineage in the projected read model.
