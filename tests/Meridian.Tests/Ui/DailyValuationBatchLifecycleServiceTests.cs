using FluentAssertions;
using Meridian.Application.Accounting;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;

namespace Meridian.Tests.Ui;

public sealed class DailyValuationBatchLifecycleServiceTests
{
    private static readonly Guid BookId = Guid.Parse("2f617234-41db-463f-a4be-6c99a026cf62");
    private static readonly Guid PeriodId = Guid.Parse("2e7b27b8-c3cf-42e9-bf70-e7d863ca7180");
    private static readonly Guid FirstId = Guid.Parse("00e92600-80df-4dd7-8b93-a2da56a40fb5");
    private static readonly Guid SecondId = Guid.Parse("67d61dcf-198c-42f4-af8e-eb73f20927d4");

    [Fact]
    public async Task ApproveAndPostAsync_AllMembersCompleteAndRetainedEvidenceFlowsToEveryAction()
    {
        var fixture = await CreateFixtureAsync(Draft(FirstId), Draft(SecondId));

        var result = await fixture.Service.ApproveAndPostAsync(Request());

        result.IsComplete.Should().BeTrue();
        result.PostedJournalEntryIds.Should().BeEquivalentTo([FirstId, SecondId]);
        fixture.Lifecycle.Requests.Should().HaveCount(8);
        fixture.Lifecycle.Requests.Should().OnlyContain(request =>
            request.EvidenceLinks.Contains("evidence://daily-valuation/retained", StringComparer.OrdinalIgnoreCase) &&
            request.EvidenceLinks.Contains("evidence://operator/approval", StringComparer.OrdinalIgnoreCase));
        (await fixture.Source.GetAsync("daily-a"))!.State.Should().Be(DailyValuationScheduleStateDto.Posted);
    }

    [Fact]
    public async Task ApproveAndPostAsync_RetrySkipsPostedMemberAndCompletesRemainingMember()
    {
        var fixture = await CreateFixtureAsync(
            Draft(FirstId, ManualJournalEntryStatusDto.Posted),
            Draft(SecondId, ManualJournalEntryStatusDto.Approved));

        var result = await fixture.Service.ApproveAndPostAsync(Request());

        result.IsComplete.Should().BeTrue();
        fixture.Lifecycle.Requests.Should().HaveCount(2);
        fixture.Lifecycle.Requests.Select(request => request.Action)
            .Should().Equal(JournalEntryLifecycleActionDto.Validate, JournalEntryLifecycleActionDto.Post);
        fixture.Lifecycle.Requests.Should().OnlyContain(request => request.JournalEntryId == SecondId);
    }

    [Fact]
    public async Task ApproveAndPostAsync_MissingMemberBlocksBeforeAnyLifecycleAction()
    {
        var fixture = await CreateFixtureAsync(Draft(FirstId));

        var result = await fixture.Service.ApproveAndPostAsync(Request());

        result.IsComplete.Should().BeFalse();
        result.Blockers.Should().ContainSingle(message => message.Contains(SecondId.ToString("D"), StringComparison.Ordinal));
        fixture.Lifecycle.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAndPostAsync_ValidatesAllMembersBeforePostingAnyMember()
    {
        var fixture = await CreateFixtureAsync(Draft(FirstId), Draft(SecondId));
        fixture.Lifecycle.FailValidationFor = SecondId;

        var result = await fixture.Service.ApproveAndPostAsync(Request());

        result.IsComplete.Should().BeFalse();
        fixture.Lifecycle.Requests.Should().HaveCount(2)
            .And.OnlyContain(request => request.Action == JournalEntryLifecycleActionDto.Validate);
        fixture.Store.Items.Values.Should().NotContain(draft => draft.Status == ManualJournalEntryStatusDto.Posted);
    }

    [Fact]
    public async Task ApproveAndPostAsync_PreparerCannotApproveOwnBatch()
    {
        var fixture = await CreateFixtureAsync(Draft(FirstId, preparedBy: "controller-a"), Draft(SecondId));

        var result = await fixture.Service.ApproveAndPostAsync(Request());

        result.IsComplete.Should().BeFalse();
        result.Blockers.Should().Contain(message => message.Contains("independent from preparer", StringComparison.Ordinal));
        fixture.Lifecycle.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAndPostAsync_DraftEntityMismatchBlocksBatch()
    {
        var fixture = await CreateFixtureAsync(
            Draft(FirstId),
            Draft(SecondId) with { EntityId = "entity-other" });

        var result = await fixture.Service.ApproveAndPostAsync(Request());

        result.IsComplete.Should().BeFalse();
        result.Blockers.Should().Contain(message => message.Contains("batch scope", StringComparison.Ordinal));
        fixture.Lifecycle.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAndPostAsync_CrossTenantRequestIsRejected()
    {
        var fixture = await CreateFixtureAsync(Draft(FirstId), Draft(SecondId));

        var act = () => fixture.Service.ApproveAndPostAsync(Request() with { TenantId = "tenant-other" });

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        fixture.Lifecycle.Requests.Should().BeEmpty();
    }

    private static async Task<Fixture> CreateFixtureAsync(params ManualJournalEntryDraftDto[] drafts)
    {
        var source = new InMemoryDailyValuationPortfolioSource();
        await source.SaveAsync(Schedule());
        var store = new RecordingDraftStore(drafts);
        var lifecycle = new RecordingLifecycleService(store);
        return new Fixture(source, store, lifecycle, new DailyValuationBatchLifecycleService(source, store, lifecycle));
    }

    [Fact]
    public async Task ApproveAndPostAsync_WithoutADerivedOrigin_DoesNotClaimHumanStanding()
    {
        // This batch approves and posts journal entries, and ManualJournalEntryWorkbenchService
        // gates every lifecycle action on OperationsOriginGuard.RequireHumanOperator. The batch used
        // to build its lifecycle requests without setting ActionOrigin at all, so they took
        // JournalEntryLifecycleActionRequestDto's permissive HumanOperator default and satisfied
        // that gate on standing nobody had established -- the #2673 hole, one layer down and
        // reachable from an HTTP route that never derived an origin because its DTO had no field
        // for one.
        var fixture = await CreateFixtureAsync(Draft(FirstId), Draft(SecondId));

        await fixture.Service.ApproveAndPostAsync(Request());

        fixture.Lifecycle.Requests.Should().NotBeEmpty();
        fixture.Lifecycle.Requests.Should().OnlyContain(
            request => request.ActionOrigin == OperationsActionOriginDto.AutomationAssistant,
            "an origin the caller never derived must fail closed rather than inherit human standing");
    }

    [Fact]
    public async Task ApproveAndPostAsync_PropagatesADerivedHumanOrigin()
    {
        // The other half: when the endpoint has derived the origin from a real interactive session,
        // the batch must carry it through unchanged rather than substituting its own.
        var fixture = await CreateFixtureAsync(Draft(FirstId), Draft(SecondId));

        await fixture.Service.ApproveAndPostAsync(
            Request() with { ActionOrigin = OperationsActionOriginDto.HumanOperator });

        fixture.Lifecycle.Requests.Should().NotBeEmpty();
        fixture.Lifecycle.Requests.Should().OnlyContain(
            request => request.ActionOrigin == OperationsActionOriginDto.HumanOperator);
    }

    private static DailyValuationBatchLifecycleRequestDto Request()
        => new(
            "daily-a",
            "fund-a",
            "controller-a",
            "Reviewed trusted marks and approved the complete valuation batch.",
            ["evidence://operator/approval"],
            "tenant-a",
            "company-a");

    private static DailyValuationScheduleWorkItem Schedule()
        => new(
            "daily-a",
            "fund-a",
            "USD",
            "preparer-a",
            BookId,
            PeriodId,
            DateTimeOffset.Parse("2026-07-16T23:00:00Z"),
            [new MarkToMarketPosition("AAPL", 10m, 150m)],
            "policy-a",
            "Listed equity close",
            "Provider close",
            "controller-a",
            DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            "Daily close",
            EntityId: "entity-a",
            TenantId: "tenant-a",
            CompanyId: "company-a",
            State: DailyValuationScheduleStateDto.DraftReady,
            JournalEntryId: FirstId,
            EvidenceLinks:
            [
                new OperationsEvidenceLinkDto(
                    "daily-retained",
                    "Retained mark evidence",
                    "evidence://daily-valuation/retained",
                    "daily-valuation-scheduler",
                    DateTimeOffset.Parse("2026-07-15T23:00:00Z"))
            ],
            JournalEntryIds: [FirstId, SecondId],
            BatchCorrelationId: "valuation-batch-a",
            UseStaticPositionOverride: true,
            StaticPositionsAsOfUtc: DateTimeOffset.Parse("2026-07-15T22:00:00Z"));

    private static ManualJournalEntryDraftDto Draft(
        Guid id,
        ManualJournalEntryStatusDto status = ManualJournalEntryStatusDto.Draft,
        string preparedBy = "preparer-a")
        => new(
            id,
            status,
            "fund-a",
            BookId,
            AccountingBasisKindDto.Primary,
            new DateOnly(2026, 7, 15),
            PeriodId.ToString("D"),
            "entity-a",
            null,
            "USD",
            "Daily fair value adjustment",
            preparedBy,
            DateTimeOffset.Parse("2026-07-15T23:00:00Z"),
            DateTimeOffset.Parse("2026-07-15T23:00:00Z"),
            Version: 1,
            Lines: [],
            EvidenceLinks: [],
            ValidationIssues: [],
            TreasuryContext: new TreasuryLedgerContextDto(IdempotencyKey: $"fair-value|{id:N}"),
            TenantId: "tenant-a",
            CompanyId: "company-a");

    private sealed record Fixture(
        InMemoryDailyValuationPortfolioSource Source,
        RecordingDraftStore Store,
        RecordingLifecycleService Lifecycle,
        DailyValuationBatchLifecycleService Service);

    private sealed class RecordingDraftStore(IEnumerable<ManualJournalEntryDraftDto> drafts)
        : IManualJournalEntryDraftStore
    {
        public Dictionary<Guid, ManualJournalEntryDraftDto> Items { get; } =
            drafts.ToDictionary(static draft => draft.JournalEntryId);

        public Task<IReadOnlyList<string>> ListFundProfileIdsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["fund-a"]);

        public Task<IReadOnlyList<ManualJournalEntryDraftDto>> ListAsync(
            string fundProfileId,
            Guid? ledgerBookId = null,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
            => Task.FromResult<IReadOnlyList<ManualJournalEntryDraftDto>>(Items.Values.ToArray());

        public Task<ManualJournalEntryDraftDto?> GetAsync(
            string fundProfileId,
            Guid journalEntryId,
            CancellationToken ct = default,
            string? tenantId = null,
            string? companyId = null)
        {
            Items.TryGetValue(journalEntryId, out var draft);
            if (draft is not null &&
                (!string.Equals(draft.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(draft.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(draft.CompanyId, companyId, StringComparison.OrdinalIgnoreCase)))
            {
                draft = null;
            }

            return Task.FromResult(draft);
        }

        public Task SaveAsync(ManualJournalEntryDraftDto draft, CancellationToken ct = default)
        {
            Items[draft.JournalEntryId] = draft;
            return Task.CompletedTask;
        }

        public Task SaveBatchAsync(
            IReadOnlyList<ManualJournalEntryDraftDto> drafts,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var draft in drafts)
            {
                Items[draft.JournalEntryId] = draft;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLifecycleService(RecordingDraftStore store)
        : IManualJournalEntryLifecycleService
    {
        public List<JournalEntryLifecycleActionRequestDto> Requests { get; } = [];

        public Guid? FailValidationFor { get; set; }

        public async Task<JournalEntryLifecycleActionResultDto> ApplyLifecycleActionAsync(
            JournalEntryLifecycleActionRequestDto request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            var current = store.Items[request.JournalEntryId];
            var nextStatus = request.Action switch
            {
                JournalEntryLifecycleActionDto.Validate when FailValidationFor == request.JournalEntryId =>
                    ManualJournalEntryStatusDto.NeedsFix,
                JournalEntryLifecycleActionDto.Validate => current.Status,
                JournalEntryLifecycleActionDto.Submit => ManualJournalEntryStatusDto.Submitted,
                JournalEntryLifecycleActionDto.Approve => ManualJournalEntryStatusDto.Approved,
                JournalEntryLifecycleActionDto.Post => ManualJournalEntryStatusDto.Posted,
                _ => current.Status
            };
            IReadOnlyList<AccountingConfigurationValidationIssueDto> issues =
                nextStatus == ManualJournalEntryStatusDto.NeedsFix
                ? [new AccountingConfigurationValidationIssueDto(
                    "valuation-control",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    "Injected validation failure.")]
                : current.ValidationIssues;
            var updated = current with
            {
                Status = nextStatus,
                Version = current.Version + 1,
                ValidationIssues = issues,
                EvidenceLinks = current.EvidenceLinks
                    .Concat(request.EvidenceLinks)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
            await store.SaveAsync(updated, ct);
            var transition = new JournalEntryLifecycleTransitionDto(
                $"transition-{Requests.Count}",
                current.Status,
                nextStatus,
                request.Action,
                request.Actor,
                DateTimeOffset.UtcNow,
                request.Notes,
                request.CorrelationId,
                request.EvidenceLinks);
            return new JournalEntryLifecycleActionResultDto(updated, transition);
        }
    }
}
