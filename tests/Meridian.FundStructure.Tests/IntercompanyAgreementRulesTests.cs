using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class IntercompanyAgreementRulesTests
{
    private const string RetainedDraftJson =
        """{"code":"IC-001","name":"Synthetic loan","partyOneId":"11111111-1111-1111-1111-111111111111","partyOneRole":"Lender","partyTwoId":"22222222-2222-2222-2222-222222222222","partyTwoRole":"Borrower","agreementType":"Loan","currency":"USD","effectiveFrom":"2026-01-01","effectiveTo":"2027-01-01","terms":{"fixedAnnualRate":"0.1234567890123456789012345678","rateDescription":"Fixed annual","scheduleDescription":"Monthly","allocationDescription":"Pro rata","settlementDays":2,"notes":"Retained terms"},"evidence":[{"reference":"vault:agreement","artifactId":"artifact-1","contentHash":"abcdef"},{"reference":"vault:approval"}],"action":"Amend","purpose":"Working capital"}""";

    [Fact]
    public void RetainedCommandAndReviewKeepTheirCanonicalFingerprints()
    {
        const string commandJson =
            """{"commandId":"33333333-3333-3333-3333-333333333333","agreementId":"44444444-4444-4444-4444-444444444444","expectedAggregateVersion":7,"proposalId":"55555555-5555-5555-5555-555555555555","draft":"""
            + RetainedDraftJson
            + ""","rationale":"Reviewed terms","submittedHash":"retained-reviewed-hash"}""";
        const string reviewJson = """{"draft":""" + RetainedDraftJson
            + ""","baseAgreementVersion":2,"predecessorAgreementVersion":3,"rationale":"Reviewed terms","agreementId":"44444444-4444-4444-4444-444444444444","proposalId":"55555555-5555-5555-5555-555555555555"}""";
        var command = JsonSerializer.Deserialize(commandJson, FundStructureContractsJsonContext.Default.IntercompanyAgreementCommandDto)!;
        var review = JsonSerializer.Deserialize(reviewJson, FundStructureContractsJsonContext.Default.IntercompanyAgreementReviewPayloadDto)!;

        Assert.Equal(commandJson, JsonSerializer.Serialize(command, FundStructureContractsJsonContext.Default.IntercompanyAgreementCommandDto));
        Assert.Equal(reviewJson, JsonSerializer.Serialize(review, FundStructureContractsJsonContext.Default.IntercompanyAgreementReviewPayloadDto));
        Assert.Equal("d98b8d9f11086f30e9894a85116d324303b6f07a1eaeae39e515611110b0464c", IntercompanyAgreementRules.CommandHash(command));
        Assert.Equal("74eb50e9bd0e216d950d2078dbc3cf9cbf54d0e3e177bf84a7e814d66ead527a", IntercompanyAgreementRules.ReviewedHash(review));
    }

    [Fact]
    public void DraftNormalizationPreservesMeaningfulTermsAndRequiredFieldRules()
    {
        var draft = JsonSerializer.Deserialize(RetainedDraftJson, FundStructureContractsJsonContext.Default.IntercompanyAgreementDraftDto)! with
        {
            Code = " IC-001 ",
            Name = " Synthetic loan ",
            Currency = " usd ",
            PartyOneRole = " Lender ",
            PartyTwoRole = " Borrower ",
            Purpose = "\t\r\n\u2003",
            Terms = new(RateDescription: null, ScheduleDescription: string.Empty, AllocationDescription: "\t\u2003", Notes: "\u2003 Retain  Case\tinside \u2003"),
            Evidence = [new(" vault:agreement ")]
        };

        var normalized = IntercompanyAgreementRules.Normalize(draft);

        Assert.Equal("IC-001", normalized.Code);
        Assert.Equal("Synthetic loan", normalized.Name);
        Assert.Equal("USD", normalized.Currency);
        Assert.Equal("Lender", normalized.PartyOneRole);
        Assert.Equal("Borrower", normalized.PartyTwoRole);
        Assert.Equal("vault:agreement", Assert.Single(normalized.Evidence!).Reference);
        Assert.Null(normalized.Purpose);
        Assert.Null(normalized.Terms!.RateDescription);
        Assert.Null(normalized.Terms.ScheduleDescription);
        Assert.Null(normalized.Terms.AllocationDescription);
        Assert.Equal("Retain  Case\tinside", normalized.Terms.Notes);
        IntercompanyAgreementRules.ValidateComplete(normalized);

        var missingCode = IntercompanyAgreementRules.Normalize(draft with { Code = null! });
        Assert.Equal(string.Empty, missingCode.Code);
        Assert.Equal("Code and name are required.", Assert.Throws<IntercompanyAgreementValidationException>(
            () => IntercompanyAgreementRules.ValidateComplete(missingCode)).Message);
    }

    [Fact]
    public void WhitespaceOnlyTermsStillCannotCompleteAnAgreement()
    {
        var draft = JsonSerializer.Deserialize(RetainedDraftJson, FundStructureContractsJsonContext.Default.IntercompanyAgreementDraftDto)! with
        {
            Terms = new(RateDescription: "\u2003", ScheduleDescription: "\t", AllocationDescription: "\r\n", Notes: " ")
        };

        var normalized = IntercompanyAgreementRules.Normalize(draft);

        Assert.Equal("A terms explanation or populated terms is required.", Assert.Throws<IntercompanyAgreementValidationException>(
            () => IntercompanyAgreementRules.ValidateComplete(normalized)).Message);
    }
}
