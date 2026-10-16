using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.Ledger;

namespace Meridian.Tests.AssetOperations;

public sealed class AcquisitionTermsCompletionTests
{
    [Theory]
    [InlineData(BondAmortizationMethod.NoAmortization, null)]
    [InlineData(BondAmortizationMethod.StraightLine, null)]
    [InlineData(BondAmortizationMethod.ConstantYield, 0.05)]
    [InlineData(BondAmortizationMethod.ConstantYield, 0.0)]
    [InlineData(BondAmortizationMethod.ConstantYield, -0.99)]
    public void ReviewedCompletion_SuppliesOnlyMissingTermsAndPreservesDraft(BondAmortizationMethod method, double? yield)
    {
        var request = BuildRequest(method, yield is null ? null : (decimal)yield.Value);
        var original = JsonSerializer.Serialize(request.Candidate);

        var completed = SpineAcquisitionLotCompletion.Resolve(request)!;

        completed.Should().Be(request.Candidate.AssetLotMutation!.Acquisition! with
        { AmortizationMethod = method, EffectiveYield = request.AcquisitionTermsCompletion!.EffectiveYield });
        JsonSerializer.Serialize(request.Candidate).Should().Be(original);
        SpineAcquisitionLotCompletion.PostingApprovalEvidence(request).Should()
            .Contain(request.AcquisitionTermsCompletion.Evidence);
    }

    [Theory]
    [InlineData("lot")]
    [InlineData("draft")]
    [InlineData("method")]
    [InlineData("yield")]
    [InlineData("reviewer")]
    [InlineData("preparer")]
    [InlineData("date")]
    [InlineData("before-draft")]
    [InlineData("future-review")]
    [InlineData("unretained")]
    [InlineData("unreviewed")]
    [InlineData("duplicate-evidence")]
    [InlineData("already-complete")]
    [InlineData("units")]
    [InlineData("wrong-event")]
    [InlineData("missing-evidence")]
    [InlineData("invalid-method")]
    [InlineData("missing-method")]
    [InlineData("missing-yield")]
    [InlineData("yield-minus-one")]
    [InlineData("yield-below-minus-one")]
    public void UnboundOrUnsupportedCompletion_IsRefused(string fault)
    {
        var request = BuildRequest();
        var completion = request.AcquisitionTermsCompletion!;
        var evidence = completion.Evidence;
        request = fault switch
        {
            "lot" => request with { AcquisitionTermsCompletion = completion with { TaxLotRecordId = Guid.NewGuid() } },
            "draft" => request with { AcquisitionTermsCompletion = completion with { DraftedCandidateFingerprint = new string('a', 64) } },
            "method" => request with { AcquisitionTermsCompletion = completion with { AmortizationMethod = BondAmortizationMethod.StraightLine } },
            "yield" => request with { AcquisitionTermsCompletion = completion with { EffectiveYield = .05m } },
            "reviewer" => WithEvidence(request, evidence with { ReviewedBy = "another-reviewer" }),
            "preparer" => WithEvidence(request with { Actor = request.Candidate.Actor }, evidence with { ReviewedBy = request.Candidate.Actor }),
            "date" => WithEvidence(request, evidence with { EffectiveDate = evidence.EffectiveDate.AddDays(1) }),
            "before-draft" => WithEvidence(request, evidence with { ReviewedAtUtc = request.Candidate.AccountingTimestamp.AddDays(-1) }),
            "future-review" => WithEvidence(request, evidence with { ReviewedAtUtc = DateTimeOffset.UtcNow.AddDays(1), RetainedAtUtc = DateTimeOffset.UtcNow.AddDays(2) }),
            "unretained" => WithEvidence(request, evidence with { RetainedAtUtc = evidence.ReviewedAtUtc.AddSeconds(-1) }),
            "unreviewed" => WithEvidence(request, evidence with { ReviewStatus = "Pending" }),
            "duplicate-evidence" => request with { ApprovalEvidence = [evidence] },
            "already-complete" => request with
            {
                Candidate = request.Candidate with
                {
                    AssetLotMutation = request.Candidate.AssetLotMutation! with
                    { Acquisition = request.Candidate.AssetLotMutation.Acquisition! with { AmortizationMethod = BondAmortizationMethod.NoAmortization } }
                }
            },
            "units" => request with
            {
                Candidate = request.Candidate with
                {
                    AssetLotMutation = request.Candidate.AssetLotMutation! with
                    { Acquisition = request.Candidate.AssetLotMutation.Acquisition! with { OriginalFace = null, BookedFactor = null, ParBasis = null } }
                }
            },
            "wrong-event" => request with { Candidate = request.Candidate with { SourceEventType = "AssetAccounting.Disposal" } },
            "missing-evidence" => request with { AcquisitionTermsCompletion = completion with { Evidence = null! } },
            "invalid-method" => Rebind(request, completion with { AmortizationMethod = (BondAmortizationMethod)999 }),
            "missing-method" => Rebind(request, completion with { AmortizationMethod = null }),
            "missing-yield" => Rebind(request, completion with { AmortizationMethod = BondAmortizationMethod.ConstantYield }),
            "yield-minus-one" => Rebind(request, completion with { AmortizationMethod = BondAmortizationMethod.ConstantYield, EffectiveYield = -1m }),
            "yield-below-minus-one" => Rebind(request, completion with { AmortizationMethod = BondAmortizationMethod.ConstantYield, EffectiveYield = -1.01m }),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };

        var act = () => SpineAcquisitionLotCompletion.Resolve(request);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OmittedMethodInCompletionJson_CannotDefaultToConstantYield()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.SerializeToNode(BuildRequest(BondAmortizationMethod.ConstantYield, .05m), options)!;
        json["acquisitionTermsCompletion"]!.AsObject().Remove("amortizationMethod");
        var restored = json.Deserialize<PostPostingRuleJournalCandidateRequestDto>(options)!;

        restored.AcquisitionTermsCompletion!.AmortizationMethod.Should().BeNull();
        var act = () => SpineAcquisitionLotCompletion.Resolve(restored);
        act.Should().Throw<InvalidOperationException>().WithMessage("*explicit amortization method*");
    }

    [Fact]
    public void ReviewedCompletion_RoundTripsThroughPostingRequestJson()
    {
        var request = BuildRequest(BondAmortizationMethod.ConstantYield, .05m);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var restored = JsonSerializer.Deserialize<PostPostingRuleJournalCandidateRequestDto>(
            JsonSerializer.Serialize(request, options), options)!;

        restored.AcquisitionTermsCompletion.Should().Be(request.AcquisitionTermsCompletion);
        SpineAcquisitionLotCompletion.Resolve(restored)!.EffectiveYield.Should().Be(.05m);
    }

    [Fact]
    public void OmittedCompletion_PreservesLegacyRequestWireShape()
    {
        var request = BuildRequest() with { AcquisitionTermsCompletion = null };
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().NotContain("acquisitionTermsCompletion");
        var restored = JsonSerializer.Deserialize<PostPostingRuleJournalCandidateRequestDto>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        restored.AcquisitionTermsCompletion.Should().BeNull();
        SpineAcquisitionLotCompletion.Resolve(restored)!.AmortizationMethod.Should().BeNull();
    }

    private static PostPostingRuleJournalCandidateRequestDto WithEvidence(
        PostPostingRuleJournalCandidateRequestDto request, RetainedEvidenceIdentityDto evidence)
        => request with { AcquisitionTermsCompletion = request.AcquisitionTermsCompletion! with { Evidence = evidence } };

    private static PostPostingRuleJournalCandidateRequestDto Rebind(
        PostPostingRuleJournalCandidateRequestDto request, AssetAcquisitionTermsCompletionDto completion)
        => request with
        {
            AcquisitionTermsCompletion = completion with
            { Evidence = completion.Evidence with { SubjectId = completion.ApprovalSubjectId() } }
        };

    private static PostPostingRuleJournalCandidateRequestDto BuildRequest(
        BondAmortizationMethod method = BondAmortizationMethod.NoAmortization, decimal? yield = null)
    {
        var at = DateTimeOffset.Parse("2026-01-02T12:00:00Z");
        var date = new DateOnly(2026, 1, 2);
        var candidate = new PostingRuleJournalCandidateRequestDto("fund", "AssetAccounting.Acquisition", 100m,
            "USD", date, "preparer", Guid.NewGuid(), Guid.NewGuid(), at, "Retained face acquisition")
        {
            AssetLotMutation = new(AssetLotMutationIntentDto.Acquire,
                new(Guid.NewGuid(), "legacy-face", date, 1m, 100m, "assets/investments",
                    OriginalFace: 100m, BookedFactor: 1m, ParBasis: 100m))
        };
        var evidence = new RetainedEvidenceIdentityDto("completion", "document://acquisition/completion", new string('b', 64),
            "review-register", "completion-review", RetainedEvidenceIdentityValidator.AcceptedReviewStatus,
            "controller", at.AddHours(1), date, 1, at.AddHours(1), "controller",
            AssetAcquisitionTermsCompletionDto.EvidenceSubjectType, "pending");
        var completion = new AssetAcquisitionTermsCompletionDto(candidate.AssetLotMutation.Acquisition!.TaxLotRecordId,
            AssetAccountingEventSpineValidator.CanonicalPayloadFingerprint(candidate), method, yield, evidence);
        return new(candidate, "controller", "original-draft-approval")
        { AcquisitionTermsCompletion = completion with { Evidence = evidence with { SubjectId = completion.ApprovalSubjectId() } } };
    }
}
