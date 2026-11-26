using System.Text.RegularExpressions;
using System.Text.Json;
using FluentAssertions;
using Meridian.Application.DirectLending;
using Meridian.Contracts.DirectLending;

namespace Meridian.Tests.Application.DirectLending;

public sealed partial class DirectLendingLifecycleReplayTests
{
    [Fact]
    public async Task AcceptedInMemoryLifecycleCommands_RetainExactNewRevisionEvidence()
    {
        var service = new InMemoryDirectLendingService();
        await service.CreateLoanAsync(new CreateLoanRequest(LoanId, "Synthetic in-memory facility",
            new BorrowerInfoDto(Guid.Parse("86666666-6666-4666-8666-666666666666"), "Synthetic borrower", null), Start, Terms()));
        await service.ActivateLoanAsync(LoanId, new ActivateLoanRequest(Start));
        await service.BookDrawdownAsync(LoanId, new BookDrawdownRequest(100m, Start, Start, null));
        var originalRevision = (await service.GetServicingStateAsync(LoanId))!.ServicingRevision;

        async Task AssertRecovery()
        {
            var live = (await service.GetServicingStateAsync(LoanId))!;
            var history = await service.GetHistoryAsync(LoanId);
            var rebuilt = Rebuild(history).Servicing;
            rebuilt.Should().BeEquivalentTo(live, options => options.Excluding(state => state.RevisionHistory));
            rebuilt.RevisionHistory.Where(revision => revision.RevisionNumber > originalRevision)
                .Should().BeEquivalentTo(live.RevisionHistory.Where(revision => revision.RevisionNumber > originalRevision));
            using var payload = JsonDocument.Parse(history[^1].PayloadJson);
            payload.RootElement.GetProperty("servicingRevision").Deserialize<ServicingRevisionDto>(Json)
                .Should().Be(live.RevisionHistory[0]);
        }

        await service.AssessFeeAsync(LoanId, new AssessFeeRequest("Administration", 5m, Start, null));
        await AssertRecovery();
        var added = await service.AddCollateralAsync(LoanId,
            new AddCollateralRequest(CollateralType.Equipment, "Synthetic machine", 200m, CurrencyCode.USD, Start));
        var collateralId = added!.Collateral!.Single().CollateralId;
        await AssertRecovery();
        await service.UpdateCollateralValueAsync(LoanId, new UpdateCollateralValueRequest(collateralId, 150m, Start));
        await AssertRecovery();
        await service.TransitionLoanStatusAsync(LoanId, new TransitionLoanStatusRequest(LoanStatus.Defaulted, "Synthetic default", Start));
        await AssertRecovery();
        await service.ApplyWriteOffAsync(LoanId, new ApplyWriteOffRequest(25m, Start, "Synthetic loss"));
        await AssertRecovery();
        await service.TogglePikAsync(LoanId, new TogglePikRequest(true, Start));
        await AssertRecovery();
        await service.AmortizeDiscountPremiumAsync(LoanId, new AmortizeDiscountPremiumRequest(Start));
        await AssertRecovery();
        await service.RemoveCollateralAsync(LoanId, new RemoveCollateralRequest(collateralId, "Synthetic release"));
        await AssertRecovery();
    }

    [Fact]
    public void EveryAcceptedAggregateEvent_HasAnExplicitReplayHandler()
    {
        var root = FindRepositoryRoot();
        var source = Path.Combine(root, "src", "Meridian.Application", "DirectLending");
        var durable = Directory.GetFiles(source, "PostgresDirectLendingCommandService*.cs")
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "eventType:\\s*\"([^\"]+)\"").Cast<Match>())
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var legacy = Directory.GetFiles(source, "InMemoryDirectLendingService*.cs")
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "AppendEvent\\(stored,\\s*\"([^\"]+)\"").Cast<Match>())
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var handlers = Directory.GetFiles(source, "DirectLendingEventRebuilder*.cs")
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "case\\s+\"([^\"]+)\"").Cast<Match>())
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        durable.Should().NotBeEmpty("the guard must discover the accepted command event inventory");
        legacy.Should().NotBeEmpty("legacy event compatibility is part of recovery");
        durable.Except(handlers).Should().BeEmpty("every durably accepted event must be replayable");
        legacy.Except(handlers).Should().BeEmpty("accepted in-memory history uses historical event aliases too");
    }

    [Fact]
    public void Rebuild_RateFixingAndPikHistory_RemainBoundToOriginalEvidenceAfterTermsChange()
    {
        var schedule = new FloatingRateResetSchedule("SOFR", RateResetFrequency.Quarterly, Start, 2,
            LookbackDayKind.Business, DeclaredRate.BasisPoints(250m));
        var terms = Terms() with { RateTypeKind = RateTypeKind.Floating, FixedAnnualRate = null, InterestIndexName = "SOFR", RateResetSchedule = schedule };
        var history = FundedHistory(terms);
        var fixingId = Guid.Parse("89999999-9999-4999-8999-999999999999");
        Add(history, "loan.rate-reset-applied", new
        {
            effectiveDate = Start,
            observedRate = 0.04m,
            spreadBps = 250m,
            allInRate = 0.065m,
            sourceRef = "retained-fixing-revision-2",
            resetKind = "Scheduled",
            fixingObservationDate = Start.AddDays(-2),
            fixingId,
            fixingRevision = 2
        });
        Add(history, "loan.daily-accrual-posted", new
        {
            accrualEntryId = Guid.Parse("8aaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"),
            accrualDate = Start,
            interestAmount = 0m,
            commitmentFeeAmount = 0m,
            penaltyAmount = 0m,
            annualRateApplied = 0.065m,
            pikInterestAmount = 5m,
            pikCapitalizationDeferred = true
        });
        Add(history, "loan.pik-capitalized", new { capitalizationDate = Start, periodStart = Start, periodEnd = Start, amount = 5m });
        Add(history, "loan.terms-amended", new
        {
            terms = terms with { RateResetSchedule = schedule with { IndexName = "OTHER" }, InterestIndexName = "OTHER" },
            versionNumber = 2,
            amendmentReason = "Later schedule"
        });
        Add(history, "loan.status-transitioned", new { newStatus = LoanStatus.NonPerforming, effectiveDate = Start, reason = "Later status" });

        var rebuilt = Rebuild(history);

        rebuilt.Servicing.CurrentRateReset!.IndexName.Should().Be("SOFR");
        rebuilt.Servicing.CurrentRateReset.FixingId.Should().Be(fixingId);
        rebuilt.Servicing.CurrentRateReset.FixingRevision.Should().Be(2);
        rebuilt.Servicing.CurrentRateReset.AllInRate.Should().Be(0.065m);
        rebuilt.Servicing.RateResetHistory.Should().ContainSingle().Which.Should().Be(rebuilt.Servicing.CurrentRateReset);
        rebuilt.Servicing.Balances.PrincipalOutstanding.Should().Be(155m);
        rebuilt.Servicing.PikInterestAccruedUnpaid.Should().Be(0m);
        rebuilt.Servicing.PikCapitalizations.Should().ContainSingle().Which.Amount.Should().Be(5m);
        rebuilt.Servicing.Status.Should().Be(LoanStatus.NonPerforming);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Unable to locate Meridian repository root for accepted event coverage.");
    }
}
