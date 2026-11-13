using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.Storage;

public sealed class ConsolidationTransitionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-transitions-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PreviewRetry_PreservesFirstTimestampAndApprovalCannotChangeReviewedOperands()
    {
        var preview = Preview();
        var store = Store(preview);
        var first = await store.RetainPreviewAsync(preview);
        var retry = await store.RetainPreviewAsync(preview with { PreparedAt = preview.PreparedAt.AddHours(1) });
        Assert.Equal(first.PreparedAt, retry.PreparedAt);
        var approved = Receipt(first);
        await store.AppendAsync(approved);
        Assert.Equal(approved.ContentHash, (await store.AppendAsync(approved)).ContentHash);
        var changed = approved with { ApprovedBy = "another-reviewer" };
        changed = changed with { ContentHash = ConsolidationTransitionRules.ReceiptHash(changed) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(changed));
        Assert.Single(await store.ListAsync(preview.Request.OrganizationId, preview.Request.OwnershipRootId));
        Assert.Null(await Store(preview, "tenant-b").GetAsync(approved.TransitionId));
    }

    [Fact]
    public async Task Approval_RequiresRetainedIndependentPreviewAndExactSourceOperands()
    {
        var preview = Preview();
        var store = Store(preview);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(Receipt(preview)));
        await store.RetainPreviewAsync(preview);
        var self = Receipt(preview) with { ApprovedBy = preview.PreparedBy };
        self = self with { ContentHash = ConsolidationTransitionRules.ReceiptHash(self) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(self));
        var tampered = Receipt(preview) with { PreviousPostedJournalIds = [Guid.NewGuid()] };
        tampered = tampered with { ContentHash = ConsolidationTransitionRules.ReceiptHash(tampered) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(tampered));
        Assert.Empty(await store.ListAsync(preview.Request.OrganizationId, preview.Request.OwnershipRootId));
    }

    [Fact]
    public async Task RootCas_RejectsForkAndHistoricalBookReuse()
    {
        var first = Preview();
        var sibling = Preview(first.Request with { TransitionId = Guid.NewGuid(), NewEliminationBookId = Guid.NewGuid() });
        var reuse = Preview(first.Request with
        {
            TransitionId = Guid.NewGuid(),
            PreviousEliminationBookId = first.Request.NewEliminationBookId,
            NewEliminationBookId = first.Request.PreviousEliminationBookId,
            CutoverDate = first.Request.CutoverDate.AddDays(2)
        }, 2, first.Request.TransitionId);
        var store = Store(first, previews: [sibling, reuse]);
        foreach (var value in new[] { first, sibling, reuse })
            await store.RetainPreviewAsync(value);
        await store.AppendAsync(Receipt(first));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(Receipt(sibling)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(Receipt(reuse)));
    }

    [Fact]
    public async Task SharedLease_FencesApprovalButAllowsPreviewRetentionAndCallbackReads()
    {
        var preview = Preview();
        var store = Store(preview);
        await using var read = await store.AcquireAuthorityLeaseAsync();
        await store.RetainPreviewAsync(preview);
        var mutation = store.AppendValidatedAsync(Receipt(preview), async ct =>
        {
            await using var nested = await store.AcquireAuthorityLeaseAsync(ct);
            Assert.Empty(await store.ListAsync(preview.Request.OrganizationId, preview.Request.OwnershipRootId, ct));
            Assert.NotNull(await store.GetPreviewAsync(preview.PreviewId, ct));
        });
        Assert.False(mutation.IsCompleted);
        await read.DisposeAsync();
        await mutation.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FailedFinalValidation_LeavesNoApprovedRouteAndDurableStateSurvivesReload()
    {
        var preview = Preview();
        var store = Store(preview);
        await store.RetainPreviewAsync(preview);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendValidatedAsync(Receipt(preview),
            _ => throw new InvalidOperationException("Source books changed")));
        Assert.Empty(await store.ListAsync(preview.Request.OrganizationId, preview.Request.OwnershipRootId));
        await store.AppendAsync(Receipt(preview));
        var alternate = Path.Combine(_directory, "restart", "transitions.json");
        Directory.CreateDirectory(Path.GetDirectoryName(alternate)!);
        File.Copy(Path.Combine(_directory, "transitions.json"), alternate);
        var restarted = Store(preview, path: alternate);
        Assert.Equal(Receipt(preview).ContentHash, (await restarted.GetAsync(preview.Request.TransitionId))!.ContentHash);
        var json = await File.ReadAllTextAsync(alternate);
        await File.WriteAllTextAsync(alternate, json.Replace("Reviewed reporting cutover", "Tampered cutover", StringComparison.Ordinal));
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.GetAsync(preview.Request.TransitionId));
    }

    [Fact]
    public async Task BookOwnerAndCompanyMustMatchTheRetainedScope()
    {
        var preview = Preview();
        await Assert.ThrowsAsync<TenantScopeRejectedException>(() => Store(preview, company: "foreign-company").RetainPreviewAsync(preview));
    }

    internal static ConsolidationTransitionPreviewDto Preview(ConsolidationTransitionRequestDto? request = null, long version = 1, Guid? previous = null)
    {
        request ??= new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 11, 1), "Reviewed reporting cutover", "retained://reporting-transition");
        var evidence = new ConsolidationEvidenceDto(new(request.OrganizationId, request.OwnershipRootId, request.NewEliminationBookId, request.NewPeriodId, request.CutoverDate),
            "scope", "source-hash", [new(request.NewEliminationBookId, 0, 0)], "perimeter-hash", "w10-v2", [], [], "USD", ["entity-a", "entity-b"]);
        var value = new ConsolidationTransitionPreviewDto(Guid.Empty, request, version, previous, "tenant-a", "company-a", "preparer",
            new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), "old-book-hash", [Guid.NewGuid()], request.CutoverDate.AddDays(-1),
            ["entity-a", "entity-b"], evidence, [], [], [], "");
        value = value with { Fingerprint = ConsolidationTransitionRules.PreviewFingerprint(value) };
        return value with { PreviewId = ConsolidationTransitionRules.PreviewId(value) };
    }

    internal static ConsolidationTransitionReceiptDto Receipt(ConsolidationTransitionPreviewDto preview)
    {
        var value = new ConsolidationTransitionReceiptDto(preview.Request.TransitionId, preview.PreviewId, preview.RootVersion, preview.PreviousTransitionId,
            preview.Request, preview.TenantId, preview.CompanyId, preview.PreparedBy, "independent-reviewer", preview.PreparedAt.AddMinutes(1), preview.Fingerprint,
            preview.PreviousBookEvidenceDigest, preview.PreviousPostedJournalIds, preview.PreviousLatestPostedAsOf, preview.PreviousEntityIds, preview.NewEvidence, "");
        return value with { ContentHash = ConsolidationTransitionRules.ReceiptHash(value) };
    }

    private ConsolidationTransitionFileStore Store(ConsolidationTransitionPreviewDto preview, string tenant = "tenant-a",
        string company = "company-a", IReadOnlyList<ConsolidationTransitionPreviewDto>? previews = null, string? path = null)
    {
        var journals = new Mock<ILedgerJournalStore>();
        foreach (var value in new[] { preview }.Concat(previews ?? []))
            foreach (var book in new[] { value.Request.PreviousEliminationBookId, value.Request.NewEliminationBookId })
                journals.Setup(x => x.GetLedgerBookAsync(book, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerBookRecord(book, "fund",
                    value.Request.OwnershipRootId, FundStructureNodeKindDto.Fund, "Elimination book", "USD", preview.PreparedAt, preview.PreparedAt));
        var owners = new Mock<IFundProfileTenancyRegistry>();
        owners.Setup(x => x.ResolveAsync("fund", It.IsAny<CancellationToken>())).ReturnsAsync(new FundProfileOwnership("fund", "tenant-a", company));
        return new(path ?? Path.Combine(_directory, "transitions.json"), new TenantAccessor(tenant), journals.Object, owners.Object);
    }
    internal sealed class TenantAccessor(string tenant) : IFundScopeTenantAccessor { public string? ResolveCallerTenant() => tenant; }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
