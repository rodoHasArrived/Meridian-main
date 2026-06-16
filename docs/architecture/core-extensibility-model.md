# Core Extensibility Model

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-10

Meridian has one stable financial operations core. Tenants can configure workflows, rules,
integrations, data mappings, reports, permissions, classifications, custom fields, source priority,
ledger controls, notifications, domain extensions, and templates around that core, but they do not
fork the core object model or weaken governed financial controls.

## Stable Core

The stable core object vocabulary is contract-owned in
`src/Meridian.Contracts/Extensibility/` and currently covers tenant, entity, relationship, account,
instrument, contract, obligation, expected cash flow, transaction, position, valuation,
reconciliation, exception, capital account, ledger account, journal entry, fund event, document,
task, report package, and audit event.

Tenant templates and domain extensions may add fields, classifications, labels, routing, or
evidence expectations, but they must attach to these objects instead of replacing them.

## Configurable Layers

| Layer | Examples | Boundary |
| --- | --- | --- |
| Workflows | Review steps, approval chains, task queues | Route and sequence work; do not own domain writes. |
| Rules | Validation rules, tolerances, materiality thresholds | Gate and classify review; do not override calculation engines. |
| Integrations | Provider mappings, file layouts, API connections | Contribute source evidence; do not become ledger truth. |
| Data mappings | Column mappings, provider schema versions | Preserve raw source, mapping version, and replay lineage. |
| Reports | Templates, schedules, recipients, evidence packages | Publish only approved, evidence-bound outputs. |
| Permissions | Roles, data scopes, approval authority | Extend scoped authority inside the security foundation. |
| Classifications | Asset classes, strategies, categories | Add tenant meaning without changing object identity. |
| Custom fields | Tenant-specific attributes | Cannot replace identifiers, lineage, approvals, or audit fields. |
| Source priority | Which source wins for price, position, cash, or terms | Must be versioned, explainable, and replayable. |
| Ledger controls | Posting rules, idempotency keys, period locks, reversals | Cannot weaken balancing, immutability, approval, or period locks. |
| Notifications | Alerts, escalations, reminders | Prompt action; authority remains permission and approval owned. |
| Domain extensions | Tenant object attributes and descriptors | Layer around stable objects without ungoverned identity. |
| Tenant templates | Profile bundles for onboarding | Activate governed configuration; do not create separate products. |

For no-code provider setup, integration configuration should compile into a versioned
`ProviderIntegrationManifest` executed by a generic connector runtime with raw payload retention,
mapping, validation, quarantine, and activation gates. See the
[Provider Integration Manifest Runtime](provider-integration-manifest-runtime.md) blueprint.

## Governed Foundations

These foundations are intentionally not fully configurable:

- Audit trail
- Security model foundation
- Core object identity
- Financial calculation integrity
- Data lineage model
- Approval evidence model
- Immutable record preservation

Any extensibility provider must declare which foundations it depends on and which guardrails prevent
tenant configuration from bypassing those controls.

## Current Implementation Seams

- `src/Meridian.Contracts/Extensibility/CoreExtensibilityContracts.cs` defines the shared DTOs,
  stable object catalog, configurable layer catalog, and governed foundation catalog.
- `src/Meridian.Contracts/Extensibility/CoreExtensibilityContractsJsonContext.cs` provides the
  generated JSON context for browser, WPF, service, and retained-manifest consumers.
- `src/Meridian.Ui.Shared/Extensibility/ExtensibilityCatalogService.cs` aggregates registrations
  from provider adapters into a shared `ExtensibilityCatalogDto`.
- `src/Meridian.Ui.Shared/Extensibility/ExtensibilityConfigurationService.cs` stores
  tenant-template configuration bundles, evaluates activation readiness, and fails closed when a
  template or domain extension attempts to override core object identity, audit trail, or financial
  calculation integrity, or when bundled configuration envelopes lack approved status and retained
  approval actor/timestamp evidence.
- `src/Meridian.Ui.Shared/Extensibility/WorkflowExtensibilityCatalogProvider.cs` adapts the shared
  workflow library so built-in workstation workflows and actions appear as configurable workflow
  registrations without becoming domain write authority.
- `src/Meridian.Ui.Shared/Extensibility/ReportingTemplateExtensibilityCatalogProvider.cs` adapts
  `IReportingTemplateCatalog` so governed report templates appear as report configuration
  registrations.
- `src/Meridian.Ui.Shared/Extensibility/PermissionExtensibilityCatalogProvider.cs` adapts the
  Identity role/permission catalog so permission profiles and permission flags appear as governed
  permission registrations.
- `src/Meridian.Ui.Shared/Extensibility/OperationalConfigurationExtensibilityCatalogProvider.cs`
  advertises the active ledger-control, accounting-validation, posting-rule mapping, and provider
  integration seams plus draft notification, tenant-template, and domain-extension contract seams.
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.Extensibility.cs` exposes the shared
  catalog at `/api/workstation/extensibility/catalog` so browser and WPF workstation clients can
  consume the same stable-core and configurable-layer registry. The same endpoint partial also
  exposes tenant-template bundle listing, save, activation-readiness, activation, and
  activation-history routes under
  `/api/workstation/extensibility/tenant-templates`.
- `src/Meridian.Ui.Shared/Services/WorkstationServiceCollectionExtensions.cs` registers the
  workstation file-backed configuration store at
  `workstation/extensibility/configuration-bundles.json`; stronger hosts may replace the store, but
  activation must still preserve the governed-foundation checks.

Future providers should plug into `IExtensibilityCatalogProvider` for report templates, rules,
integration mappings, scoped permissions, ledger controls, notifications, and domain-extension
descriptors instead of creating UI-local extension catalogs.
