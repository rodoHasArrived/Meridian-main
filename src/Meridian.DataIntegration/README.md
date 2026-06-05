---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-DATA-INTEGRATION
path: src/Meridian.DataIntegration
status: active
owner_lane: Data Confidence and Validation
last_reviewed: 2026-06-05
---

# src/Meridian.DataIntegration

## Purpose

Physical bounded-context module project for provider, ingestion, validation, source evidence, and publish-data ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.DataIntegration` - registered source module root.
- `Credentials/ProviderCredentialCatalog.cs` - canonical provider credential descriptors, field mappings, environment normalization, and credential-source projection.
- `Credentials/IProviderCredentialStore.cs` - provider-neutral encrypted credential store contract and mutation/read result models.
- `Credentials/FileProviderCredentialStore.cs` - local encrypted provider credential vault with audit metadata, verification status, and environment fallback handling.
- `Credentials/CredentialStatus.cs` - provider credential status, test-result, cached-status, and expiration-warning records.
- `Credentials/OAuthToken.cs` - provider-neutral OAuth token, provider config, and token-refresh result records.
- `AccountingSystem/QuickBooks/QuickBooksFixtureAccountingProvider.cs` - read-only fixture GL evidence provider for contract and workstation validation.
- `AccountingSystem/QuickBooks/QuickBooksOnlineAccountingProvider.cs` - read-only QuickBooks Online accounting-system adapter, token refresh, connection verification, company evidence import, and DTO projection.
- `AccountingSystem/QuickBooks/QuickBooksOnlineProviderCredentialConnectionStore.cs` - QuickBooks Online connection metadata, refresh-token, and verification-state adapter backed by the provider credential vault.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

QuickBooks accounting-system integration lives in this module. The adapter family imports chart-of-accounts, journal-entry, and trial-balance evidence as read-only reconciliation input through `IAccountingSystemProvider`, refreshes OAuth access tokens through the server-side QuickBooks client seam, records connection verification posture, maps provider-vault credentials into the QuickBooks connection store, and leaves posting/export disabled. UI Shared registers the Data Integration providers and connection store; it does not own QuickBooks transport, credential persistence mapping, or import mapping.

Provider credential catalog, vault, status, and OAuth record ownership also lives in this module. Application and UI layers may orchestrate setup, testing, token refresh, and endpoint projection, but provider credential descriptors, encrypted local storage, verification metadata, expiration policy records, OAuth token records, and provider-environment normalization must stay behind the `Meridian.DataIntegration.Credentials` seam.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-DATA-INTEGRATION -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-DATA-INTEGRATION -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.DataIntegration/Meridian.DataIntegration.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~AccountingSystemIntegrationServiceTests|FullyQualifiedName~ProviderConnectionEndpointsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~QuickBooksOnlineProviderCredentialConnectionStoreTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~OAuthTokenTests|FullyQualifiedName~CredentialStatusTests|FullyQualifiedName~CredentialTestingServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ProviderCredentialStoreTests|FullyQualifiedName~ProviderConnectionEndpointsTests|FullyQualifiedName~CredentialCompatibilityEndpointsTests|FullyQualifiedName~ProviderReadinessEndpointTests|FullyQualifiedName~ProviderRoutingEndpointsTests|FullyQualifiedName~ProviderFactoryCredentialContextTests|FullyQualifiedName~AlpacaBrokerageConnectionServiceTests|FullyQualifiedName~PlaidWorkstationServiceTests|FullyQualifiedName~BrokerageConnectionEndpointsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

### API and contract notes

The QuickBooks adapter implements `IAccountingSystemProvider`, `IAccountingSystemConnectionMetadataProvider`, and `IAccountingSystemConnectionVerifier` from `Meridian.ProviderSdk.AccountingSystem`. Accounting-system DTOs remain in `Meridian.Contracts.AccountingSystem`.

### Migration and archive notes

`QuickBooksFixtureAccountingProvider`, `QuickBooksOnlineAccountingProvider`, `IQuickBooksOnlineConnectionStore`, `IQuickBooksOnlineClient`, `QuickBooksOnlineHttpClient`, and QuickBooks evidence records moved from `src/Meridian.Infrastructure/Adapters/QuickBooks` into this module. Infrastructure no longer owns QuickBooks accounting-system transport.

`QuickBooksOnlineProviderCredentialConnectionStore` moved from `src/Meridian.Ui.Shared/Services` into this module. UI Shared keeps only endpoint and service-collection registration for the Data Integration-owned QuickBooks connection adapter.

`ProviderCredentialCatalog`, `IProviderCredentialStore`, `FileProviderCredentialStore`, `CredentialStatus`, and `OAuthToken` moved from `src/Meridian.Application/Config/Credentials` into this module. Application keeps credential testing, OAuth refresh, legacy resolver/composition, and provider setup orchestration as consumers of the Data Integration credential seam.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
