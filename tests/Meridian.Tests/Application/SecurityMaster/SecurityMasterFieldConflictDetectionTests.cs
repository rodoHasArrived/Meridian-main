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
}
