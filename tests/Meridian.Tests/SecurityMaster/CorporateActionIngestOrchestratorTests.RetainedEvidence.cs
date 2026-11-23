using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster.CorporateActions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Xunit;

namespace Meridian.Tests.SecurityMaster;

public sealed partial class CorporateActionIngestOrchestratorTests
{
    [Fact]
    public async Task IngestAsync_TypedMergerPreservesTermsAndRawEvidenceWithoutInferringCurrencyOrAcceptance()
    {
        var securityId = Guid.NewGuid();
        const string raw = """{ "id": "native-merger", "cash_rate": 5, "acquirer_symbol": "SUCCESSOR" }""";
        var evidence = new CorporateActionRetainedSourceEvidenceDto(
            "stock_and_cash_merger", "alpaca-market-data-corporate-actions-v1/meridian-v2", raw,
            Sha256Digest.ComputeUtf8(raw));
        var command = Dividend(securityId, "alpaca", 5m) with
        {
            ActionType = CorporateActionEventTypes.MergerAbsorption,
            Currency = null,
            PreserveUnknownCurrency = true,
            ExchangeRatio = 0.5m,
            Payload = JsonSerializer.SerializeToElement(new
            {
                cashConsiderationPerUnit = 5m,
                acquirerSymbol = "SUCCESSOR",
                providerEventType = "stock_and_cash_merger",
            }),
            SourceEventId = "native-merger",
            SourceEventVersion = "payload-sha256:fixture:alpaca-rest-v2",
            SourceEvidence = evidence,
        };
        var orchestrator = CreateDurableOrchestrator(securityId, out var recorded,
            new StubProvider("alpaca", command));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest(MinimumSourcesToApply: 1));

        result.Errors.Should().BeEmpty();
        result.Applied.Should().Be(0);
        var request = recorded.Should().ContainSingle().Subject;
        request.ProviderIdentity.SourceEventId.Should().Be("native-merger");
        request.ProviderIdentity.ReleaseStatus.Should().Be(CorporateActionProviderReleaseStatusDto.ReviewOnly);
        request.ProposedAction.Currency.Should().BeNull();
        request.ProposedAction.ExchangeRatio.Should().Be(0.5m);
        request.ProposedAction.AcquirerSecurityId.Should().BeNull("a provider ticker cannot invent a mastered successor ID");
        request.ProposedAction.Payload!.Value.GetProperty("cashConsiderationPerUnit").GetDecimal().Should().Be(5m);
        request.ProposedAction.SourceEvidence.Should().Be(evidence);
        result.Proposals.Should().ContainSingle().Which.AutoApplied.Should().BeFalse();
    }

    [Fact]
    public async Task IngestAsync_SpinoffDistributionRatioAndSuccessorDisagreementRemainDistinct()
    {
        var securityId = Guid.NewGuid();
        var first = Dividend(securityId, "provider-a", 0m) with
        {
            ActionType = CorporateActionEventTypes.SpinOff,
            Amount = null,
            Currency = null,
            DistributionRatio = 0.25m,
            Payload = JsonSerializer.SerializeToElement(new { newSymbol = "CHILD-A" }),
            SourceEventId = "spin-a",
            SourceEventVersion = "v1",
        };
        var second = first with
        {
            SourceProvider = "provider-b",
            SourceEventId = "spin-b",
            DistributionRatio = 0.5m,
            Payload = JsonSerializer.SerializeToElement(new { newSymbol = "CHILD-B" }),
        };
        var orchestrator = CreateDurableOrchestrator(securityId, out var recorded,
            new StubProvider("provider-a", first), new StubProvider("provider-b", second));

        var result = await orchestrator.IngestAsync(new CorporateActionIngestRequest());

        result.Errors.Should().BeEmpty();
        recorded.Should().HaveCount(2);
        recorded.Select(request => request.ProposedAction.DistributionRatio).Should().BeEquivalentTo(new decimal?[] { 0.25m, 0.5m });
        recorded.Select(request => request.ClaimedEconomicFingerprint).Should().OnlyHaveUniqueItems();
        recorded.Should().OnlyContain(request => request.DisplayMetadata!.DissentingSources.Count == 1);
        recorded[0].DisplayMetadata!.DissentingFields!.Select(field => field.Field)
            .Should().Contain(nameof(CorporateActionDto.DistributionRatio)).And.Contain(nameof(CorporateActionDto.Payload));
    }
}
