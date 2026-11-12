using System.Globalization;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Instruments.FixedIncome;

namespace Meridian.Tests.FixedIncome;

public sealed class BondScreeningServiceTests
{
    private static readonly DateOnly AsOf = new(2026, 11, 12);
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CombinedFiltersAndResumptionNeverLoseSourcePageLeftovers()
    {
        var refs = new References(Enumerable.Range(1, 205).Select(index => Bond(index,
            issuer: index % 2 == 0 ? "Alpha Holdings" : "Beta Holdings")).ToArray());
        var service = new BondScreeningService(refs, new Ratings([]), new FrozenClock());
        var query = Query() with { Issuer = "alpha", Currency = "usd", CouponKind = "fixed", PageSize = 3 };
        var found = new List<Guid>();
        do
        {
            var page = await service.ScreenAsync("tenant", query);
            page.Rows.Should().HaveCountLessThanOrEqualTo(3);
            found.AddRange(page.Rows.Select(row => row.Reference.SecurityId));
            query = query with { Cursor = page.NextCursor };
        } while (query.Cursor is not null);
        found.Should().HaveCount(102).And.OnlyHaveUniqueItems();
        found.Should().Equal(refs.Rows.Where(row => row.IssuerName == "Alpha Holdings").Select(row => row.SecurityId));
        refs.PageSizes.Should().OnlyContain(size => size == BondScreeningService.SourcePageSize);
    }

    [Fact]
    public async Task ScanBudgetReturnsAContinuationEvenWhenNoCandidateMatches()
    {
        var refs = new References(Enumerable.Range(1, 1700).Select(index => Bond(index)).ToArray());
        var service = new BondScreeningService(refs, new Ratings([]), new FrozenClock());
        var query = Query() with { Issuer = "missing", PageSize = 1 };
        var first = await service.ScreenAsync("tenant", query);
        first.Rows.Should().BeEmpty();
        first.SourcePagesRead.Should().Be(16);
        first.NextCursor.Should().NotBeNull();
        var last = await service.ScreenAsync("tenant", query with { Cursor = first.NextCursor });
        last.SourcePagesRead.Should().Be(1);
        last.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ExactAgencyFilterUsesOnlyEligibleRetainedCodesAndSurfacesMissingEvidence()
    {
        var references = new References([Bond(1), Bond(2)]);
        var ratings = new Ratings([Rating(1, "S&P", "AA", Now.AddHours(-1)), Rating(1, "Moody's", "Aaa", Now.AddHours(-1)),
            Rating(1, "S&P", "AAA", Now.AddHours(1))]);
        var page = await new BondScreeningService(references, ratings, new FrozenClock()).ScreenAsync("tenant",
            Query() with { RatingAgency = "s&p", AcceptedRatings = ["AA"] });
        page.Rows.Should().ContainSingle().Which.Ratings.Should().ContainSingle().Which.Input.Rating.Should().Be("AA");
        page.Gaps.Should().Contain(row => row.SecurityId == Id(2) && row.Reason.Contains("S&P", StringComparison.OrdinalIgnoreCase));
        page.ReferenceKnowledgeTimeVerified.Should().BeFalse();
        page.Limitations.Should().Contain(row => row.Contains("recorded-information cutoff", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompetingSameAgencyRatingsDoNotPickTheFavorableCode()
    {
        var values = new[] { Rating(1, "S&P", "AA", Now.AddHours(-1)),
            Rating(1, "S&P", "AAA", Now.AddHours(-1)) with { Input = Rating(1, "S&P", "AAA", Now.AddHours(-1)).Input with { ProviderId = "other" } } };
        var page = await new BondScreeningService(new References([Bond(1)]), new Ratings(values), new FrozenClock())
            .ScreenAsync("tenant", Query() with { RatingAgency = "S&P", AcceptedRatings = ["AAA"] });
        page.Rows.Should().BeEmpty();
        page.Gaps.Should().Contain(row => row.Reason.Contains("Competing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CallProtectionRequiresActualEligibilityEvidenceAndAnExclusiveCallBoundary()
    {
        var through = AsOf.AddYears(1);
        var references = new References([Bond(1), Bond(2, callable: true, callDate: through),
            Bond(3, callable: true, callDate: through.AddDays(1)), Bond(4, callable: true)]);
        var page = await new BondScreeningService(references, new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = through });
        page.Rows.Select(row => row.Reference.SecurityId).Should().Equal(Id(1), Id(3));
        page.Gaps.Should().Contain(row => row.SecurityId == Id(4) && row.Reason.Contains("call date", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("currency")]
    [InlineData("coupon")]
    [InlineData("asof")]
    [InlineData("known")]
    [InlineData("maturity")]
    [InlineData("protection")]
    [InlineData("agency")]
    [InlineData("ratings")]
    [InlineData("pagesize")]
    [InlineData("tenant")]
    public async Task CursorIsBoundToEveryFilterAndTenant(string change)
    {
        var service = new BondScreeningService(new References([Bond(1), Bond(2)]), new Ratings([]), new FrozenClock());
        var original = Query() with { PageSize = 1, KnownAtUtc = Now.AddMinutes(-1) };
        var first = await service.ScreenAsync("tenant", original);
        var revised = original with { Cursor = first.NextCursor };
        revised = change switch
        {
            "issuer" => revised with { Issuer = "Alpha" },
            "currency" => revised with { Currency = "EUR" },
            "coupon" => revised with { CouponKind = "Floating" },
            "asof" => revised with { AsOf = AsOf.AddDays(-1) },
            "known" => revised with { KnownAtUtc = Now },
            "maturity" => revised with { MaturityFrom = AsOf.AddDays(1) },
            "protection" => revised with { CallProtectedThrough = AsOf.AddYears(1) },
            "agency" => revised with { RatingAgency = "S&P" },
            "ratings" => revised with { RatingAgency = "S&P", AcceptedRatings = ["AA"] },
            "pagesize" => revised with { PageSize = 2 },
            _ => revised
        };
        var action = () => service.ScreenAsync(change == "tenant" ? "other-tenant" : "tenant", revised);
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ChangedSourcePageRefusesOffsetReplayInsteadOfSkippingFinancialRows()
    {
        var references = new References([Bond(1), Bond(2)]);
        var service = new BondScreeningService(references, new Ratings([]), new FrozenClock());
        var query = Query() with { PageSize = 1 };
        var first = await service.ScreenAsync("tenant", query);
        references.Rows = [Bond(1) with { Currency = "EUR" }, Bond(2)];
        var resume = () => service.ScreenAsync("tenant", query with { Cursor = first.NextCursor });
        await resume.Should().ThrowAsync<ArgumentException>().WithMessage("*Reference data changed*");
    }

    private static BondScreeningQuery Query() => new(AsOf, Now, AsOf, AsOf.AddYears(10));
    private static Guid Id(int number) => Guid.ParseExact(number.ToString("x32", CultureInfo.InvariantCulture), "N");
    private static BondReferenceDto Bond(int number, string issuer = "Alpha Holdings", bool callable = false, DateOnly? callDate = null) =>
        new(Id(number), $"Bond {number}", "USD", issuer, "Senior", $"ISIN-{number}",
            new(Id(number), BondLifecycleStat.Active, AsOf.AddYears(-1), callDate, AsOf.AddYears(5), callable, 1),
            new(Id(number), "Actual360", 2, null, "Fixed", 4m, null, null, 1), 1);
    private static BondMarketObservationDto Rating(int number, string agency, string code, DateTimeOffset recorded)
    {
        var input = new BondMarketObservationInput($"rating-{number}-{agency}-{code}", 1, BondMarketObservationKind.CreditRating,
            "synthetic", "ratings", AsOf, BondMarketValueUnit.Rating, null, "synthetic", Sha256Digest.ComputeUtf8("rating-evidence"),
            "synthetic-verified", recorded, Id(number), Rating: code, RatingAgency: agency);
        return new(Guid.NewGuid(), input, recorded, "fixture", BondMarketDataRules.ContentHash(input));
    }
    private sealed class FrozenClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Ratings(IReadOnlyList<BondMarketObservationDto> rows) : IBondMarketDataService
    {
        public Task<IReadOnlyList<BondMarketObservationDto>> QueryAsync(string tenantId, BondMarketDataQuery query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BondMarketObservationDto>>(rows.Where(row => row.Input.SecurityId == query.SecurityId).ToArray());
        public Task<BondMarketImportResult> ImportAsync(string tenantId, string actor, BondMarketImportRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BondMarketObservationDto?> GetAsync(string tenantId, Guid observationId, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class References(IReadOnlyList<BondReferenceDto> rows) : IBondReferenceService
    {
        public IReadOnlyList<BondReferenceDto> Rows { get; set; } = rows;
        public List<int> PageSizes { get; } = [];
        public Task<BondReferencePageDto> GetMaturityLadderPageAsync(DateOnly from, DateOnly to, int pageSize = 100,
            string? cursor = null, DateOnly? asOf = null, CancellationToken ct = default)
        {
            PageSizes.Add(pageSize);
            var offset = cursor is null ? 0 : int.Parse(cursor, CultureInfo.InvariantCulture);
            var selected = Rows.Skip(offset).Take(pageSize).ToArray();
            return Task.FromResult(new BondReferencePageDto(selected, offset + selected.Length < Rows.Count
                ? (offset + selected.Length).ToString(CultureInfo.InvariantCulture) : null));
        }
        public Task<BondReferenceDto?> GetReferenceAsync(Guid securityId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BondLifecycleDto?> GetLifecycleAsync(Guid securityId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BondAccrualConventionDto?> GetAccrualConventionAsync(Guid securityId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BondReferenceDto>> GetIssuerLadderAsync(string issuerName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<BondReferenceDto>> GetMaturityLadderAsync(DateOnly from, DateOnly to, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
