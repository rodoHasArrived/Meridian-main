using System.Text.Json;
using FluentAssertions;
using Meridian.Application.FundStructure;
using Meridian.Storage.FundStructure;

namespace Meridian.Tests.Application.FundStructure;

/// <summary>Operator review, legacy ownership ambiguity, and retry after an attribution commit.</summary>
public sealed class FundStructureTenantBackfillTests
{
    [Fact]
    public async Task Preview_ReviewedLegacyFund_DerivesCompletePlanWithoutWriting()
    {
        var snapshot = MakeSnapshot();
        var store = new MemoryStore(snapshot);
        var plan = await new FundStructureTenantBackfillRunner(store).PreviewAsync();

        plan.AttributionComplete.Should().BeTrue();
        plan.Stamps.Should().HaveCount(4);
        plan.StrictReadCounts.Should().ContainSingle().Which.Should().Be(new FundStructureTenantReadCount("tenant-a", 0, 3));
        plan.Evidence.Evidence.Single().CompanyId.Should().Be("company-separate-from-tenant");
        store.Commits.Should().Be(0);
        store.Disposals.Should().Be(1);
        plan.PlanHash.Should().Be(FundStructureTenantBackfillPlanner.Create(snapshot with
        {
            Rows = snapshot.Rows.Reverse().ToArray()
        }).PlanHash);
    }

    [Theory]
    [InlineData("mixed")]
    [InlineData("missing-registry")]
    [InlineData("existing-owner")]
    [InlineData("non-owning-link")]
    [InlineData("cycle")]
    [InlineData("seeded-child")]
    public void Preview_ConflictingOwnership_QuarantinesWholeComponentAndDependentLink(string conflict)
    {
        var snapshot = MakeSnapshot();
        var fund = snapshot.Rows.Single(row => row.Kind == "Fund");
        if (conflict == "mixed")
        {
            var second = Row("fund", Guid.NewGuid(), "Fund", [snapshot.Rows.Single(row => row.Kind == "Organization").Id]);
            snapshot = snapshot with { Rows = [.. snapshot.Rows, second], Evidence = [.. snapshot.Evidence, Evidence(second.Id, "tenant-b")] };
        }
        else if (conflict == "missing-registry") snapshot = snapshot with { Evidence = [] };
        else if (conflict == "existing-owner") snapshot = snapshot with
        {
            Rows = snapshot.Rows.Select(row => row.Id == fund.Id ? row with { TenantId = "tenant-b" } : row).ToArray()
        };
        else if (conflict == "non-owning-link") snapshot = snapshot with
        {
            Rows = snapshot.Rows.Select(row => row.Kind == "OwnershipLink"
                ? row with { RetainedRow = JsonSerializer.SerializeToElement(new { relationship_type = "Advises" }) } : row).ToArray()
        };
        else if (conflict == "seeded-child")
        {
            var child = Row("fund", Guid.NewGuid(), "Fund", [fund.Id]);
            snapshot = snapshot with { Rows = [.. snapshot.Rows, child], Evidence = [.. snapshot.Evidence, Evidence(child.Id, "tenant-b")] };
        }
        else snapshot = snapshot with
        {
            Rows = snapshot.Rows.Select(row => row.Kind == "Organization" ? row with { Parents = [fund.Id] } : row).ToArray()
        };

        var plan = FundStructureTenantBackfillPlanner.Create(snapshot);

        plan.AttributionComplete.Should().BeFalse();
        plan.Stamps.Should().BeEmpty();
        plan.Exceptions.Should().HaveCount(snapshot.Rows.Count);
        plan.Exceptions.Should().Contain(row => row.NodeKind == "OwnershipLink");
        if (conflict == "existing-owner") plan.Evidence.Rows.Single(row => row.Id == fund.Id).TenantId.Should().Be("tenant-b");
    }

    [Fact]
    public void Preview_UnrelatedOrphan_RemainsInExceptionQueueWithoutInventedTenant()
    {
        var snapshot = MakeSnapshot();
        var orphan = Row("legal_entity", Guid.NewGuid(), "LegalEntity");
        var plan = FundStructureTenantBackfillPlanner.Create(snapshot with { Rows = [.. snapshot.Rows, orphan] });

        plan.Stamps.Should().HaveCount(4);
        plan.Stamps.Should().NotContain(stamp => stamp.Id == orphan.Id);
        plan.Exceptions.Should().ContainSingle().Which.NodeId.Should().Be(orphan.Id);
        plan.Exceptions.Single().CandidateTenantIds.Should().BeEmpty();
        plan.AttributionComplete.Should().BeFalse();
    }

    [Fact]
    public async Task Apply_NewlyDerivableQuarantine_RequiresSeparateResolutionReview()
    {
        var snapshot = MakeSnapshot();
        var node = snapshot.Rows.Single(row => row.Kind == "Fund");
        var store = new MemoryStore(snapshot with
        {
            RetainedQuarantine = [JsonSerializer.SerializeToElement(new
            {
                node_id = node.Id, reason = "PriorMissingEvidence", resolved_at_utc = (string?)null
            })]
        });
        var runner = new FundStructureTenantBackfillRunner(store);
        var preview = await runner.PreviewAsync();

        preview.AttributionComplete.Should().BeFalse();
        preview.Exceptions.Should().BeEmpty();
        preview.BlockingReasons.Should().ContainMatch("*unresolved quarantine*");
        var apply = () => runner.ApplyAsync(Guid.NewGuid(), preview.PlanHash, "operator", "review/123");
        await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blockers*");
        store.Commits.Should().Be(0);
    }

    [Theory]
    [InlineData("tenant-b", false)]
    [InlineData(null, false)]
    [InlineData("tenant-a", true)]
    public void Preview_RetainedResolution_MustAgreeWithAuthoritativeEvidence(string? resolvedTenant, bool compatible)
    {
        var snapshot = MakeSnapshot();
        var fund = snapshot.Rows.Single(row => row.Kind == "Fund");
        var plan = FundStructureTenantBackfillPlanner.Create(snapshot with
        {
            RetainedQuarantine = [JsonSerializer.SerializeToElement(new
            {
                node_id = fund.Id, resolved_at_utc = "2026-09-01T00:00:00Z", resolved_tenant_id = resolvedTenant
            })]
        });

        plan.AttributionComplete.Should().Be(compatible);
        if (!compatible) plan.BlockingReasons.Should().ContainMatch("*resolution conflicts*");
    }

    [Fact]
    public async Task Apply_ChangedEvidenceOrGraph_RejectsReviewedFingerprintWithoutWrites()
    {
        var store = new MemoryStore(MakeSnapshot());
        var runner = new FundStructureTenantBackfillRunner(store);
        var reviewed = await runner.PreviewAsync();
        store.Snapshot = store.Snapshot with { SchemaIdentity = "changed-schema" };

        var action = () => runner.ApplyAsync(Guid.NewGuid(), reviewed.PlanHash, "operator", "review/123");

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*stale*");
        store.Commits.Should().Be(0);
    }

    [Fact]
    public async Task Apply_CommittedRequestRetry_ReturnsSameReceiptAndRejectsIdentityReuse()
    {
        var store = new MemoryStore(MakeSnapshot());
        var runner = new FundStructureTenantBackfillRunner(store);
        var reviewed = await runner.PreviewAsync();
        var runId = Guid.NewGuid();

        var receipt = await runner.ApplyAsync(runId, reviewed.PlanHash, "operator", "review/123");
        store.Snapshot = store.Snapshot with { SchemaIdentity = "later-change" };
        var retry = await runner.ApplyAsync(runId, reviewed.PlanHash, "operator", "review/123");
        var wrongReview = () => runner.ApplyAsync(runId, reviewed.PlanHash, "operator", "review/other");

        retry.Should().Be(receipt);
        store.Commits.Should().Be(1);
        await wrongReview.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different review evidence*");
    }

    [Fact]
    public async Task Apply_SeparateDatabases_PreviewReportsBlockedAndNeverMutates()
    {
        var store = new MemoryStore(MakeSnapshot() with { SupportsAtomicApply = false });
        var runner = new FundStructureTenantBackfillRunner(store);
        var reviewed = await runner.PreviewAsync();

        reviewed.BlockingReasons.Should().ContainMatch("*separate databases*");
        var apply = () => runner.ApplyAsync(Guid.NewGuid(), reviewed.PlanHash, "operator", "review/123");
        await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*blockers*");
        store.Commits.Should().Be(0);
    }

    [Fact]
    public async Task Apply_CanceledReview_DoesNotOpenMutationSession()
    {
        var store = new MemoryStore(MakeSnapshot());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = () => new FundStructureTenantBackfillRunner(store)
            .ApplyAsync(Guid.NewGuid(), "reviewed-plan", "operator", "review/123", cts.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        store.Opens.Should().Be(0);
        store.Commits.Should().Be(0);
    }

    private static FundStructureTenantBackfillSnapshot MakeSnapshot()
    {
        var organizationId = Guid.NewGuid();
        var fundId = Guid.NewGuid();
        var sleeveId = Guid.NewGuid();
        return new("source-identity", "schema-identity",
        [
            Row("organization", organizationId, "Organization"),
            Row("fund", fundId, "Fund", [organizationId]),
            Row("sleeve", sleeveId, "Sleeve", [fundId]),
            Row("ownership_link", Guid.NewGuid(), "OwnershipLink", [fundId], [sleeveId], false)
        ], [Evidence(fundId, "tenant-a")], []);
    }

    private static FundStructureTenantBackfillRow Row(string table, Guid id, string kind,
        IReadOnlyList<Guid>? parents = null, IReadOnlyList<Guid>? children = null, bool isNode = true)
        => new(table, id, kind, isNode, null, parents ?? [], children ?? [],
            JsonSerializer.SerializeToElement(new { id, relationship_type = "Owns" }));

    private static FundStructureTenantBackfillEvidence Evidence(Guid fundId, string tenant)
    {
        var bookId = Guid.NewGuid();
        return new(bookId, "retained-fund", fundId, tenant, "company-separate-from-tenant",
            JsonSerializer.SerializeToElement(new { ledger_book_id = bookId, fund_profile_id = "retained-fund", fund_structure_node_id = fundId, fund_structure_node_kind = "Fund" }),
            JsonSerializer.SerializeToElement(new { fund_profile_id = "retained-fund", tenant_id = tenant, company_id = "company-separate-from-tenant" }));
    }

    private sealed class MemoryStore(FundStructureTenantBackfillSnapshot snapshot) : IFundStructureTenantBackfillStore
    {
        public FundStructureTenantBackfillSnapshot Snapshot { get; set; } = snapshot;
        public int Opens { get; private set; }
        public int Disposals { get; set; }
        public int Commits { get; set; }
        public FundStructureTenantBackfillReceipt? Receipt { get; set; }
        public Task<IFundStructureTenantBackfillSession> OpenSessionAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Opens++;
            return Task.FromResult<IFundStructureTenantBackfillSession>(new Session(this, Snapshot));
        }

        private sealed class Session(MemoryStore store, FundStructureTenantBackfillSnapshot snapshot) : IFundStructureTenantBackfillSession
        {
            public FundStructureTenantBackfillSnapshot Snapshot { get; } = snapshot;
            public Task<FundStructureTenantBackfillReceipt?> FindReceiptAsync(Guid runId, CancellationToken ct)
                => Task.FromResult(store.Receipt?.RunId == runId ? store.Receipt : null);
            public Task<FundStructureTenantBackfillReceipt> CommitAsync(Guid runId, string planHash, string operatorId,
                string reviewReference, JsonElement plan, IReadOnlyList<FundStructureTenantBackfillStamp> stamps,
                IReadOnlyList<FundStructureTenantBackfillException> exceptions, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                store.Commits++;
                store.Receipt = new(runId, planHash, operatorId, reviewReference, DateTimeOffset.UtcNow, stamps.Count, exceptions.Count, plan);
                return Task.FromResult(store.Receipt!);
            }
            public ValueTask DisposeAsync() { store.Disposals++; return ValueTask.CompletedTask; }
        }
    }
}
