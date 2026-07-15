using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class DailyValuationScheduleIdentityTests
{
    private static readonly Guid BookId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid PeriodId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    [Fact]
    public async Task SaveAsync_AllowsOperatorHandoffWhilePreservingCreatorIdentity()
    {
        var source = new InMemoryDailyValuationPortfolioSource();
        var original = await source.SaveAsync(CreateWorkItem() with
        {
            Actor = "creator-a",
            CreatedBy = "creator-a",
            LastConfiguredBy = "creator-a"
        });

        var reconfigured = await source.SaveAsync(original with
        {
            Actor = "controller-b",
            LastConfiguredBy = "controller-b",
            NextRunAtUtc = original.NextRunAtUtc.AddDays(1)
        });

        reconfigured.Actor.Should().Be("controller-b");
        reconfigured.CreatedBy.Should().Be("creator-a");
        reconfigured.LastConfiguredBy.Should().Be("controller-b");
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("company")]
    [InlineData("fund")]
    [InlineData("book")]
    [InlineData("entity")]
    [InlineData("currency")]
    [InlineData("creator")]
    public async Task SaveAsync_RejectsImmutableIdentityTakeover(string mutation)
    {
        var source = new InMemoryDailyValuationPortfolioSource();
        var original = await source.SaveAsync(CreateWorkItem());
        var replacement = mutation switch
        {
            "tenant" => original with { TenantId = "tenant-b" },
            "company" => original with { CompanyId = "company-b" },
            "fund" => original with { FundProfileId = "fund-b" },
            "book" => original with { LedgerBookId = Guid.NewGuid() },
            "entity" => original with { EntityId = "entity-b" },
            "currency" => original with { Currency = "EUR" },
            "creator" => original with { CreatedBy = "creator-b" },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        var act = () => source.SaveAsync(replacement);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*different immutable identity scope*");
    }

    [Fact]
    public async Task GetStatusAsync_RequiresExactTenantCompanyAndEntityScope()
    {
        var source = new InMemoryDailyValuationPortfolioSource();
        await source.SaveAsync(CreateWorkItem());

        var wrongScope = await source.GetStatusAsync(
            "fund-a",
            BookId,
            "2026-07",
            entityId: "entity-b",
            tenantId: "tenant-a",
            companyId: "company-a");
        var ownedScope = await source.GetStatusAsync(
            "fund-a",
            BookId,
            "2026-07",
            entityId: "entity-a",
            tenantId: "tenant-a",
            companyId: "company-a");

        wrongScope.IsConfigured.Should().BeFalse();
        ownedScope.IsConfigured.Should().BeTrue();
        ownedScope.EntityId.Should().Be("entity-a");
        ownedScope.TenantId.Should().Be("tenant-a");
        ownedScope.CompanyId.Should().Be("company-a");
    }

    [Fact]
    public async Task GetStatusAsync_CurrentScheduledWorkWinsOlderPostedScheduleForSameScope()
    {
        var source = new InMemoryDailyValuationPortfolioSource();
        await source.SaveAsync(CreateWorkItem() with
        {
            ScheduleId = "older-posted",
            State = DailyValuationScheduleStateDto.Posted,
            LastRunAtUtc = DateTimeOffset.Parse("2026-07-15T23:05:00Z"),
            LastScheduledForUtc = DateTimeOffset.Parse("2026-07-15T23:00:00Z"),
            NextRunAtUtc = DateTimeOffset.Parse("2026-07-16T23:00:00Z")
        });
        await source.SaveAsync(CreateWorkItem() with
        {
            ScheduleId = "current-scheduled",
            State = DailyValuationScheduleStateDto.Scheduled,
            LastRunAtUtc = null,
            LastScheduledForUtc = null,
            NextRunAtUtc = DateTimeOffset.Parse("2026-07-16T22:00:00Z")
        });

        var status = await source.GetStatusAsync(
            "fund-a",
            BookId,
            "2026-07",
            entityId: "entity-a",
            tenantId: "tenant-a",
            companyId: "company-a");

        status.ScheduleId.Should().Be("current-scheduled");
        status.State.Should().Be(DailyValuationScheduleStateDto.Scheduled);
    }

    [Fact]
    public void BuildIntakeBlockers_RejectedTerminalNeedsFixReassessmentAndProjectionFailure_AreNeverReady()
    {
        var needsFixDraft = new ManualJournalEntryDraftDto(
            Guid.Parse("33333333-3333-4333-8333-333333333333"),
            ManualJournalEntryStatusDto.NeedsFix,
            "fund-a",
            BookId,
            AccountingBasisKindDto.Primary,
            new DateOnly(2026, 7, 15),
            PeriodId.ToString("D"),
            "entity-a",
            FundNodeId: null,
            Currency: "USD",
            Memo: "Needs repair",
            PreparedBy: "preparer-a",
            CreatedAtUtc: DateTimeOffset.Parse("2026-07-15T23:00:00Z"),
            UpdatedAtUtc: DateTimeOffset.Parse("2026-07-15T23:00:00Z"),
            Version: 1,
            Lines: [],
            EvidenceLinks: [],
            ValidationIssues: []);
        var dispositions = new[]
        {
            AutomatedJournalDraftIntakeDisposition.ProjectionFailed,
            AutomatedJournalDraftIntakeDisposition.ExistingDraftNeedsFix,
            AutomatedJournalDraftIntakeDisposition.ExistingDraftRejected,
            AutomatedJournalDraftIntakeDisposition.ExistingDraftTerminal,
            AutomatedJournalDraftIntakeDisposition.ExistingDraftReassessmentRequired
        };
        var intake = new AutomatedJournalDraftIntakeResult(
            [needsFixDraft],
            dispositions.Select((disposition, index) => new AutomatedJournalDraftIntakeSkip(
                Guid.Parse($"44444444-4444-4444-8444-{index + 1:000000000000}"),
                $"key-{index}",
                $"blocked-{disposition}",
                disposition)).ToArray());

        var blockers = DailyValuationScheduledWorker.BuildIntakeBlockers(intake);

        blockers.Should().HaveCount(dispositions.Length + 1);
        blockers.Should().Contain(message => message.Contains("NeedsFix", StringComparison.Ordinal));
        blockers.Should().Contain(dispositions.Select(disposition => $"blocked-{disposition}"));
    }

    private static DailyValuationScheduleWorkItem CreateWorkItem()
        => new(
            "daily-fund-a",
            "fund-a",
            "USD",
            "creator-a",
            BookId,
            PeriodId,
            DateTimeOffset.Parse("2026-07-16T23:00:00Z"),
            [new MarkToMarketPosition("AAPL", 10m, 150m)],
            "fair-value-policy",
            "Listed equities close",
            "Provider close",
            "controller-a",
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            "Daily governed valuation",
            EntityId: "entity-a",
            TenantId: "tenant-a",
            CompanyId: "company-a",
            UseStaticPositionOverride: true,
            StaticPositionsAsOfUtc: DateTimeOffset.Parse("2026-07-16T22:00:00Z"),
            CreatedBy: "creator-a",
            LastConfiguredBy: "creator-a");
}
