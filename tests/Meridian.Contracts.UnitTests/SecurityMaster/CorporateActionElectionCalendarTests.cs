using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Contracts.UnitTests.SecurityMaster;

public sealed class CorporateActionElectionCalendarTests
{
    [Fact]
    public void Read_PreservesActualSourceOffsetAndEvidence()
    {
        var payload = JsonSerializer.SerializeToElement(new
        {
            electionCalendar = new
            {
                schemaVersion = 1,
                cutoffs = new[] { new { id = "custodian-v2", label = "Custodian instruction", deadlineAt = "2026-11-10T23:30:00-05:00", source = "Retained notice revision 2", evidenceReference = "document://custodian/notice/v2" } },
            },
        });
        var result = CorporateActionElectionCalendar.Read(payload);
        result.EvidenceState.Should().Be("Available");
        var cutoff = result.Cutoffs.Should().ContainSingle().Subject;
        cutoff.DeadlineAt.Offset.Should().Be(TimeSpan.FromHours(-5));
        cutoff.DeadlineAt.UtcDateTime.Day.Should().Be(11);
        cutoff.EvidenceReference.Should().Be("document://custodian/notice/v2");
    }

    [Theory]
    [InlineData("2026-11-10")]
    [InlineData("2026-11-10T16:00:00")]
    [InlineData("2026-02-30T16:00:00Z")]
    public void Read_RejectsMissingTimeOffsetOrInvalidDate(string deadlineAt)
    {
        var payload = JsonSerializer.SerializeToElement(new { electionCalendar = new { schemaVersion = 1,
            cutoffs = new[] { new { id = "deadline", label = "Election", deadlineAt, source = "Custodian", evidenceReference = "document://notice" } } } });
        var result = CorporateActionElectionCalendar.Read(payload);
        result.EvidenceState.Should().Be("InvalidEvidence");
        result.Cutoffs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"offerExpiryDate\":\"2026-11-12\",\"exDate\":\"2026-11-10\"}")]
    public void Read_DoesNotDeriveAnElectionCutoff(string json)
    {
        var result = CorporateActionElectionCalendar.Read(JsonSerializer.Deserialize<JsonElement>(json));
        result.EvidenceState.Should().Be("MissingEvidence");
        result.Cutoffs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{\"electionCalendar\":{\"schemaVersion\":\"1\",\"cutoffs\":[]}}")]
    [InlineData("{\"electionCalendar\":{\"schemaVersion\":2,\"cutoffs\":[]}}")]
    [InlineData("{\"electionCalendar\":{\"schemaVersion\":1,\"cutoffs\":[{}]}}")]
    public void Read_InvalidDeclarationIsExplicitAndDoesNotThrow(string json)
    {
        CorporateActionElectionCalendar.Read(JsonSerializer.Deserialize<JsonElement>(json)).EvidenceState.Should().Be("InvalidEvidence");
    }
}
