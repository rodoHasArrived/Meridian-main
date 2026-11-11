using FluentAssertions;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Ledger;
using Xunit;

namespace Meridian.Tests.FinancialOperations;

public sealed class FundAdministrationRegisterTests : IDisposable
{
    private static readonly Guid Book = Guid.Parse("cccccccc-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-registers-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FixedAssetRegister_SurvivesRestart_ReplacesByAssetId_AndKeepsBooksApart()
    {
        var id = Guid.NewGuid();
        await new FileFixedAssetRegister(_root).UpsertAsync(Entry(id, 48_000m));
        await new FileFixedAssetRegister(_root).UpsertAsync(Entry(id, 48_000m) with { RetiredOn = new DateOnly(2027, 6, 30) });

        var restarted = new FileFixedAssetRegister(_root);
        // Depreciation already posted on the registered terms is never recomputed, so the terms are fixed.
        var reprice = () => restarted.UpsertAsync(Entry(id, 36_000m));
        var extend = () => restarted.UpsertAsync(Entry(id, 48_000m, months: 72));
        await reprice.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be changed*");
        await extend.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be changed*");
        var entry = (await restarted.ListAsync("tenant", "company", "fund", Book)).Should().ContainSingle().Subject;
        entry.Asset.Cost.Should().Be(48_000m);
        entry.RetiredOn.Should().Be(new DateOnly(2027, 6, 30));
        (await restarted.ListAsync("tenant", "company", "fund", Guid.NewGuid())).Should().BeEmpty();
        (await restarted.ListAsync("other-tenant", "company", "fund", Book)).Should().BeEmpty();
        var move = () => restarted.UpsertAsync(Entry(id, 48_000m) with { LedgerBookId = Guid.NewGuid() });
        await move.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different fund book*");
    }

    [Fact]
    public async Task FixedAssetRegister_RejectsAnAssetThatCannotBeScheduled()
    {
        var register = new FileFixedAssetRegister(_root);
        var noLife = () => register.UpsertAsync(Entry(Guid.NewGuid(), 36_000m, months: 0));
        var retiredEarly = () => register.UpsertAsync(Entry(Guid.NewGuid(), 36_000m) with { RetiredOn = new DateOnly(2025, 12, 31) });

        await noLife.Should().ThrowAsync<ArgumentException>();
        await retiredEarly.Should().ThrowAsync<ArgumentException>();
        (await register.ListAsync("tenant", "company", "fund", Book)).Should().BeEmpty();
    }

    [Fact]
    public async Task DistributionRegister_RetriesAreIdempotent_AndChangedTermsAreRefused()
    {
        var record = new DistributionDeclarationRecord("tenant", "company", "fund", Book, "lp-1", "dist-1", new DateOnly(2026, 4, 30),
            Guid.NewGuid(), 1_000_000m, 80_000m, 600_000m, 0.2m, 1m, 600_000m, 0m, 0m, 0m, 0m, 0m, "ops", Now);
        await new FileDistributionRegister(_root).RecordAsync(record);
        var retried = await new FileDistributionRegister(_root).RecordAsync(record with { DeclaredBy = "ops-2", DeclaredAtUtc = Now.AddHours(1) });

        retried.DeclaredBy.Should().Be("ops");
        var register = new FileDistributionRegister(_root);
        (await register.ListAsync("tenant", "company", "fund", Book, "lp-1")).Should().ContainSingle();
        (await register.ListAsync("tenant", "company", "fund", Book, "lp-2")).Should().BeEmpty();
        var changed = () => register.RecordAsync(record with { AmountToDistribute = 700_000m });
        await changed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different terms*");
    }

    private static FixedAssetRegisterEntry Entry(Guid id, decimal cost, int months = 36)
        => new(new FixedAssetRecord(id, "Server", "USD", cost, 0m, new DateOnly(2026, 1, 1), months, DepreciationMethod.StraightLine),
            "tenant", "company", "fund", Book, "controller", Now, "evidence://invoice");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
