using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;

namespace Meridian.Tests.AssetOperations;

public sealed class RetainedEvidenceIdentityValidatorTests
{
    [Fact]
    public void Validate_CompleteAcceptedEvidence_ShouldPass()
    {
        var evidence = CompleteEvidence();

        RetainedEvidenceIdentityValidator.Validate(evidence).Should().BeEmpty();
        RetainedEvidenceIdentityValidator.IsComplete(evidence).Should().BeTrue();
    }

    [Fact]
    public void Validate_NullEvidence_ShouldFailClosed()
    {
        RetainedEvidenceIdentityValidator.Validate(null)
            .Should().ContainSingle("Retained evidence identity is required.");
        RetainedEvidenceIdentityValidator.IsComplete(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(nameof(RetainedEvidenceIdentityDto.EvidenceId))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.EvidenceUri))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.ContentHashSha256))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.SourceSystem))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.SourceReference))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.ReviewStatus))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.ReviewedBy))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.ReviewedAtUtc))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.EffectiveDate))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.EvidenceVersion))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.RetainedAtUtc))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.RetainedBy))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.SubjectType))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.SubjectId))]
    public void Validate_EachMissingInvariant_ShouldFailClosed(string fieldName)
    {
        var valid = CompleteEvidence();
        var incomplete = fieldName switch
        {
            nameof(RetainedEvidenceIdentityDto.EvidenceId) => valid with { EvidenceId = " " },
            nameof(RetainedEvidenceIdentityDto.EvidenceUri) => valid with { EvidenceUri = " " },
            nameof(RetainedEvidenceIdentityDto.ContentHashSha256) => valid with { ContentHashSha256 = " " },
            nameof(RetainedEvidenceIdentityDto.SourceSystem) => valid with { SourceSystem = " " },
            nameof(RetainedEvidenceIdentityDto.SourceReference) => valid with { SourceReference = " " },
            nameof(RetainedEvidenceIdentityDto.ReviewStatus) => valid with { ReviewStatus = "Pending" },
            nameof(RetainedEvidenceIdentityDto.ReviewedBy) => valid with { ReviewedBy = " " },
            nameof(RetainedEvidenceIdentityDto.ReviewedAtUtc) => valid with { ReviewedAtUtc = default },
            nameof(RetainedEvidenceIdentityDto.EffectiveDate) => valid with { EffectiveDate = default },
            nameof(RetainedEvidenceIdentityDto.EvidenceVersion) => valid with { EvidenceVersion = 0 },
            nameof(RetainedEvidenceIdentityDto.RetainedAtUtc) => valid with { RetainedAtUtc = default },
            nameof(RetainedEvidenceIdentityDto.RetainedBy) => valid with { RetainedBy = " " },
            nameof(RetainedEvidenceIdentityDto.SubjectType) => valid with { SubjectType = " " },
            nameof(RetainedEvidenceIdentityDto.SubjectId) => valid with { SubjectId = " " },
            _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, null)
        };

        RetainedEvidenceIdentityValidator.Validate(incomplete)
            .Should().Contain(issue => issue.Contains(fieldName, StringComparison.Ordinal));
        RetainedEvidenceIdentityValidator.IsComplete(incomplete).Should().BeFalse();
    }

    [Theory]
    [InlineData("relative/evidence/1", nameof(RetainedEvidenceIdentityDto.EvidenceUri))]
    [InlineData("not-a-sha256", nameof(RetainedEvidenceIdentityDto.ContentHashSha256))]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", nameof(RetainedEvidenceIdentityDto.ContentHashSha256))]
    public void Validate_MalformedIdentityOrHash_ShouldFailClosed(string value, string fieldName)
    {
        var valid = CompleteEvidence();
        var malformed = fieldName switch
        {
            nameof(RetainedEvidenceIdentityDto.EvidenceUri) => valid with { EvidenceUri = value },
            nameof(RetainedEvidenceIdentityDto.ContentHashSha256) => valid with { ContentHashSha256 = value },
            _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, null)
        };

        RetainedEvidenceIdentityValidator.Validate(malformed)
            .Should().Contain(issue => issue.Contains(fieldName, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(nameof(RetainedEvidenceIdentityDto.ReviewedAtUtc))]
    [InlineData(nameof(RetainedEvidenceIdentityDto.RetainedAtUtc))]
    public void Validate_NonUtcTimestamp_ShouldFailClosed(string fieldName)
    {
        var valid = CompleteEvidence();
        var nonUtc = DateTimeOffset.Parse("2026-07-01T12:00:00-07:00");
        var malformed = fieldName == nameof(RetainedEvidenceIdentityDto.ReviewedAtUtc)
            ? valid with { ReviewedAtUtc = nonUtc }
            : valid with { RetainedAtUtc = nonUtc };

        RetainedEvidenceIdentityValidator.Validate(malformed)
            .Should().Contain(issue => issue.Contains(fieldName, StringComparison.Ordinal));
    }

    internal static RetainedEvidenceIdentityDto CompleteEvidence(Guid? securityId = null)
    {
        var subjectId = securityId ?? Guid.Parse("11111111-1111-1111-1111-111111111111");
        var contentHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("retained provider statement bytes")))
            .ToLowerInvariant();
        return new RetainedEvidenceIdentityDto(
            EvidenceId: "evidence-provider-statement-2026-06-v3",
            EvidenceUri: "evidence://asset-operations/provider-statement-2026-06-v3",
            ContentHashSha256: contentHash,
            SourceSystem: "Custodian",
            SourceReference: "statement-2026-06-v3",
            ReviewStatus: RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            ReviewedBy: "controller@meridian.local",
            ReviewedAtUtc: DateTimeOffset.Parse("2026-07-01T18:00:00Z"),
            EffectiveDate: new DateOnly(2026, 6, 30),
            EvidenceVersion: 3,
            RetainedAtUtc: DateTimeOffset.Parse("2026-07-01T18:30:00Z"),
            RetainedBy: "evidence-retention@meridian.local",
            SubjectType: "security-master",
            SubjectId: subjectId.ToString("D"));
    }
}
