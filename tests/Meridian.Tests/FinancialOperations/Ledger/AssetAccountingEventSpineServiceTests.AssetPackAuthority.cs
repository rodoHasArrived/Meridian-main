using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.AssetOperations;
using Meridian.Storage.Ledger;
using NSubstitute;

namespace Meridian.Tests.FinancialOperations.Ledger;

public sealed partial class AssetAccountingEventSpineServiceTests
{
    [Theory]
    [InlineData("missing-pack", "*does not contain a promoted Rules Studio rule pack*")]
    [InlineData("missing-selection", "*did not select a promoted posting rule*")]
    [InlineData("missing-match", "*selected rule metadata is missing*")]
    [InlineData("unmatched-selection", "*selected rule metadata is missing*")]
    [InlineData("new-rule-version", "*does not belong to the authoritative accounting policy rule pack*")]
    [InlineData("new-policy-version", "*policy id/version does not match the authoritative book and period*")]
    public async Task AssetPackAccountingDeclarations_CannotReplaceExactPromotedBinding(
        string scenario, string expectedMessage)
    {
        var fixture = BuildFixture();
        fixture = fixture with { Security = fixture.Security with { AssetClass = "Bond" } };

        // These public facades project the class-owned module declarations. Even an in-slice
        // accounting identity and declared deep-accounting templates grant no posting authority.
        var descriptor = SecurityAssetClassCatalog.GetOrDefault(fixture.Security.AssetClass);
        descriptor.AccountingInstrumentClass.Should().Be(SecurityAccountingInstrumentClasses.Bond);
        descriptor.AccountingDisposition.Should().Be(SecurityAccountingDisposition.InSlice);
        SecurityAssetPackRegistry.FindByAssetClass(descriptor.AssetClass).Should().Contain(pack =>
            pack.AutomationDepth == AssetPackAutomationDepth.DeepAccountingAutomation &&
            pack.AccountingRules.JournalTemplates.Any(template => template.LifecycleEvent == "Coupon"));

        var policy = BuildPolicy();
        var dryRun = BuildMatchingDryRun(fixture);
        switch (scenario)
        {
            case "missing-pack":
                policy = policy with { RulePack = null };
                break;
            case "missing-selection":
                dryRun = dryRun with { SelectedRuleId = null };
                break;
            case "missing-match":
                dryRun = dryRun with { RuleMatches = [] };
                break;
            case "unmatched-selection":
                dryRun = dryRun with { RuleMatches = [dryRun.RuleMatches[0] with { IsMatched = false }] };
                break;
            case "new-rule-version":
                dryRun = dryRun with { RuleMatches = [dryRun.RuleMatches[0] with { RuleVersion = "v3" }] };
                break;
            case "new-policy-version":
                policy = policy with { Version = "v2" };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var source = BuildProjectedSpine(fixture);
        var eventStore = Substitute.For<IAssetAccountingEventProjectionStore>();
        eventStore.GetAsync(source.EventId, source.EventVersion, source.SpineVersion, Arg.Any<CancellationToken>())
            .Returns(new AssetAccountingEventProjectionRecord(source, ComputeFingerprint(source)));
        var positions = Substitute.For<IInstrumentPositionProjectionStore>();
        positions.GetBookPositionAsync(fixture.Position.PositionId, Arg.Any<CancellationToken>()).Returns(fixture.Position);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetRecordedByIdAsOfAsync(fixture.Security.SecurityId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(fixture.Security);
        var books = Substitute.For<ILedgerBookService>();
        books.GetBookAsync(fixture.Book.LedgerBookId, Arg.Any<CancellationToken>()).Returns(fixture.Book);
        books.ListPeriodsAsync(Arg.Any<LedgerPeriodQuery>(), Arg.Any<CancellationToken>()).Returns([fixture.Period]);
        var policies = Substitute.For<IAccountingPolicyService>();
        policies.ResolvePolicyAsync(Arg.Any<AccountingPolicyQuery>(), Arg.Any<CancellationToken>()).Returns(policy);
        var configuration = Substitute.For<IAccountingConfigurationService>();
        configuration.DryRunPostingRuleAsync(Arg.Any<RuleDryRunRequestDto>(), Arg.Any<CancellationToken>()).Returns(dryRun);
        var authorityBuilder = Substitute.For<IAccountingPostingCandidateAuthorityBuilder>();
        var journals = Substitute.For<ILedgerJournalStore>();
        var service = new AssetAccountingEventSpineService(
            eventStore, positions, securities, books, policies, configuration, authorityBuilder, journals);

        var act = () => service.BuildPostingCandidateAsync(BuildCandidateRequest(fixture));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage(expectedMessage);
        await authorityBuilder.DidNotReceiveWithAnyArgs().BuildAuthoritativeCandidateWriteAsync(default!, default!, default);
        await eventStore.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, default, default);
        journals.ReceivedCalls().Should().BeEmpty("invalid promoted bindings must fail before durable ledger access");
    }
}
