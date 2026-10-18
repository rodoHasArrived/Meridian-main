using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Unit")]
public sealed class SecurityMasterClosePriceServiceTests
{
    private static readonly Guid SecurityId = Guid.NewGuid();
    private static readonly DateTimeOffset Close = new(2026, 10, 1, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApprovalRetainsExactSourceObservationsOverrideAndIndependentRationale()
    {
        var (service, store) = BuildService();
        var manual = new SecurityClosePriceOverrideDto(100.1m, SecurityPriceUnit.PercentOfPar, "Validated close adjustment", "dealer-file/row-17");
        var draft = await service.CreateClosePriceSetAsync(Request(manual));
        draft.Snapshot.Blockers.Should().BeEmpty();
        draft.Snapshot.Lines.Single().Observations.Should().HaveCount(2);
        draft.Snapshot.Lines.Single().ClosePrice.Should().Be(100.1m);
        draft.Snapshot.Lines.Single().Selection!.GoldenCopyPrice.Should().Be(100m);
        draft.Snapshot.Lines.Single().Override.Should().Be(manual);
        RetainForRead(store, draft);

        await service.Invoking(s => s.ApproveClosePriceSetAsync(Approve(draft, "maker"))).Should().ThrowAsync<InvalidOperationException>();
        var approved = await service.ApproveClosePriceSetAsync(Approve(draft));
        approved.Approval!.Rationale.Should().Be("Reviewed both source rows and the adjustment");
        approved.Approval.SnapshotSha256.Should().Be(SecurityClosePriceEvidence.Digest(draft.Snapshot));
        await store.Received(1).RetainClosePriceApprovalAsync("account-a", "book-a", DateOnly.FromDateTime(Close.UtcDateTime),
            Arg.Is<SecurityClosePriceApprovalDto>(a => a.Reviewer == "checker"), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("stale", "StalePrice")]
    [InlineData("unsupported", "UnsupportedPrice")]
    [InlineData("dispersion", "SourceDispersion")]
    [InlineData("mixed", "IncomparableUnits")]
    [InlineData("override", "UnsupportedOverride")]
    public async Task ExceptionsRemainRetainedBlockersAndCannotBeApproved(string scenario, string blocker)
    {
        var (service, store) = BuildService(scenario);
        var manual = scenario == "override" ? new SecurityClosePriceOverrideDto(100m, SecurityPriceUnit.PercentOfPar, "", "") : null;
        var draft = await service.CreateClosePriceSetAsync(Request(manual));
        draft.Snapshot.Blockers.Select(b => b.Code).Should().Contain(blocker);
        draft.Snapshot.Lines.Single().Observations.Should().NotBeEmpty();
        RetainForRead(store, draft);
        await service.Invoking(s => s.ApproveClosePriceSetAsync(Approve(draft))).Should().ThrowAsync<InvalidOperationException>();
        await store.DidNotReceive().RetainClosePriceApprovalAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(),
            Arg.Any<SecurityClosePriceApprovalDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorrectionLinksExactScopeAndPreservesSecurityCoverage()
    {
        var (service, store) = BuildService();
        var original = await service.CreateClosePriceSetAsync(Request());
        RetainForRead(store, original);
        var corrected = await service.CreateClosePriceSetAsync(Request() with
        { CorrectsSnapshotId = original.Snapshot.SnapshotId, CorrectionRationale = "Corrected vendor observation" });
        corrected.Snapshot.Version.Should().Be(2);
        corrected.Snapshot.CorrectsSnapshotId.Should().Be(original.Snapshot.SnapshotId);
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with
        { AccountId = "account-b", CorrectsSnapshotId = original.Snapshot.SnapshotId, CorrectionRationale = "Wrong scope" }))
            .Should().ThrowAsync<InvalidOperationException>();
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with
        { Securities = [new(Guid.NewGuid())], CorrectsSnapshotId = original.Snapshot.SnapshotId, CorrectionRationale = "Changed coverage" }))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IdentifiedCreationRetryReturnsRetainedReviewBeforeReadingOrRetainingPrices()
    {
        var (service, store) = BuildService();
        var request = Request() with { RequestId = Guid.NewGuid() };
        var draft = await service.CreateClosePriceSetAsync(request);
        draft.Snapshot.SnapshotId.Should().Be(request.RequestId!.Value);
        draft.Snapshot.CreationCommandDigest.Should().Be(SecurityClosePriceEvidence.CreationCommandDigest(request));
        RetainForRead(store, draft);
        var approved = await service.ApproveClosePriceSetAsync(Approve(draft));
        RetainForRead(store, approved);
        store.ClearReceivedCalls();

        var recovered = await service.CreateClosePriceSetAsync(request with
        { AccountId = " account-a ", BookId = " book-a ", CreatedBy = " maker ", EvaluatedAsOf = Close.ToOffset(TimeSpan.FromHours(5)) });

        recovered.Should().BeSameAs(approved);
        await store.DidNotReceive().GetHierarchyAsOfAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
        await store.DidNotReceive().GetRawPricesAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
        await store.DidNotReceive().RetainPriceSelectionAsync(Arg.Any<SecurityPriceGoldenCopyDto>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().RetainClosePriceSnapshotAsync(Arg.Any<SecurityClosePriceSnapshotDto>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("tolerance")]
    [InlineData("preparer")]
    [InlineData("override")]
    [InlineData("knowledge")]
    [InlineData("correction")]
    public async Task CreationIdentityCannotBeReusedForChangedCommandBeforeAnyPriceEvaluation(string changedField)
    {
        var (service, store) = BuildService();
        var request = Request() with { RequestId = Guid.NewGuid() };
        var draft = await service.CreateClosePriceSetAsync(request);
        RetainForRead(store, draft);
        store.ClearReceivedCalls();
        var changed = changedField switch
        {
            "tolerance" => request with { MaxSourceDispersionPercent = 3m },
            "preparer" => request with { CreatedBy = "another-maker" },
            "override" => request with { Securities = [new(SecurityId, new(100.1m, SecurityPriceUnit.PercentOfPar, "Changed", "file/row-1"))] },
            "knowledge" => request with { KnowledgeAsOf = draft.Snapshot.KnowledgeAsOf },
            _ => request with { CorrectsSnapshotId = Guid.NewGuid(), CorrectionRationale = "Changed predecessor" }
        };

        await service.Invoking(s => s.CreateClosePriceSetAsync(changed)).Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*request identity cannot be reused*");
        await store.DidNotReceive().GetHierarchyAsOfAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
        await store.DidNotReceive().GetRawPricesAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
        await store.DidNotReceive().RetainPriceSelectionAsync(Arg.Any<SecurityPriceGoldenCopyDto>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().RetainClosePriceSnapshotAsync(Arg.Any<SecurityClosePriceSnapshotDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreationDigestBindsDurableScopeIdentityToleranceAndDefaultKnowledgeChoice()
    {
        var (service, _) = BuildService();
        var draft = await service.CreateClosePriceSetAsync(Request() with { RequestId = Guid.NewGuid() });
        foreach (var changed in new[]
        {
            draft.Snapshot with { SnapshotId = Guid.NewGuid() },
            draft.Snapshot with { MaxSourceDispersionPercent = 3m },
            draft.Snapshot with { RequestedKnowledgeAsOf = draft.Snapshot.KnowledgeAsOf },
            draft.Snapshot with { CreationCommandDigest = new string('0', 64) }
        })
        {
            Action validate = () => SecurityClosePriceEvidence.Validate(changed);
            validate.Should().Throw<ArgumentException>().WithMessage("*creation command digest*");
        }
    }

    [Fact]
    public async Task LegacySnapshotCanonicalBytesAndApprovalDigestOmitAbsentCreationMetadata()
    {
        var (service, _) = BuildService();
        var draft = await service.CreateClosePriceSetAsync(Request());
        var legacyPayload = JsonSerializer.Serialize(draft.Snapshot, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto);
        legacyPayload.Should().NotContain("CreationCommandDigest").And.NotContain("RequestedKnowledgeAsOf");
        var legacyDigest = Meridian.Contracts.Integrity.Sha256Digest.ComputeUtf8(legacyPayload);
        var hydrated = JsonSerializer.Deserialize(legacyPayload, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto)!;
        SecurityClosePriceEvidence.Digest(hydrated).Should().Be(legacyDigest);
        var approval = new SecurityClosePriceApprovalDto(hydrated.SnapshotId, legacyDigest, "checker", "Retained legacy approval", DateTimeOffset.UtcNow);
        Action validateApproval = () => SecurityClosePriceEvidence.ValidateApproval(hydrated, approval);
        validateApproval.Should().NotThrow();
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("negative")]
    public async Task SupportedOverrideAndLargeDispersionLimitCannotHideUnsupportedSourcePrice(string scenario)
    {
        var (service, store) = BuildService(scenario);
        var manual = new SecurityClosePriceOverrideDto(100m, SecurityPriceUnit.PercentOfPar, "Override", "source-file/row-1");
        var draft = await service.CreateClosePriceSetAsync(Request(manual) with { MaxSourceDispersionPercent = 1000m });
        draft.Snapshot.Lines.Single().ClosePrice.Should().Be(100m);
        draft.Snapshot.Blockers.Select(b => b.Code).Should().Contain("UnsupportedPrice");
        RetainForRead(store, draft);
        await service.Invoking(s => s.ApproveClosePriceSetAsync(Approve(draft))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("extreme")]
    [InlineData("tiny-override")]
    public async Task UnrepresentableDispersionRetainsExactSourcesAndDurableBlockerInsteadOfFailingSnapshot(string scenario)
    {
        var (service, store) = BuildService(scenario);
        var manual = scenario == "tiny-override"
            ? new SecurityClosePriceOverrideDto(0.0000000000000000000000000001m,
                SecurityPriceUnit.PercentOfPar, "Retained manual input", "dealer-file/row-17")
            : null;
        var draft = await service.CreateClosePriceSetAsync(Request(manual) with { MaxSourceDispersionPercent = decimal.MaxValue });

        draft.Snapshot.Blockers.Should().ContainSingle(b => b.Code == "SourceDispersion"
            && b.Reason.Contains("decimal range", StringComparison.Ordinal));
        var line = draft.Snapshot.Lines.Single();
        line.Selection.Should().NotBeNull();
        line.Observations.Should().HaveCount(2);
        line.Observations.Single(p => p.SourceId == "source").Price.Should().Be(scenario == "extreme" ? 0.0000000001m : 100m);
        line.Observations.Single(p => p.SourceId == "comparison").Price.Should().Be(scenario == "extreme" ? 999999999999999999m : 100.2m);
        if (scenario == "extreme")
            line.Selection!.ComparisonPrices.Single().PctDiffFromGoldenCopy.Should().BeNull();
        else
            line.Override.Should().Be(manual);
        await store.Received(1).RetainPriceSelectionAsync(Arg.Any<SecurityPriceGoldenCopyDto>(), "account-a", Arg.Any<CancellationToken>());
        await store.Received(1).RetainClosePriceSnapshotAsync(draft.Snapshot, Arg.Any<CancellationToken>());

        var payload = JsonSerializer.Serialize(draft.Snapshot, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto);
        var hydrated = JsonSerializer.Deserialize(payload, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto)!;
        Action validate = () => SecurityClosePriceEvidence.Validate(hydrated);
        validate.Should().NotThrow();
        hydrated.Blockers.Should().BeEquivalentTo(draft.Snapshot.Blockers);
        Action removeBlocker = () => SecurityClosePriceEvidence.Validate(hydrated with { Blockers = [] });
        removeBlocker.Should().Throw<ArgumentException>();
        RetainForRead(store, new(hydrated, null));
        await service.Invoking(s => s.ApproveClosePriceSetAsync(Approve(draft))).Should().ThrowAsync<InvalidOperationException>();
        await store.DidNotReceive().RetainClosePriceApprovalAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(),
            Arg.Any<SecurityClosePriceApprovalDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetainedReadAndApprovalRetryDoNotConsultLaterQuotes()
    {
        var (service, store) = BuildService();
        var draft = await service.CreateClosePriceSetAsync(Request());
        RetainForRead(store, draft);
        var approved = await service.ApproveClosePriceSetAsync(Approve(draft));
        // Hydration through source-generated persistence exercises the restart representation.
        var payload = JsonSerializer.Serialize(approved.Snapshot, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto);
        var hydrated = JsonSerializer.Deserialize(payload, SecurityMasterClosePricesJsonContext.Default.SecurityClosePriceSnapshotDto)!;
        RetainForRead(store, new(hydrated, approved.Approval));
        store.ClearReceivedCalls();
        var restarted = new SecurityMasterPricingService(store,
            Substitute.For<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>(), NullLogger<SecurityMasterPricingService>.Instance);
        (await restarted.ApproveClosePriceSetAsync(Approve(draft))).Should().BeEquivalentTo(approved);
        await store.DidNotReceive().GetRawPricesAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
        await restarted.Invoking(s => s.ApproveClosePriceSetAsync(Approve(draft) with { Rationale = "Replace review" }))
            .Should().ThrowAsync<InvalidOperationException>();
        (await restarted.GetClosePriceSetAsync("account-b", "book-a", hydrated.ValuationDate, hydrated.SnapshotId)).Should().BeNull();
        (await restarted.GetClosePriceSetAsync("account-a", "book-b", hydrated.ValuationDate, hydrated.SnapshotId)).Should().BeNull();
        (await restarted.GetClosePriceSetAsync("account-a", "book-a", hydrated.ValuationDate.AddDays(1), hydrated.SnapshotId)).Should().BeNull();
    }

    [Fact]
    public async Task MissingScopeAndFutureValuationCutoffCannotCreateCertifiableSet()
    {
        var (service, _) = BuildService();
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with { AccountId = "" })).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with { BookId = "" })).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with { RequestId = Guid.Empty })).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.CreateClosePriceSetAsync(Request() with { EvaluatedAsOf = Close.AddDays(1) })).Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FutureEvaluationOrKnowledgeCutoffIsRejectedBeforeSourceEvaluation(bool futureKnowledge)
    {
        var (service, store) = BuildService();
        var future = DateTimeOffset.UtcNow.AddDays(1);
        var request = futureKnowledge
            ? Request() with { KnowledgeAsOf = future }
            : Request() with { EvaluatedAsOf = future, ValuationDate = DateOnly.FromDateTime(future.UtcDateTime) };
        await service.Invoking(s => s.CreateClosePriceSetAsync(request)).Should().ThrowAsync<ArgumentException>()
            .WithMessage("*cutoffs cannot be later than the snapshot capture time*");
        await store.DidNotReceive().GetRawPricesAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>());
        await store.DidNotReceive().RetainPriceSelectionAsync(Arg.Any<SecurityPriceGoldenCopyDto>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().RetainClosePriceSnapshotAsync(Arg.Any<SecurityClosePriceSnapshotDto>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NullSecurityCollectionOrEntryIsRejectedWithoutRetention(bool nullEntry)
    {
        var (service, store) = BuildService();
        var request = Request() with { Securities = nullEntry ? new SecurityClosePriceInputDto[] { null! } : null! };
        await service.Invoking(s => s.CreateClosePriceSetAsync(request)).Should().ThrowAsync<ArgumentException>();
        await store.DidNotReceive().RetainPriceSelectionAsync(Arg.Any<SecurityPriceGoldenCopyDto>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().RetainClosePriceSnapshotAsync(Arg.Any<SecurityClosePriceSnapshotDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DefaultKnowledgeCutoffAndCreatedAtUseTheSameCaptureInstant()
    {
        var (service, _) = BuildService();
        var before = DateTimeOffset.UtcNow;
        var draft = await service.CreateClosePriceSetAsync(Request());
        draft.Snapshot.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTimeOffset.UtcNow);
        draft.Snapshot.KnowledgeAsOf.Should().Be(draft.Snapshot.CreatedAt);
        draft.Snapshot.Lines.Single().Selection!.KnowledgeAsOf.Should().Be(draft.Snapshot.CreatedAt);
    }

    [Fact]
    public async Task PriceHistoryRequiresExactAccountBookAndValuationDateBeforeReadingStore()
    {
        var (service, store) = BuildService();
        var date = DateOnly.FromDateTime(Close.UtcDateTime);
        await service.Invoking(s => s.GetClosePriceSetsAsync("", "book-a", date)).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.GetClosePriceSetsAsync("account-a", " ", date)).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.GetClosePriceSetsAsync("account-a", "book-a", default)).Should().ThrowAsync<ArgumentException>();
        await store.DidNotReceive().GetClosePriceSetsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PriceHistoryNormalizesOnlySurroundingScopeWhitespaceAndPreservesCancellation()
    {
        var (service, store) = BuildService();
        var draft = await service.CreateClosePriceSetAsync(Request());
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<SecurityClosePriceSetDto> expected = [draft];
        store.GetClosePriceSetsAsync("account-a", "book-a", draft.Snapshot.ValuationDate, cancellation.Token).Returns(expected);

        (await service.GetClosePriceSetsAsync(" account-a ", " book-a ", draft.Snapshot.ValuationDate, cancellation.Token))
            .Should().BeSameAs(expected);
        await store.Received(1).GetClosePriceSetsAsync("account-a", "book-a", draft.Snapshot.ValuationDate, cancellation.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DurableValidationRefusesCutoffsBeyondRetainedCaptureEvenWhenSelectionAgrees(bool futureKnowledge)
    {
        var (service, _) = BuildService();
        var draft = await service.CreateClosePriceSetAsync(Request());
        var future = draft.Snapshot.CreatedAt.AddDays(1);
        var evaluatedAsOf = futureKnowledge ? draft.Snapshot.EvaluatedAsOf : future;
        var knowledgeAsOf = futureKnowledge ? future : draft.Snapshot.KnowledgeAsOf;
        var line = draft.Snapshot.Lines.Single();
        var forged = draft.Snapshot with
        {
            EvaluatedAsOf = evaluatedAsOf,
            KnowledgeAsOf = knowledgeAsOf,
            ValuationDate = DateOnly.FromDateTime(evaluatedAsOf.UtcDateTime),
            Lines = [line with { Selection = line.Selection! with { EvaluatedAsOf = evaluatedAsOf, KnowledgeAsOf = knowledgeAsOf } }]
        };
        Action retain = () => SecurityClosePriceEvidence.Validate(forged);
        retain.Should().Throw<ArgumentException>();
    }

    private static CreateSecurityClosePriceSetRequest Request(SecurityClosePriceOverrideDto? manual = null)
        => new("account-a", "book-a", DateOnly.FromDateTime(Close.UtcDateTime), Close, [new(SecurityId, manual)], 2m, "maker");
    private static ApproveSecurityClosePriceSetRequest Approve(SecurityClosePriceSetDto set, string reviewer = "checker")
        => new(set.Snapshot.AccountId, set.Snapshot.BookId, set.Snapshot.ValuationDate, set.Snapshot.SnapshotId, reviewer,
            "Reviewed both source rows and the adjustment");
    private static void RetainForRead(ISecurityMasterPricingStore store, SecurityClosePriceSetDto set)
        => store.GetClosePriceSetAsync(set.Snapshot.AccountId, set.Snapshot.BookId, set.Snapshot.ValuationDate,
            set.Snapshot.SnapshotId, Arg.Any<CancellationToken>()).Returns(set);

    private static (SecurityMasterPricingService, ISecurityMasterPricingStore) BuildService(string scenario = "fresh")
    {
        var store = Substitute.For<ISecurityMasterPricingStore>();
        var query = Substitute.For<Meridian.Application.SecurityMaster.ISecurityMasterQueryService>();
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(new SecurityDetailDto(
            SecurityId, "Bond", SecurityStatusDto.Active, "Close test", "USD", JsonSerializer.SerializeToElement(new { }),
            JsonSerializer.SerializeToElement(new { }), [], [], 1, Close.AddDays(-10), null));
        store.GetHierarchyAsOfAsync(SecurityId, "account-a", Close, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
            .Returns(new SecurityPricingHierarchyDto(SecurityId, "account-a", [new(1, "source", "Source", 1)], Close.AddDays(-10), "operator"));
        var unit = scenario == "unsupported" ? SecurityPriceUnit.Unspecified : SecurityPriceUnit.PercentOfPar;
        var sourcePrice = scenario switch
        {
            "zero" => 0m,
            "negative" => -100m,
            "extreme" => 0.0000000001m,
            _ => 100m
        };
        var comparisonPrice = scenario switch
        {
            "dispersion" => 110m,
            "extreme" => 999999999999999999m,
            _ => 100.2m
        };
        store.GetRawPricesAsync(SecurityId, Close, Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>()).Returns(new SecurityRawPriceDto[]
        {
            new("source", sourcePrice,
                scenario == "stale" ? Close.AddDays(-5) : Close.AddHours(-1), unit),
            new("comparison", comparisonPrice, Close.AddHours(-2),
                scenario == "mixed" ? SecurityPriceUnit.CurrencyPerUnit : unit)
        });
        return (new(store, query, NullLogger<SecurityMasterPricingService>.Instance), store);
    }
}
