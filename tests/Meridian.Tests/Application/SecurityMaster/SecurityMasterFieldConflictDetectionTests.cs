using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Application.SecurityMaster;

/// <summary>
/// Field-level cross-source conflict detection — the golden-copy capability: when two source
/// systems disagree on an economic or common term of the same security, a typed conflict is
/// produced for the authority policy; equivalent spellings, missing fields, and same-source
/// revisions never conflict.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SecurityMasterFieldConflictDetectionTests
{
    private static readonly Guid SecurityId = Guid.Parse("fcfcfcfc-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset DetectedAt = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    private static SecurityProjectionRecord Record(
        string sourceSystem,
        object assetSpecificTerms,
        string currency = "USD",
        object? commonTerms = null)
        => new(
            SecurityId,
            "Bond",
            SecurityStatusDto.Active,
            "Cross-source bond",
            currency,
            "Cusip",
            "123456789",
            JsonSerializer.SerializeToElement(commonTerms ?? new { displayName = "Cross-source bond", currency }),
            JsonSerializer.SerializeToElement(assetSpecificTerms),
            Json($$"""{"sourceSystem":"{{sourceSystem}}","updatedBy":"feed","asOf":"2026-07-01T00:00:00Z"}"""),
            1,
            DateTimeOffset.UtcNow,
            null,
            [new SecurityIdentifierDto(SecurityIdentifierKind.Cusip, "123456789", true, DateTimeOffset.UtcNow)],
            []);

    [Fact]
    public void DetectFieldConflicts_IncumbentFieldSource_OverridesRecordProvenance()
    {
        // Provider A supplied countryOfRisk, provider B later touched only the coupon (record
        // provenance now names B), provider C asserts a different country. The conflict's
        // incumbent must be A — the field's recorded source — not B.
        var current = Record("providerB", new { maturityDate = "2030-01-15" },
            commonTerms: new { currency = "USD", countryOfRisk = "US" });
        var incoming = Record("providerC", new { maturityDate = "2030-01-15" },
            commonTerms: new { currency = "USD", countryOfRisk = "CA" });

        var conflicts = SecurityMasterConflictDetection.DetectFieldConflicts(
            current, incoming, DetectedAt,
            new Dictionary<string, string> { ["CommonTerms.countryOfRisk"] = "providerA" });

        var country = conflicts.Should().ContainSingle(conflict =>
            conflict.FieldPath == "CommonTerms.countryOfRisk").Subject;
        country.ProviderA.Should().Be("providerA",
            "per-field attribution names the incumbent; record provenance is only the fallback");
        country.ProviderB.Should().Be("providerC");
    }

    [Fact]
    public void ChangedGovernedFieldPaths_ClearedValue_CountsAsChanged()
    {
        // Attribution for a value that no longer exists is as stale as attribution for a replaced
        // one — absence transitions count as changes for retirement, even though conflict creation
        // keeps its both-values-present rule.
        var current = Record("providerA", new { maturityDate = "2030-01-15", couponRate = 4.25m },
            commonTerms: new { currency = "USD", countryOfRisk = "US" });
        var incoming = Record("providerA", new { maturityDate = "2030-01-15" },
            commonTerms: new { currency = "USD" });

        var changed = SecurityMasterConflictDetection.ChangedGovernedFieldPaths(current, incoming);

        changed.Should().Contain("EconomicTerms.couponRate");
        changed.Should().Contain("CommonTerms.countryOfRisk");

        // Conflict creation stays both-present: a cleared field is incompleteness, not disagreement.
        var incomingOtherSource = Record("providerB", new { maturityDate = "2030-01-15" },
            commonTerms: new { currency = "USD" });
        SecurityMasterConflictDetection.DetectFieldConflicts(current, incomingOtherSource, DetectedAt)
            .Should().NotContain(conflict => conflict.FieldPath == "EconomicTerms.couponRate");
    }

    [Fact]
    public void ChangedGovernedFieldPaths_ClearedDayCount_CountsAsChanged()
    {
        // Day count follows the same absence-transition rule as every other governed comparator:
        // an amendment that removes the convention changed the field's hands, so its old
        // ConflictResolution attribution must retire like a replaced value's would.
        var current = Record("providerA", new { maturityDate = "2030-01-15", dayCountConvention = "30/360" });
        var incoming = Record("providerA", new { maturityDate = "2030-01-15" });

        var changed = SecurityMasterConflictDetection.ChangedGovernedFieldPaths(current, incoming);

        changed.Should().Contain("EconomicTerms.dayCountConvention");

        // Conflict creation keeps the both-present rule for day count too.
        var incomingOtherSource = Record("providerB", new { maturityDate = "2030-01-15" });
        SecurityMasterConflictDetection.DetectFieldConflicts(current, incomingOtherSource, DetectedAt)
            .Should().NotContain(conflict => conflict.FieldPath == "EconomicTerms.dayCountConvention");
    }

    [Fact]
    public void DetectFieldConflicts_SameRecordSource_StillConflictsAgainstFieldIncumbent()
    {
        // Provider A supplied countryOfRisk; provider B amended only the coupon (record provenance
        // on the stored copy now reads B) and NOW changes A's country too. Record-level sources
        // match on both sides, but the field's incumbent is A — the disagreement to open is
        // A-versus-B, and B revising its own coupon stays versioning.
        var current = Record("providerB", new { maturityDate = "2030-01-15", couponRate = 4.25m },
            commonTerms: new { currency = "USD", countryOfRisk = "US" });
        var incoming = Record("providerB", new { maturityDate = "2030-01-15", couponRate = 4.50m },
            commonTerms: new { currency = "USD", countryOfRisk = "CA" });

        var conflicts = SecurityMasterConflictDetection.DetectFieldConflicts(
            current, incoming, DetectedAt,
            new Dictionary<string, string>
            {
                ["CommonTerms.countryOfRisk"] = "providerA",
                ["EconomicTerms.couponRate"] = "providerB"
            });

        var country = conflicts.Should().ContainSingle(conflict =>
            conflict.FieldPath == "CommonTerms.countryOfRisk").Subject;
        country.ProviderA.Should().Be("providerA");
        country.ProviderB.Should().Be("providerB");
        conflicts.Should().NotContain(conflict => conflict.FieldPath == "EconomicTerms.couponRate",
            "the incumbent revising its own field is versioning, not a cross-source conflict");

        // Without per-field attribution the record-level short-circuit still applies.
        SecurityMasterConflictDetection.DetectFieldConflicts(current, incoming, DetectedAt)
            .Should().BeEmpty();
    }

    [Fact]
    public void DetectFieldConflicts_DifferingPrincipalSchedules_ProduceEconomicTermConflict()
    {
        // The contractual principal schedule drives calculated cash flows and ledger support, so a
        // source replacing another source's dated instalments is an economic-term disagreement.
        var current = Record("Bloomberg", new
        {
            maturityDate = "2031-01-15",
            principalSchedule = new object[]
            {
                new { paymentDate = "2028-06-15", amount = 30m },
                new { paymentDate = "2029-06-15", amount = 20m }
            }
        });
        var incoming = Record("Reuters", new
        {
            maturityDate = "2031-01-15",
            principalSchedule = new object[]
            {
                new { paymentDate = "2028-06-15", amount = 40m },
                new { paymentDate = "2030-06-15", amount = 20m }
            }
        });

        var conflicts = SecurityMasterConflictDetection.DetectFieldConflicts(current, incoming, DetectedAt);

        var schedule = conflicts.Should().ContainSingle(conflict =>
            conflict.FieldPath == "EconomicTerms.principalSchedule").Subject;
        schedule.ConflictKind.Should().Be(SecurityMasterConflictKinds.EconomicTermMismatch);
        schedule.ValueA.Should().Be("2028-06-15:30|2029-06-15:20");
        schedule.ValueB.Should().Be("2028-06-15:40|2030-06-15:20");
    }

    [Fact]
    public void DetectFieldConflicts_EquivalentPrincipalSchedules_DoNotConflict()
    {
        // Ordering and decimal scale are presentation, not economics: the normalized comparison
        // must not open a conflict for the same instalments spelled differently.
        var current = Record("Bloomberg", new
        {
            maturityDate = "2031-01-15",
            principalSchedule = new object[]
            {
                new { paymentDate = "2029-06-15", amount = 20m },
                new { paymentDate = "2028-06-15", amount = 30.00m }
            }
        });
        var incoming = Record("Reuters", new
        {
            maturityDate = "2031-01-15",
            principalSchedule = new object[]
            {
                new { paymentDate = "2028-06-15", amount = 30m },
                new { paymentDate = "2029-06-15", amount = 20.0m }
            }
        });

        var conflicts = SecurityMasterConflictDetection.DetectFieldConflicts(current, incoming, DetectedAt);

        conflicts.Should().NotContain(conflict => conflict.FieldPath == "EconomicTerms.principalSchedule");
    }

    [Fact]
    public void DetectFieldConflicts_SameDayInstalmentsSplitAcrossRows_DoNotConflict()
    {
        // A source splitting a payment date's amount across rows asserts the same economics as one
        // that records it whole: normalization sums same-day instalments before comparing, so
        // 30+20 on one date agrees with a single 50.
        var current = Record("Bloomberg", new
        {
            maturityDate = "2031-01-15",
            principalSchedule = new object[]
            {
                new { paymentDate = "2028-06-15", amount = 30m },
                new { paymentDate = "2028-06-15", amount = 20m }
            }
        });
        var incoming = Record("Reuters", new
        {
            maturityDate = "2031-01-15",
            principalSchedule = new object[]
            {
                new { paymentDate = "2028-06-15", amount = 50m }
            }
        });

        var conflicts = SecurityMasterConflictDetection.DetectFieldConflicts(current, incoming, DetectedAt);

        conflicts.Should().NotContain(conflict => conflict.FieldPath == "EconomicTerms.principalSchedule");
    }

    [Fact]
    public void DetectFieldConflicts_DisagreeingSources_ProduceTypedFieldConflicts()
    {
        // Bloomberg's copy says maturity 2030 / coupon 4.25 / risk country US; the Reuters revision
        // says 2031 / 4.50 / GB — three conflicts, each attributed to both sources.
        var current = Record("Bloomberg", new { maturityDate = "2030-01-15", couponRate = 4.25m },
            commonTerms: new { currency = "USD", countryOfRisk = "US" });
        var incoming = Record("Reuters", new { maturityDate = "2031-01-15", couponRate = 4.50m },
            commonTerms: new { currency = "USD", countryOfRisk = "GB" });

        var conflicts = SecurityMasterConflictDetection.DetectFieldConflicts(current, incoming, DetectedAt);

        conflicts.Should().HaveCount(3);
        conflicts.Should().OnlyContain(conflict =>
            conflict.SecurityId == SecurityId &&
            conflict.ProviderA == "Bloomberg" &&
            conflict.ProviderB == "Reuters" &&
            conflict.Status == "Open" &&
            conflict.DetectedAt == DetectedAt);

        var maturity = conflicts.Single(conflict => conflict.FieldPath == "EconomicTerms.maturityDate");
        maturity.ConflictKind.Should().Be(SecurityMasterConflictKinds.EconomicTermMismatch);
        maturity.ValueA.Should().Be("2030-01-15");
        maturity.ValueB.Should().Be("2031-01-15");

        var coupon = conflicts.Single(conflict => conflict.FieldPath == "EconomicTerms.couponRate");
        coupon.ValueA.Should().Be("4.25");
        coupon.ValueB.Should().Be("4.50");

        var country = conflicts.Single(conflict => conflict.FieldPath == "CommonTerms.countryOfRisk");
        country.ConflictKind.Should().Be(SecurityMasterConflictKinds.CommonTermMismatch);
        country.ValueA.Should().Be("US");
        country.ValueB.Should().Be("GB");
    }

    [Fact]
    public void DetectFieldConflicts_SameSourceRevision_ProducesNothing()
    {
        // The same source updating its own data is versioning, not a cross-source conflict.
        var current = Record("Bloomberg", new { maturityDate = "2030-01-15" });
        var incoming = Record("Bloomberg", new { maturityDate = "2031-01-15" });

        SecurityMasterConflictDetection.DetectFieldConflicts(current, incoming, DetectedAt)
            .Should().BeEmpty();
    }

    [Fact]
    public void DetectFieldConflicts_FieldMissingOnEitherSide_IsNotAConflict()
    {
        // Absence is incompleteness, not disagreement.
        var current = Record("Bloomberg", new { maturityDate = "2030-01-15" });
        var incoming = Record("Reuters", new { couponRate = 4.5m });

        SecurityMasterConflictDetection.DetectFieldConflicts(current, incoming, DetectedAt)
            .Should().BeEmpty();
    }

    [Fact]
    public void DetectFieldConflicts_EquivalentSpellings_NeverConflict()
    {
        // "maturity" and "maturityDate" are vendor aliases of the same term; "30/360" and
        // "Thirty360" parse to the same canonical day-count convention. Neither is a disagreement.
        var current = Record("Bloomberg", new { maturityDate = "2030-01-15", dayCountConvention = "30/360" });
        var incoming = Record("Reuters", new { maturity = "2030-01-15", dayCount = "Thirty360" });

        SecurityMasterConflictDetection.DetectFieldConflicts(current, incoming, DetectedAt)
            .Should().BeEmpty();
    }

    [Fact]
    public void DetectFieldConflicts_DifferentDayCountConventions_Conflict()
    {
        var current = Record("Bloomberg", new { dayCountConvention = "30/360" });
        var incoming = Record("Reuters", new { dayCountConvention = "ACT/360" });

        var conflicts = SecurityMasterConflictDetection.DetectFieldConflicts(current, incoming, DetectedAt);

        conflicts.Should().ContainSingle().Which.FieldPath.Should().Be("EconomicTerms.dayCountConvention");
    }

    [Fact]
    public void DetectFieldConflicts_ConflictIdIsSymmetricAcrossDirection()
    {
        // The same disagreement yields the same id no matter which side is the stored copy, so
        // re-detection after a flip cannot duplicate the conflict.
        var bloomberg = Record("Bloomberg", new { couponRate = 4.25m });
        var reuters = Record("Reuters", new { couponRate = 4.50m });

        var forward = SecurityMasterConflictDetection.DetectFieldConflicts(bloomberg, reuters, DetectedAt).Single();
        var reverse = SecurityMasterConflictDetection.DetectFieldConflicts(reuters, bloomberg, DetectedAt).Single();

        forward.ConflictId.Should().Be(reverse.ConflictId);
    }

    [Fact]
    public void ProvenanceReader_ReadsTheStoredShape_AndDegradesWhenMissing()
    {
        var provenance = SecurityMasterProvenanceReader.Read(
            Json("""{"sourceSystem":"Bloomberg","sourceRecordId":"r-1","asOf":"2026-07-01T00:00:00Z","updatedBy":"feed","reason":"refresh"}"""));

        provenance.SourceSystem.Should().Be("Bloomberg");
        provenance.SourceRecordId.Should().Be("r-1");
        provenance.AsOf.Should().Be(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
        provenance.UpdatedBy.Should().Be("feed");

        var field = provenance.ForField("EconomicTerms.couponRate");
        field.FieldPath.Should().Be("EconomicTerms.couponRate");
        field.SourceSystem.Should().Be("Bloomberg");
        field.AsOf.Should().Be(provenance.AsOf);
        field.Confidence.Should().BeNull();

        SecurityMasterProvenanceReader.Read(Json("null")).SourceSystem
            .Should().Be(SecurityMasterProvenanceReader.UnknownSource);
    }

    [Fact]
    public async Task RecordFieldConflictsAsync_PersistsConflictsIdempotently()
    {
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        var current = Record("Bloomberg", new { couponRate = 4.25m });
        var incoming = Record("Reuters", new { couponRate = 4.50m });

        await service.RecordFieldConflictsAsync(current, incoming, CancellationToken.None);
        await service.RecordFieldConflictsAsync(current, incoming, CancellationToken.None);

        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflict = open.Should().ContainSingle().Subject;
        conflict.ConflictKind.Should().Be(SecurityMasterConflictKinds.EconomicTermMismatch);
        conflict.FieldPath.Should().Be("EconomicTerms.couponRate");
        conflict.ProviderA.Should().Be("Bloomberg");
        conflict.ProviderB.Should().Be("Reuters");
    }

    [Fact]
    public async Task RecordFieldConflictsAsync_GovernedProfileFieldReplacedAcrossSources_OpensConflict()
    {
        // Governed profile fields ARE the economics of a profile-backed record: provider B
        // replacing provider A's commitment must open a conflict exactly like a coupon
        // disagreement would, instead of silently overwriting the golden record.
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());
        var service = new SecurityMasterConflictService(
            store,
            NullLogger<SecurityMasterConflictService>.Instance,
            Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        var current = Record("Bloomberg", new
        {
            customProfileId = "private-fund-interest",
            profileVersion = 1,
            profileFields = new { commitment = 1_000_000m, gpSponsor = "Meridian Growth Partners" }
        });
        var incoming = Record("Reuters", new
        {
            customProfileId = "private-fund-interest",
            profileVersion = 1,
            profileFields = new { commitment = 2_000_000m, gpSponsor = "Meridian Growth Partners" }
        });

        await service.RecordFieldConflictsAsync(current, incoming, CancellationToken.None);

        var open = await service.GetOpenConflictsAsync(CancellationToken.None);
        var conflict = open.Should().ContainSingle(c => c.FieldPath == "ProfileFields.commitment").Subject;
        conflict.ProviderA.Should().Be("Bloomberg");
        conflict.ProviderB.Should().Be("Reuters");
        conflict.ValueA.Should().Be("1000000");
        conflict.ValueB.Should().Be("2000000");
        open.Should().NotContain(c => c.FieldPath == "ProfileFields.gpSponsor",
            "an agreeing governed field is not a disagreement");
    }

    [Fact]
    public async Task RecordFieldConflictsAsync_UndeclaredProfileFieldKey_OpensNoConflict()
    {
        // Only fields the PINNED PROFILE declares are governed economics: two providers differing
        // on pass-through metadata (operatorNote) must not open an EconomicTermMismatch or mint
        // canonical field attribution.
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());
        var service = new SecurityMasterConflictService(
            store,
            NullLogger<SecurityMasterConflictService>.Instance,
            Meridian.ReferenceData.SecurityMaster.StaticSecurityAssetProfileCatalog.CreateDefault());

        var current = Record("Bloomberg", new
        {
            customProfileId = "private-fund-interest",
            profileVersion = 1,
            profileFields = new { commitment = 1_000_000m, operatorNote = "seed note" }
        });
        var incoming = Record("Reuters", new
        {
            customProfileId = "private-fund-interest",
            profileVersion = 1,
            profileFields = new { commitment = 1_000_000m, operatorNote = "different note" }
        });

        await service.RecordFieldConflictsAsync(current, incoming, CancellationToken.None);

        (await service.GetOpenConflictsAsync(CancellationToken.None))
            .Should().NotContain(c => c.FieldPath.StartsWith("ProfileFields."),
                "the profile does not declare operatorNote and the declared fields agree");
    }

    [Fact]
    public async Task RecordFieldConflictsAsync_CanonicalWriteReplacingBothCandidates_SupersedesTheObsoleteConflict()
    {
        // Bloomberg (4.25) and Reuters (4.50) opened a coupon conflict; a later amendment from a
        // THIRD source persists 5.00. The old conflict can never resolve — the durable store's
        // value guard rejects a winner whose value the record no longer carries — so it must close
        // as Superseded instead of surfacing an actionable-looking queue row whose resolution flow
        // cannot complete.
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        var bloomberg = Record("Bloomberg", new { couponRate = 4.25m });
        var reuters = Record("Reuters", new { couponRate = 4.50m });
        await service.RecordFieldConflictsAsync(bloomberg, reuters, CancellationToken.None);
        var opened = (await service.GetOpenConflictsAsync(CancellationToken.None)).Should().ContainSingle().Subject;

        var thirdSource = Record("IceData", new { couponRate = 5.00m });
        await service.RecordFieldConflictsAsync(reuters, thirdSource, CancellationToken.None);
        // The obsolete-conflict sweep runs only AFTER the canonical write durably persists.
        await service.ReconcileOpenFieldConflictsAsync(thirdSource, CancellationToken.None);

        var superseded = await service.GetConflictAsync(opened.ConflictId, CancellationToken.None);
        superseded!.Status.Should().Be("Superseded",
            "the canonical 5.00 write matches neither 4.25 nor 4.50, so the old conflict is obsolete");
        superseded.ResolvedWinnerSource.Should().BeNull("no source supplied the persisted value; a winner would be fabricated attribution");
        superseded.ResolvedReason.Should().Contain("matches neither");
        (await service.GetOpenConflictsAsync(CancellationToken.None))
            .Should().NotContain(conflict => conflict.ConflictId == opened.ConflictId);
    }

    [Fact]
    public async Task RecordFieldConflictsAsync_CanonicalWriteMatchingOneCandidate_KeepsTheConflictOpen()
    {
        // A write that re-asserts one CANDIDATE's value is not obsolescence: that candidate
        // remains a legal resolution, and the disagreement still needs an operator's decision.
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        var bloomberg = Record("Bloomberg", new { couponRate = 4.25m });
        var reuters = Record("Reuters", new { couponRate = 4.50m });
        await service.RecordFieldConflictsAsync(bloomberg, reuters, CancellationToken.None);
        var opened = (await service.GetOpenConflictsAsync(CancellationToken.None)).Should().ContainSingle().Subject;

        // Precision differs but the economics match Bloomberg's candidate exactly.
        var reassertion = Record("IceData", new { couponRate = 4.2500m });
        await service.RecordFieldConflictsAsync(reuters, reassertion, CancellationToken.None);
        // The obsolete-conflict sweep runs only AFTER the canonical write durably persists.
        await service.ReconcileOpenFieldConflictsAsync(reassertion, CancellationToken.None);

        var stillOpen = await service.GetConflictAsync(opened.ConflictId, CancellationToken.None);
        stillOpen!.Status.Should().Be("Open",
            "a persisted value matching a recorded candidate keeps that candidate resolvable");
    }

    [Fact]
    public async Task RecordFieldConflictsAsync_CandidateRevisingItsOwnValue_RefreshesTheCandidateInsteadOfSuperseding()
    {
        // Bloomberg (4.25) and Reuters (4.50) disagree; Reuters then revises ITS OWN value to
        // 4.75. Same-source detection records no replacement candidate for that write, and 4.75
        // matches neither recorded value — but Bloomberg and Reuters still disagree. The sweep
        // must refresh Reuters' candidate to the live 4.75, not retire the dispute as if a third
        // source had replaced both.
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        var bloomberg = Record("Bloomberg", new { couponRate = 4.25m });
        var reuters = Record("Reuters", new { couponRate = 4.50m });
        await service.RecordFieldConflictsAsync(bloomberg, reuters, CancellationToken.None);
        var opened = (await service.GetOpenConflictsAsync(CancellationToken.None)).Should().ContainSingle().Subject;

        var reutersRevision = Record("Reuters", new { couponRate = 4.75m });
        await service.RecordFieldConflictsAsync(reuters, reutersRevision, CancellationToken.None);
        // The obsolete-conflict sweep runs only AFTER the canonical write durably persists.
        await service.ReconcileOpenFieldConflictsAsync(reutersRevision, CancellationToken.None);

        var refreshed = await service.GetConflictAsync(opened.ConflictId, CancellationToken.None);
        refreshed!.Status.Should().Be("Open",
            "a candidate revising its own value leaves the cross-source disagreement live");
        refreshed.ValueB.Should().Be("4.75",
            "the revising candidate's recorded value must track its live assertion so it stays a resolvable winner");
        refreshed.ValueA.Should().Be(opened.ValueA, "the other candidate's assertion is untouched");
    }

    [Fact]
    public async Task ReconcileOpenFieldConflictsAsync_RefreshedRowMatchingANewerConflict_CoalescesIntoIt()
    {
        // A=4.25 vs B=4.50 opens a conflict; B revises to 4.75 (candidate refresh keeps the row
        // open); A then revises to 5.10 — pre-persist detection opens a NEW conflict for the same
        // provider pair under a different deterministic id. Refreshing the OLD row too would leave
        // two independently resolvable queue entries describing one live disagreement, so the
        // older row coalesces into the newer one.
        var store = Substitute.For<ISecurityMasterStore>();
        store.LoadAllAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SecurityProjectionRecord>());
        var service = new SecurityMasterConflictService(store, NullLogger<SecurityMasterConflictService>.Instance);

        var bloomberg = Record("Bloomberg", new { couponRate = 4.25m });
        var reuters = Record("Reuters", new { couponRate = 4.50m });
        await service.RecordFieldConflictsAsync(bloomberg, reuters, CancellationToken.None);
        var original = (await service.GetOpenConflictsAsync(CancellationToken.None)).Should().ContainSingle().Subject;

        var reutersRevised = Record("Reuters", new { couponRate = 4.75m });
        await service.RecordFieldConflictsAsync(reuters, reutersRevised, CancellationToken.None);
        await service.ReconcileOpenFieldConflictsAsync(reutersRevised, CancellationToken.None);

        var bloombergRevised = Record("Bloomberg", new { couponRate = 5.10m });
        await service.RecordFieldConflictsAsync(reutersRevised, bloombergRevised, CancellationToken.None);
        await service.ReconcileOpenFieldConflictsAsync(bloombergRevised, CancellationToken.None);

        var coalesced = await service.GetConflictAsync(original.ConflictId, CancellationToken.None);
        coalesced!.Status.Should().Be("Superseded",
            "the newer detection row carries the live values for the same provider pair");
        coalesced.ResolvedReason.Should().Contain("Coalesced");
        var openForField = (await service.GetOpenConflictsAsync(CancellationToken.None))
            .Where(conflict => conflict.FieldPath == "EconomicTerms.couponRate")
            .ToArray();
        openForField.Should().ContainSingle("one live disagreement must surface exactly one resolvable queue entry");
    }

    [Theory]
    [InlineData("ProfileFields.poolId", "001", "1", false)]
    [InlineData("ProfileFields.poolId", "001", "001", true)]
    [InlineData("CommonTerms.countryOfRisk", "051", "51", false)]
    [InlineData("EconomicTerms.paymentFrequency", "02", "2", false)]
    [InlineData("EconomicTerms.couponRate", "6.00", "6.0", true)]
    [InlineData("EconomicTerms.principalFace", "1000000", "1000000.00", true)]
    public void FieldValuesMatch_RestrictsNumericEqualityToNumericTermPaths(
        string fieldPath, string persisted, string candidate, bool expected)
    {
        // Numeric equality is meaningful only on the known numeric term paths: on Text, Enum, and
        // code-valued fields a numeric-looking string keeps its textual identity — a text pool ID
        // of "001" and "1" are DIFFERENT values, and matching them numerically would let a
        // resolution close a conflict for a value that was never applied.
        SecurityMasterConflictDetection.FieldValuesMatch(fieldPath, persisted, candidate)
            .Should().Be(expected);
    }
}
