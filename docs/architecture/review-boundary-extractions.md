# Review boundary extractions

Reviewed: 2026-11-12. These changes preserve financial behavior and persistence schemas while
separating adapters from their lower-level contracts and primitives.

- `Meridian.Storage` owns market-data sinks, WAL, maintenance, catalog and shared durable IO.
  `Meridian.Storage.Accounting` compiles the PostgreSQL business adapters for ledger, reporting,
  Security Master, fund accounts/structure, asset operations, banking, money market, direct lending,
  operations and tenancy. Existing source paths and namespaces remain stable through linked sources;
  SQL migration files retain the same output paths. The adapter composes Storage with the Ledger
  and Reporting owners; Storage has no dependency on either owner.
- `Meridian.Risk` uses contracts from `Meridian.Execution.Sdk`. Risk admission results, log
  sanitization and governed escalation records now reside in the SDK with their existing namespaces.
  Circuit-breaker control, escalation queue and audit outcomes have explicit SDK interfaces.
  Runtime services implement those contracts and host aliases resolve the existing singleton instances.
- Reconciliation import, durability and IB Flex adapters reside in `Meridian.DataIntegration`,
  so FinancialOperations no longer depends on the market-data provider-adapter assembly.
  Strategies likewise uses ProviderSdk contracts without an Infrastructure assembly reference.

Rebuild the host and all in-process plugin assemblies together. Type forwarders preserve lookup of
relocated public execution contracts through their former assembly-qualified names. They do **not**
preserve the old CLR constructor signatures of `CompositeRiskValidator` and `KillSwitchSweepTripHandler`,
whose concrete runtime-service parameters became SDK authority interfaces. Source callers remain
compatible because the concrete implementations implement those interfaces; previously compiled callers
must be rebuilt before deployment.

## Focused test ownership

`Meridian.Storage.Tests` owns the source-linked market-data storage primitive tests (WAL, sinks,
maintenance primitives, catalog, conversion, buffers and file IO). Domain persistence and host
composition integration tests remain in `Meridian.Tests`, where their shared PostgreSQL fixtures live.
`Meridian.Execution.Tests` owns the execution unit/contract sources; the three host/UI composition tests
remain in the broad suite. Shared source manifests are imported by both projects and the broad suite,
which removes exactly those files from compilation. Each test has one discovery owner.

The focused Storage project references only Storage, Core, Domain and provider Infrastructure;
the focused Execution project avoids host, UI and MCP dependencies. The source files remain at their
existing paths to preserve review history and links. Source-linked helper files contain no test cases.

## CI assessment confirmation

The original review's duplicate CI claim is stale in the assessed checkout. `ci.yml` runs Secret Scan
on pull requests and retains full verification for nightly/manual execution. `meridian-ci.yml` owns
the four canonical validation lanes. Documentation Automation deliberately reports its check on every
pull request/merge group because repository-structure generation can depend on any tracked path.
The existing consolidation and check-identity policy is documented in
[CI/CD validation ownership](../engineering/ci-cd-optimization.md). No validation purpose or required
check was removed to reduce the apparent workflow count.
