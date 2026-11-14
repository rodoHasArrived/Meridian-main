using FluentAssertions;
using Meridian.Tests.FinancialOperations.Intercompany;

namespace Meridian.Tests.Ui;

/// <summary>
/// A retained counterpart proposal whose accounting arrangement is no longer returned by the
/// tenant's arrangement registry. Guards against one unreadable arrangement taking down the whole
/// aged-break list or aborting a delivery worker run instead of being recorded against that
/// single proposal.
/// </summary>
public sealed class IntercompanyProposalMissingArrangementTests
{
    [Fact]
    public async Task Breaks_ArrangementMissingFromTenant_IsReportedAsAgreementAuthorityUnavailableInsteadOfFailingTheList()
    {
        var fixture = new IntercompanyProposalBreakAgingTests.BreakFixture(agreementAvailable: false);

        var aged = (await fixture.Breaks(new(2026, 7, 15))).Should().ContainSingle().Subject;

        aged.Classification.Should().Be("AgreementAuthorityUnavailable");
        aged.AgingBasis.Should().Be("Source effective date");
        aged.AgeFrom.Should().Be(IntercompanyProposalTestData.Date);
        aged.AgeDays.Should().Be(15);
    }

    [Fact]
    public async Task Delivery_ArrangementMissingFromTenant_FailsThatProposalInsteadOfAbortingTheWorkerRun()
    {
        var fixture = new IntercompanyProposalDeliveryRetryTests.DeliveryFixture { ArrangementRetained = false };

        var delivered = await fixture.Service.RunOnceAsync("worker");

        delivered.Should().Be(0);
        fixture.Completions.Should().BeEmpty();
        fixture.Failures.Should().ContainSingle();
        (await fixture.StoredDrafts()).Should().BeEmpty();
    }
}
