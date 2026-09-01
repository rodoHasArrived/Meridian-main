using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

/// <summary>
/// The durable, Postgres-backed conflict store must detect the same golden-record conflicts as the
/// in-memory store and — the reason it exists — retain each resolution and its chosen winner across
/// process instances, so the audit guarantee survives restarts and horizontal scale-out.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PostgresSecurityMasterConflictServiceTests : IClassFixture<SecurityMasterDatabaseFixture>
{
    private readonly SecurityMasterDatabaseFixture _fixture;

    public PostgresSecurityMasterConflictServiceTests(SecurityMasterDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresSecurityMasterConflictService NewService(ISecurityMasterStore store)
        => new(store, _fixture.Options, NullLogger<PostgresSecurityMasterConflictService>.Instance);

    private static ISecurityMasterStore StoreReturning(params SecurityProjectionRecord[] projections)
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>()).Returns(projections);
        store.FindIdentifierCandidatesAsync(
                Arg.Any<IReadOnlyList<SecurityIdentifierDto>>(),
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => projections
                .Where(projection => !call.ArgAt<IReadOnlyCollection<Guid>>(1).Contains(projection.SecurityId))
                .ToArray());
        return store;
    }

    // primaryValue overrides the record-level canonical primary when the shared identifier
    // claim must not be it: migration 032 uniquely indexes the securities table on
    // (primary_identifier_kind, normalized_primary_identifier_value), so two distinct upserted
    // securities may share an identifier claim — the ambiguity under test — but never a
    // normalized record primary. Claims drive detection and candidate lookup, not the primary.
    private static SecurityProjectionRecord MakeProjection(
        Guid securityId, string identifierKind, string identifierValue, string provider,
        string? primaryValue = null)
    {
        var identifier = new SecurityIdentifierDto(
            Enum.Parse<SecurityIdentifierKind>(identifierKind, ignoreCase: true),
            identifierValue,
            IsPrimary: true,
            ValidFrom: DateTimeOffset.UtcNow.AddDays(-30),
            Provider: provider);

        return new SecurityProjectionRecord(
            SecurityId: securityId,
            AssetClass: "Equity",
            Status: SecurityStatusDto.Active,
            DisplayName: $"Test Security {securityId:N}",
            Currency: "USD",
            PrimaryIdentifierKind: identifierKind,
            PrimaryIdentifierValue: primaryValue ?? identifierValue,
            CommonTerms: JsonSerializer.SerializeToElement(new { currency = "USD" }),
            AssetSpecificTerms: JsonSerializer.SerializeToElement(new { }),
            Provenance: JsonSerializer.SerializeToElement(new { sourceSystem = provider }),
            Version: 1,
            EffectiveFrom: DateTimeOffset.UtcNow.AddDays(-30),
            EffectiveTo: null,
            Identifiers: new[] { identifier },
            Aliases: Array.Empty<SecurityAliasDto>());
    }

    [SecurityMasterDatabaseFact]
    public async Task GetOpenConflictsAsync_DetectsAndPersistsConflict()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();
        var store = StoreReturning(
            MakeProjection(securityA, "Isin", "US0378331005", "alpaca"),
            MakeProjection(securityB, "Isin", "US0378331005", "polygon"));

        var conflicts = await NewService(store).GetOpenConflictsAsync(CancellationToken.None);

        var conflict = conflicts.Should().ContainSingle(c => c.SecurityId == securityA).Subject;
        conflict.ConflictKind.Should().Be("IdentifierAmbiguity");
        conflict.FieldPath.Should().Contain("Isin");
        conflict.Status.Should().Be("Open");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_PersistsWinnerAndResolverAcrossInstances()
    {
        var securityA = Guid.NewGuid();
        var securityB = Guid.NewGuid();
        var store = StoreReturning(
            MakeProjection(securityA, "Cusip", "037833100", "provA"),
            MakeProjection(securityB, "Cusip", "037833100", "provB"));

        // Instance A detects and resolves.
        var serviceA = NewService(store);
        var open = await serviceA.GetOpenConflictsAsync(CancellationToken.None);
        var conflictId = open.Single(c => c.SecurityId == securityA).ConflictId;

        var resolved = await serviceA.ResolveAsync(
            new ResolveConflictRequest(conflictId, "Resolve", "operator@meridian.test", "Edgar is golden.", ChosenWinnerSource: "Edgar"),
            CancellationToken.None);
        resolved.Should().NotBeNull();
        resolved!.Status.Should().Be("Resolved");

        // A fresh instance reading the same database observes the durable resolution and its winner.
        var serviceB = NewService(store);
        var reloaded = await serviceB.GetConflictAsync(conflictId, CancellationToken.None);
        reloaded.Should().NotBeNull();
        reloaded!.Status.Should().Be("Resolved");
        reloaded.ResolvedWinnerSource.Should().Be("Edgar");
        reloaded.ResolvedBy.Should().Be("operator@meridian.test");
        reloaded.ResolvedReason.Should().Be("Edgar is golden.");
        reloaded.ResolvedAt.Should().NotBeNull();

        // The resolved conflict is excluded from the open list and cannot be re-resolved.
        var openAfter = await serviceB.GetOpenConflictsAsync(CancellationToken.None);
        openAfter.Should().NotContain(c => c.ConflictId == conflictId);

        var second = await serviceB.ResolveAsync(
            new ResolveConflictRequest(conflictId, "AcceptA", "operator.b@meridian.test"),
            CancellationToken.None);
        second.Should().BeNull("a conflict that is no longer Open cannot be re-resolved");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_FieldConflict_WritesWinnerFieldProvenanceWithTheClose()
    {
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US88160R1014", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US88160R1014", "provB") with
        {
            Currency = "EUR",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };

        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);

        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflict = open.Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);
        conflict.FieldPath.Should().Be("CommonTerms.currency");

        var resolved = await service.ResolveAsync(
            new ResolveConflictRequest(conflict.ConflictId, "AcceptB", "operator@meridian.test", "provB confirmed."),
            CancellationToken.None);
        resolved.Should().NotBeNull();
        resolved!.ResolvedWinnerSource.Should().Be("provB");

        // The winning attribution is durable field lineage, committed with the conflict close.
        var provenanceStore = new PostgresSecurityFieldProvenanceStore(_fixture.Options);
        var lineage = await provenanceStore.GetAsync(securityId, CancellationToken.None);
        var row = lineage.Should().ContainSingle().Subject;
        row.FieldPath.Should().Be("CommonTerms.currency");
        row.SourceSystem.Should().Be("provB");
        row.Origin.Should().Be(SecurityFieldProvenanceOrigins.ConflictResolution);
        row.OriginReference.Should().Be(conflict.ConflictId.ToString("D"));
        row.UpdatedBy.Should().Be("operator@meridian.test");
        row.AsOf.Should().BeNull(
            "the conflict row does not retain the winning source's own as-of, and an unknown as-of is null, never fabricated from the resolution time");
        row.RecordedAt.Should().NotBe(default, "the resolution time is carried by RecordedAt");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_FieldConflict_OriginalWinner_ShouldRemainOpenUntilValueIsRestored()
    {
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US5949181045", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US5949181045", "provB") with
        {
            Currency = "EUR",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };

        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var conflict = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        var act = () => service.ResolveAsync(
            new ResolveConflictRequest(
                conflict.ConflictId,
                "Resolve",
                "operator@meridian.test",
                "Original source selected for a later governed restore.",
                ChosenWinnerSource: "provA"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Apply the selected value through a governed amendment*");
        var canonical = await canonicalStore.GetProjectionAsync(securityId, CancellationToken.None);
        canonical.Should().NotBeNull();
        canonical!.Currency.Should().Be("EUR");
        var retained = await service.GetConflictAsync(conflict.ConflictId, CancellationToken.None);
        retained.Should().NotBeNull();
        retained!.Status.Should().Be("Open");
        retained.ResolvedWinnerSource.Should().BeNull();
        var lineage = await new PostgresSecurityFieldProvenanceStore(_fixture.Options)
            .GetAsync(securityId, CancellationToken.None);
        lineage.Should().BeEmpty(
            "provA cannot be asserted until its selected value is restored");
    }

    [SecurityMasterDatabaseFact]
    public async Task FieldProvenanceUpsert_StaleRecord_ShouldNotReplaceNewerAttribution()
    {
        var securityId = Guid.NewGuid();
        var store = new PostgresSecurityFieldProvenanceStore(_fixture.Options);
        var newerRecordedAt = new DateTimeOffset(2026, 6, 1, 12, 2, 0, TimeSpan.Zero);
        var newer = new SecurityFieldProvenanceRecord(
            securityId,
            "EconomicDefinition.Coupon",
            "new-provider",
            AsOf: newerRecordedAt,
            UpdatedBy: "new-operator",
            Confidence: 0.99m,
            Origin: SecurityFieldProvenanceOrigins.OperatorFieldEdit,
            OriginReference: "revision-new",
            RecordedAt: newerRecordedAt);
        await store.UpsertAsync(newer, CancellationToken.None);

        await store.UpsertAsync(newer with
        {
            SourceSystem = "stale-provider",
            UpdatedBy = "stale-operator",
            OriginReference = "revision-stale",
            RecordedAt = newerRecordedAt.AddMinutes(-1)
        }, CancellationToken.None);

        var retained = (await store.GetAsync(securityId, CancellationToken.None))
            .Should().ContainSingle().Subject;
        retained.SourceSystem.Should().Be("new-provider");
        retained.UpdatedBy.Should().Be("new-operator");
        retained.OriginReference.Should().Be("revision-new");
        retained.RecordedAt.Should().Be(newerRecordedAt);
    }

    [SecurityMasterDatabaseFact]
    public async Task RemoveAsync_VersionGuard_PreservesNewerAmendmentAttribution()
    {
        // Amendment v2's attribution failed twice and reaches its invalidation fallback AFTER
        // amendment v3 committed its newer CanonicalWrite row. The fallback deletes by wall-clock
        // cleared_at, so without the source-version guard it would erase v3's row (recorded before
        // the fallback's cleared_at) and leave conflict detection falling back to the wrong
        // record-level provider. The guard preserves any row carrying a HIGHER source version.
        var store = new PostgresSecurityFieldProvenanceStore(_fixture.Options);
        var securityId = Guid.NewGuid();
        var v3Row = new SecurityFieldProvenanceRecord(
            securityId,
            "EconomicTerms.couponRate",
            "provC",
            AsOf: null,
            UpdatedBy: "v3-operator",
            Confidence: null,
            Origin: SecurityFieldProvenanceOrigins.CanonicalWrite,
            OriginReference: "version:3",
            RecordedAt: DateTimeOffset.UtcNow.AddSeconds(-1),
            SourceVersion: 3);
        await store.UpsertAsync(v3Row, CancellationToken.None);

        await store.RemoveAsync(
            securityId, "EconomicTerms.couponRate", SecurityFieldProvenanceOrigins.CanonicalWrite,
            clearedAt: DateTimeOffset.UtcNow, maxSourceVersion: 2, CancellationToken.None);

        (await store.GetAsync(securityId, CancellationToken.None))
            .Should().ContainSingle(row => row.SourceVersion == 3,
                "the failed v2 invalidation must not erase the attribution v3 already committed");

        // Without the version bound (or at/above the row's version) the recorded_at guard applies
        // as before and the stale row is removable.
        await store.RemoveAsync(
            securityId, "EconomicTerms.couponRate", SecurityFieldProvenanceOrigins.CanonicalWrite,
            clearedAt: DateTimeOffset.UtcNow, maxSourceVersion: 3, CancellationToken.None);
        (await store.GetAsync(securityId, CancellationToken.None)).Should().BeEmpty();
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_ResolutionProvenance_CarriesTheLockedVersionPastOlderInvalidations()
    {
        // The resolution's persisted-value check runs against the securities row locked at a
        // specific version in the close transaction, and the provenance row it writes is ordered
        // by THAT version. A null version would make the row always-eligible under the
        // version-bounded invalidation an OLDER amendment's retry issues — v2's fallback could
        // delete the attribution a resolution recorded against v3.
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US4581401001", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US4581401001", "provB") with
        {
            Currency = "EUR",
            Version = 3,
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };

        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflict = open.Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        await service.ResolveAsync(
            new ResolveConflictRequest(conflict.ConflictId, "AcceptB", "operator@meridian.test", "provB confirmed."),
            CancellationToken.None);

        var provenanceStore = new PostgresSecurityFieldProvenanceStore(_fixture.Options);
        var row = (await provenanceStore.GetAsync(securityId, CancellationToken.None)).Should().ContainSingle().Subject;
        row.SourceVersion.Should().Be(3, "the resolution row is ordered by the projection version its close transaction locked");

        await provenanceStore.RemoveAsync(
            securityId, conflict.FieldPath, SecurityFieldProvenanceOrigins.ConflictResolution,
            clearedAt: DateTimeOffset.UtcNow.AddMinutes(1), maxSourceVersion: 2, CancellationToken.None);
        (await provenanceStore.GetAsync(securityId, CancellationToken.None)).Should().ContainSingle(
            "an invalidation bounded at version 2 must not delete a resolution recorded against version 3");

        await provenanceStore.RemoveAsync(
            securityId, conflict.FieldPath, SecurityFieldProvenanceOrigins.ConflictResolution,
            clearedAt: DateTimeOffset.UtcNow.AddMinutes(1), maxSourceVersion: 3, CancellationToken.None);
        (await provenanceStore.GetAsync(securityId, CancellationToken.None)).Should().BeEmpty(
            "an invalidation at or past the resolution's own version legitimately retires it");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_FieldConflict_PersistedValueMatchingNeitherCandidate_SupersedesTheConflict()
    {
        // provA (USD) vs provB (EUR) opened the conflict; a provC amendment persisted GBP — a
        // value matching NEITHER candidate. Resolving to either source is impossible (the value
        // guard rejects both), so instead of leaving a dead-end Open row the resolution attempt
        // closes it as Superseded, writes no winner and no field provenance, and says so.
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US02079K3059", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US02079K3059", "provB") with
        {
            Currency = "EUR",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };
        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var conflict = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        await canonicalStore.UpsertProjectionAsync(incoming with
        {
            Currency = "GBP",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provC" }),
            Version = 2
        }, CancellationToken.None);

        var act = () => service.ResolveAsync(
            new ResolveConflictRequest(
                conflict.ConflictId,
                "Resolve",
                "operator@meridian.test",
                "provB no longer matches the golden record.",
                ChosenWinnerSource: "provB"),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*superseded*");
        var retained = await service.GetConflictAsync(conflict.ConflictId, CancellationToken.None);
        retained!.Status.Should().Be("Superseded",
            "a persisted value matching neither candidate makes the conflict permanently unresolvable");
        retained.ResolvedWinnerSource.Should().BeNull("neither source supplied the persisted value");
        var lineage = await new PostgresSecurityFieldProvenanceStore(_fixture.Options)
            .GetAsync(securityId, CancellationToken.None);
        lineage.Should().BeEmpty("no field provenance may be fabricated for a superseded conflict");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_DismissalAfterThirdPartyReplacement_SupersedesInsteadOfClosing()
    {
        // A DISMISSAL revalidates the persisted value like a resolution does: dismissing asserts
        // the recorded candidates are equivalent, which is only meaningful while the persisted
        // value still matches one of them. Here a provC amendment slipped in before the dismissal
        // acquired the conflict lock and persisted GBP — a value matching neither candidate — so
        // closing the stale assessment as Dismissed would erase a live disagreement that
        // post-write reconciliation then skips (it ignores closed rows). The dismissal instead
        // supersedes the obsolete row in the same governed transaction.
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US64110L1061", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US64110L1061", "provB") with
        {
            Currency = "EUR",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };
        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var conflict = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        await canonicalStore.UpsertProjectionAsync(incoming with
        {
            Currency = "GBP",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provC" }),
            Version = 2
        }, CancellationToken.None);

        var act = () => service.ResolveAsync(
            new ResolveConflictRequest(
                conflict.ConflictId,
                "Dismiss",
                "operator@meridian.test",
                "Values are equivalent.",
                ChosenWinnerSource: "provB"),
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*superseded*");
        (await service.GetConflictAsync(conflict.ConflictId, CancellationToken.None))!
            .Status.Should().Be("Superseded",
                "the dismissal targeted a stale assessment whose candidates were both replaced by a third source");
    }

    [SecurityMasterDatabaseFact]
    public async Task RecordFieldConflictsAsync_CanonicalWriteReplacingBothCandidates_SupersedesOnWrite()
    {
        // The proactive half of the same rule: the canonical write that obsoletes both candidates
        // retires the open conflict immediately, so the queue never surfaces an actionable-looking
        // row whose resolution flow cannot complete.
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US02079K1045", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US02079K1045", "provB") with
        {
            Currency = "EUR",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };
        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var conflict = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        var thirdSource = incoming with
        {
            Currency = "GBP",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provC" }),
            Version = 2
        };
        await canonicalStore.UpsertProjectionAsync(thirdSource, CancellationToken.None);
        await service.RecordFieldConflictsAsync(incoming, thirdSource, CancellationToken.None);
        // The obsolete-conflict sweep runs only AFTER the canonical write durably persists.
        await service.ReconcileOpenFieldConflictsAsync(thirdSource, CancellationToken.None);

        var retained = await service.GetConflictAsync(conflict.ConflictId, CancellationToken.None);
        retained!.Status.Should().Be("Superseded",
            "the GBP write matches neither USD nor EUR, so the write itself retires the stale conflict");
        (await service.GetOpenConflictsAsync(CancellationToken.None))
            .Should().NotContain(c => c.ConflictId == conflict.ConflictId);
    }

    [SecurityMasterDatabaseFact]
    public async Task RecordFieldConflictsAsync_CandidateRevisingItsOwnValue_RefreshesTheCandidateAndStaysResolvable()
    {
        // provA (USD) vs provB (EUR) disagree; provB then revises ITS OWN value to GBP. The
        // disagreement is still live, so the sweep refreshes provB's candidate to GBP instead of
        // retiring the conflict as a third-source replacement — and resolving to provB then
        // succeeds because the persisted value matches the refreshed candidate.
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US67066G1040", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US67066G1040", "provB") with
        {
            Currency = "EUR",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };
        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var conflict = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        var revision = incoming with { Currency = "GBP", Version = 2 };
        await canonicalStore.UpsertProjectionAsync(revision, CancellationToken.None);
        await service.RecordFieldConflictsAsync(incoming, revision, CancellationToken.None);
        // The obsolete-conflict sweep runs only AFTER the canonical write durably persists.
        await service.ReconcileOpenFieldConflictsAsync(revision, CancellationToken.None);

        var refreshed = await service.GetConflictAsync(conflict.ConflictId, CancellationToken.None);
        refreshed!.Status.Should().Be("Open",
            "a candidate revising its own value leaves the cross-source disagreement live");
        refreshed.ValueB.Should().Be("GBP", "the revising candidate's recorded value tracks its live assertion");

        var resolved = await service.ResolveAsync(
            new ResolveConflictRequest(
                conflict.ConflictId,
                "Resolve",
                "operator@meridian.test",
                "provB's live value wins.",
                ChosenWinnerSource: "provB"),
            CancellationToken.None);
        resolved!.Status.Should().Be("Resolved",
            "the refreshed candidate matches the persisted value, so the resolution completes");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_FieldConflict_ThirdSourceAmendedUnrelatedField_StillResolvesWhenValueMatches()
    {
        // Record-level provenance flips on EVERY amendment, including one from an unrelated third
        // source that did not touch the conflicted field. The resolution guard compares the
        // persisted FIELD VALUE against the selected source's asserted value — it must not require
        // the whole record's current source to equal the selected source, or any provider-C
        // amendment makes a valid provider-A/B resolution permanently impossible.
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US0231351067", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US0231351067", "provB") with
        {
            Currency = "EUR",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };
        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        await canonicalStore.UpsertProjectionAsync(incoming, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var conflict = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        // provider-C amends the record without changing the conflicted currency field: the record's
        // provenance now names provC, but the persisted currency is still provB's EUR.
        await canonicalStore.UpsertProjectionAsync(incoming with
        {
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provC" }),
            Version = 2
        }, CancellationToken.None);

        var resolved = await service.ResolveAsync(
            new ResolveConflictRequest(
                conflict.ConflictId,
                "Resolve",
                "operator@meridian.test",
                "provB's value is the persisted golden value.",
                ChosenWinnerSource: "provB"),
            CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.Status.Should().Be("Resolved");
        resolved.ResolvedWinnerSource.Should().Be("provB");
        var lineage = await new PostgresSecurityFieldProvenanceStore(_fixture.Options)
            .GetAsync(securityId, CancellationToken.None);
        lineage.Should().ContainSingle().Which.SourceSystem.Should().Be("provB");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_FieldConflict_DecimalValueWithDifferentScale_StillResolves()
    {
        // The value guard must compare decimal fields numerically: the selected source asserted
        // "6.00" while the canonical document carries the economically identical "6.0". An ordinal
        // comparison would leave the conflict permanently open after any amendment re-serialized
        // the value with different precision.
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US00206R1023", "provA") with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new { couponRate = 6.25m })
        };
        var incoming = MakeProjection(securityId, "Isin", "US00206R1023", "provB") with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new { couponRate = 6.00m }),
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };
        var canonicalStore = new PostgresSecurityMasterStore(_fixture.Options);
        // The canonical document persists the same coupon at a different scale than the asserted text.
        await canonicalStore.UpsertProjectionAsync(incoming with
        {
            AssetSpecificTerms = JsonSerializer.SerializeToElement(new { couponRate = 6.0m })
        }, CancellationToken.None);
        var service = NewService(canonicalStore);
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var conflict = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single(c =>
            c.SecurityId == securityId && c.FieldPath == "EconomicTerms.couponRate");

        var resolved = await service.ResolveAsync(
            new ResolveConflictRequest(
                conflict.ConflictId,
                "Resolve",
                "operator@meridian.test",
                "provB's coupon is the persisted golden value.",
                ChosenWinnerSource: "provB"),
            CancellationToken.None);

        resolved.Should().NotBeNull();
        resolved!.Status.Should().Be("Resolved",
            "6.00 and 6.0 are the same coupon; precision must not block the close");
        resolved.ResolvedWinnerSource.Should().Be("provB");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_DismissalOrIdentifierConflict_WritesNoFieldProvenance()
    {
        var securityId = Guid.NewGuid();
        var previous = MakeProjection(securityId, "Isin", "US4642872000", "provA");
        var incoming = MakeProjection(securityId, "Isin", "US4642872000", "provB") with
        {
            Currency = "GBP",
            Provenance = JsonSerializer.SerializeToElement(new { sourceSystem = "provB" })
        };

        var service = NewService(StoreReturning());
        await service.RecordFieldConflictsAsync(previous, incoming, CancellationToken.None);
        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflict = open.Single(c =>
            c.SecurityId == securityId && c.ConflictKind == SecurityMasterConflictKinds.CommonTermMismatch);

        // A dismissal asserts equivalence, not attribution: no field provenance is written, but the
        // acknowledged source (required by the workbench's DismissAsEquivalent flow) is persisted on
        // the conflict row so the durable record matches what the workbench reported.
        var dismissed = await service.ResolveAsync(
            new ResolveConflictRequest(
                conflict.ConflictId, "Dismiss", "operator@meridian.test", "Values equivalent.",
                ChosenWinnerSource: "provB"),
            CancellationToken.None);
        dismissed.Should().NotBeNull();
        dismissed!.Status.Should().Be("Dismissed");
        dismissed.ResolvedWinnerSource.Should().Be("provB",
            "the workbench reports the acknowledged source, so the authoritative row must carry it too");

        var provenanceStore = new PostgresSecurityFieldProvenanceStore(_fixture.Options);
        var lineage = await provenanceStore.GetAsync(securityId, CancellationToken.None);
        lineage.Should().BeEmpty("dismissals and identifier-ownership resolutions carry no field-source winner");
    }

    [SecurityMasterDatabaseFact]
    public async Task ResolveAsync_WhenConflictNotFound_ReturnsNull()
    {
        var result = await NewService(StoreReturning()).ResolveAsync(
            new ResolveConflictRequest(Guid.NewGuid(), "AcceptA", "test"),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task RecordConflictsForProjectionAsync_PersistsIngestTimeConflict()
    {
        var existingId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var existing = MakeProjection(existingId, "Figi", "BBG000B9XRY4", "openfigi");
        var incoming = MakeProjection(newId, "Figi", "BBG000B9XRY4", "polygon");

        // Universe already holds the existing security; the incoming projection collides on its FIGI.
        var store = StoreReturning(existing, incoming);
        var service = NewService(store);

        await service.RecordConflictsForProjectionAsync(incoming, CancellationToken.None);

        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        open.Should().Contain(c => c.SecurityId == newId && c.FieldPath.Contains("Figi"));
    }

    [SecurityMasterDatabaseFact]
    public async Task FindIdentifierCandidatesAsync_UsesNormalizedIdentifierIndexAndExcludesSubjects()
    {
        var existing = MakeProjection(Guid.NewGuid(), "Isin", "US-0378331005", "provider-a", primaryValue: $"CANON-{Guid.NewGuid():N}");
        var unrelated = MakeProjection(Guid.NewGuid(), "Isin", "US5949181045", "provider-b", primaryValue: $"CANON-{Guid.NewGuid():N}");
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        await store.UpsertProjectionAsync(existing, CancellationToken.None);
        await store.UpsertProjectionAsync(unrelated, CancellationToken.None);

        var lookup = new SecurityIdentifierDto(
            SecurityIdentifierKind.Isin,
            "us 0378331005",
            IsPrimary: true,
            ValidFrom: DateTimeOffset.UtcNow.AddDays(-1));
        var candidates = await store.FindIdentifierCandidatesAsync(
            [lookup],
            [unrelated.SecurityId],
            CancellationToken.None);

        candidates.Select(candidate => candidate.SecurityId).Should().Contain(existing.SecurityId);
        candidates.Select(candidate => candidate.SecurityId).Should().NotContain(unrelated.SecurityId);
    }

    [SecurityMasterDatabaseFact]
    public async Task ProviderScopedIdentifier_DoesNotConflictAcrossProviders()
    {
        var value = $"SCOPED-{Guid.NewGuid():N}";
        var first = MakeProjection(Guid.NewGuid(), "ProviderSymbol", value, "provider-a", primaryValue: $"{value}-A");
        var second = MakeProjection(Guid.NewGuid(), "ProviderSymbol", value, "provider-b", primaryValue: $"{value}-B");
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        await store.UpsertProjectionAsync(first, CancellationToken.None);
        await store.UpsertProjectionAsync(second, CancellationToken.None);

        var providerACandidates = await store.FindIdentifierCandidatesAsync(
            [second.Identifiers[0] with { Provider = "provider-a", NormalizedProvider = null }],
            [second.SecurityId],
            CancellationToken.None);
        providerACandidates.Select(candidate => candidate.SecurityId).Should().Contain(first.SecurityId);

        var providerBCandidates = await store.FindIdentifierCandidatesAsync(
            [second.Identifiers[0]],
            [second.SecurityId],
            CancellationToken.None);
        providerBCandidates.Select(candidate => candidate.SecurityId).Should().NotContain(first.SecurityId);

        var open = await NewService(store).GetOpenConflictsAsync(CancellationToken.None);
        open.Should().NotContain(conflict =>
            conflict.ValueA == first.SecurityId.ToString() || conflict.ValueB == first.SecurityId.ToString());
    }

    [SecurityMasterDatabaseFact]
    public async Task GetOpenConflictsAsync_ReopensDetectorSupersededConflictWithSameId()
    {
        var boundary = DateTimeOffset.UtcNow;
        var value = $"REOPEN-{Guid.NewGuid():N}";
        var first = MakeProjection(Guid.NewGuid(), "Ticker", value, "XNAS", primaryValue: $"{value}-A");
        var second = MakeProjection(Guid.NewGuid(), "Ticker", value, "XNAS", primaryValue: $"{value}-B");
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        await store.UpsertProjectionAsync(first, CancellationToken.None);
        await store.UpsertProjectionAsync(second, CancellationToken.None);
        var service = NewService(store);

        var original = (await service.GetOpenConflictsAsync(CancellationToken.None)).Single(conflict =>
            (conflict.ValueA == first.SecurityId.ToString() && conflict.ValueB == second.SecurityId.ToString())
            || (conflict.ValueA == second.SecurityId.ToString() && conflict.ValueB == first.SecurityId.ToString()));

        await store.UpsertProjectionAsync(first with
        {
            Version = 2,
            Identifiers = [first.Identifiers[0] with { ValidTo = boundary }]
        }, CancellationToken.None);
        await store.UpsertProjectionAsync(second with
        {
            Version = 2,
            Identifiers = [second.Identifiers[0] with { ValidFrom = boundary }]
        }, CancellationToken.None);
        (await service.GetOpenConflictsAsync(CancellationToken.None))
            .Should().NotContain(conflict => conflict.ConflictId == original.ConflictId);

        await store.UpsertProjectionAsync(first with
        {
            Version = 3,
            Identifiers = [first.Identifiers[0] with { ValidTo = null }]
        }, CancellationToken.None);
        var reopened = (await service.GetOpenConflictsAsync(CancellationToken.None))
            .Single(conflict => conflict.ConflictId == original.ConflictId);

        reopened.Status.Should().Be("Open");
        reopened.ResolvedBy.Should().BeNull();
        reopened.ResolvedReason.Should().BeNull();
    }

    [SecurityMasterDatabaseFact]
    public async Task ConcurrentRefreshAndIngest_LeaveOneOpenConflict()
    {
        var value = $"RACE-{Guid.NewGuid():N}";
        var first = MakeProjection(Guid.NewGuid(), "Ticker", value, "XNYS", primaryValue: $"{value}-A");
        var second = MakeProjection(Guid.NewGuid(), "Ticker", value, "XNYS", primaryValue: $"{value}-B");
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        await store.UpsertProjectionAsync(first, CancellationToken.None);
        await store.UpsertProjectionAsync(second, CancellationToken.None);
        var services = Enumerable.Range(0, 6).Select(_ => NewService(store)).ToArray();

        await Task.WhenAll(services.SelectMany(service => new Task[]
        {
            service.GetOpenConflictsAsync(CancellationToken.None),
            service.RecordConflictsForProjectionAsync(second, CancellationToken.None)
        }));

        var open = await services[0].GetOpenConflictsAsync(CancellationToken.None);
        open.Should().ContainSingle(conflict =>
            (conflict.ValueA == first.SecurityId.ToString() && conflict.ValueB == second.SecurityId.ToString())
            || (conflict.ValueA == second.SecurityId.ToString() && conflict.ValueB == first.SecurityId.ToString()));
    }

    [SecurityMasterDatabaseFact]
    public async Task GetOpenConflictsAsync_MigratesLegacyRawConflictIdAndPreservesResolution()
    {
        var value = $"Legacy{Guid.NewGuid():N}";
        var first = MakeProjection(Guid.NewGuid(), "InternalCode", value, "provider-a", primaryValue: $"{value}-A");
        var second = MakeProjection(Guid.NewGuid(), "InternalCode", value.ToLowerInvariant(), "provider-b", primaryValue: $"{value}-B");
        var store = new PostgresSecurityMasterStore(_fixture.Options);
        await store.UpsertProjectionAsync(first, CancellationToken.None);
        await store.UpsertProjectionAsync(second, CancellationToken.None);

        var legacyId = SecurityMasterConflictDetection.DeterministicConflictId(
            SecurityIdentifierKind.InternalCode.ToString(),
            value,
            first.SecurityId,
            second.SecurityId);
        var canonicalId = SecurityMasterConflictDetection.DeterministicConflictId(
            SecurityIdentifierKind.InternalCode.ToString(),
            SecurityIdentifierNormalizer.NormalizeValue(SecurityIdentifierKind.InternalCode, value),
            first.SecurityId,
            second.SecurityId);
        legacyId.Should().NotBe(canonicalId);

        await using (var connection = new Npgsql.NpgsqlConnection(_fixture.Options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText =
                $"""
                insert into {_fixture.Options.Schema}.security_master_conflicts (
                    conflict_id, security_id, conflict_kind, field_path,
                    provider_a, value_a, provider_b, value_b, detected_at, status,
                    resolved_winner_source, resolved_by, resolved_reason, resolved_at)
                values (
                    @conflict_id, @security_id, @conflict_kind, @field_path,
                    @provider_a, @value_a, @provider_b, @value_b, now(), 'Resolved',
                    @provider_a, 'operator@meridian.test', 'Legacy adjudication.', now());
                """;
            insert.Parameters.AddWithValue("conflict_id", legacyId);
            insert.Parameters.AddWithValue("security_id", first.SecurityId);
            insert.Parameters.AddWithValue("conflict_kind", SecurityMasterConflictKinds.IdentifierAmbiguity);
            insert.Parameters.AddWithValue("field_path", "Identifiers.InternalCode");
            insert.Parameters.AddWithValue("provider_a", "provider-a");
            insert.Parameters.AddWithValue("value_a", first.SecurityId.ToString());
            insert.Parameters.AddWithValue("provider_b", "provider-b");
            insert.Parameters.AddWithValue("value_b", second.SecurityId.ToString());
            await insert.ExecuteNonQueryAsync();
        }

        var service = NewService(store);
        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        open.Should().NotContain(conflict => conflict.ConflictId == canonicalId);
        (await service.GetConflictAsync(legacyId, CancellationToken.None)).Should().BeNull();
        var reconciled = await service.GetConflictAsync(canonicalId, CancellationToken.None);
        reconciled.Should().NotBeNull();
        reconciled!.Status.Should().Be("Resolved");
        reconciled.ResolvedBy.Should().Be("operator@meridian.test");
        reconciled.ResolvedReason.Should().Be("Legacy adjudication.");
    }
}
