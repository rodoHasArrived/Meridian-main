# Entity Master Adoption: Counterparties and Fund Structure

**Status:** draft
**Owner:** core-team
**Reviewed:** 2026-11-25

This plan covers the remaining consumers of the entity master that
[ADR-033](../../adr/033-entity-master-bounded-context.md) names: counterparties and fund-structure legal
entities. ADR-033 is still **Proposed**, so nothing here starts until the owner accepts it. The plan is a
working input, not a status source. Live status stays in the [roadmap registry](../../roadmap/README.md).

## Where adoption stands

| Consumer | State | Evidence |
|---|---|---|
| Security Master issuers | EDGAR ingest finds or registers the issuer by CIK and records a valid-dated issuer link. CIK no longer chooses between share classes. | `EdgarIngestOrchestrator.LinkIssuerAsync`, `EdgarIngestOrchestratorTests` |
| Issuer concentration | The portfolio exposure report groups gross exposure by linked issuer and reports unlinked exposure separately. | `IssuerConcentrationService`, `GET /api/portfolio/exposure` (`issuerConcentration`) |
| Counterparties | Not started. OTC master agreements carry free-text `Counterparty` and `CounterpartyLei`. | `src/Meridian.Contracts/SecurityMaster/OtcContracts.cs` |
| Fund-structure legal entities | Not started. Fund structure owns its own `LegalEntitySummaryDto` and a tenant-scoped lifecycle timeline. | `src/Meridian.Storage/FundStructure/IEntityLifecycleStore.cs`, `src/Meridian.Contracts/FundStructure/EntityLifecycleDtos.cs` |

## Principle: identity is shared, lifecycle is not

The entity master owns **identity**: one `EntityId` per legal entity, its LEI and CIK, and the
valid-dated parent hierarchy. It does not own operating state.

Fund structure already keeps an immutable, hash-chained lifecycle per tenant
(`EntityLifecycleRevisionDto`). That timeline has half-open status windows (`Draft` … `Dissolved`,
`Merged` with a successor), revision pinning, and read leases that accounting transactions hold until
they commit. Those are accounting controls for one tenant's books. Moving them into a shared master
would couple every tenant's postings to a cross-context store, and ADR-033 rejects that for the same
reason it keeps issuers out of the Security Master.

So adoption **links** the contexts and never merges them:

- A fund-structure legal entity gains an optional `MasterEntityId` that references the entity master.
- The fund-structure lifecycle stays the authority for whether that tenant may post against the
  entity. The entity master stays the authority for who the entity is and who its parent was on a date.

## Stage 1: Counterparty identity (OTC master agreements)

1. Add an optional `CounterpartyEntityId` to `OtcMasterAgreementDto` and its request. Keep
   `Counterparty` and `CounterpartyLei` as display and evidence fields, as ADR-033 does for
   `IssuerName`.
2. On create or amend, when an LEI is present, find the entity by LEI or register it, then record the id.
   Without an LEI, record no link and never match by name.
3. Counterparty exposure, by netting set or by agreement, groups by the counterparty's **ultimate parent
   as of the valuation date**. This is the counterparty counterpart of the issuer concentration consumer.
4. Backfill links lazily on the next write. There is no bulk migration, which matches ADR-033 decision 4.

**Done when:** each agreement written after the change carries an entity link when it has an LEI,
counterparty exposure groups by ultimate parent with an unlinked bucket, and both stores have tests.

## Stage 2: Fund-structure legal entities

1. Add `MasterEntityId` (nullable) to the fund-structure legal entity record and its migration. Linking
   is an explicit, audited operator action, recorded as a `LegalEntityLifecycleEventDto` with evidence.
   It is never inferred from the name.
2. **Reconciling the two timelines.** This is the part ADR-033 decision 5 asks each change to state.

   | Fund-structure event | Entity-master effect |
   |---|---|
   | `Draft` / `Forming` | None. A forming vehicle may not be a legal entity yet, so it is linked once it is registered. |
   | `Active`, `AmendmentPending`, `Restructuring`, `WindingDown` | None. These are operating states of the tenant's books. |
   | `Dissolved` | None in the entity master. Dissolution is tenant-local. The legal entity may still exist, and other tenants may still reference it. |
   | `Merged` with `SuccessorEntityId` | Proposes, and does not apply, a parent or successor link in the entity master. An operator confirms it, because a merger in one tenant's books is evidence about the shared entity but not proof of it. |
   | Parent change in the fund structure tree | No automatic write. The fund structure tree is an *ownership and reporting* hierarchy, and the entity master hierarchy is a *legal parent* hierarchy. They can legitimately differ. A divergence report lists the entities where they disagree. |

3. Read leases stay in fund structure. An accounting transaction never holds a lock in the entity
   master.
4. Consolidation and intercompany controls keep keying on fund-structure entity ids. Cross-tenant
   reporting, for example "every fund's exposure to issuer X's group", joins through `MasterEntityId`.

**Done when:** the link and its audit event exist, the merger proposal and divergence report have
tests, and no accounting path reads the entity master inside a posting transaction.

## Stage 3: Workstation surfaces

Both workstations need an entity view: identity, LEI and CIK, the parent hierarchy as of a date, linked
securities, and linked fund-structure entities. It goes under **Data** (reference data). It must not add
a top-level navigation item. Build it from a shared read model, so the browser and WPF lanes use the same
seam (see the [WPF alignment plan](wpf-web-ui-alignment-plan.md)).

## Open decisions for the owner

1. Accept or amend ADR-033, which gates all three stages.
2. Stage 1: should an agreement with no LEI be blocked from counterparty-exposure reporting, or reported
   as unlinked? Unlinked is proposed, matching issuer concentration.
3. Stage 2: should the merger proposal require an independent reviewer under the Institutional profile,
   as governed Security Master revisions do?

## Validation

- Store parity: each new link has in-memory and PostgreSQL tests, following `EntityMasterPostgresTests`.
- Consumers: exposure grouping tests with synthetic hierarchies, including a parent change mid-period.
- Docs: update the ADR-033 Implementation Links table as each stage lands.
