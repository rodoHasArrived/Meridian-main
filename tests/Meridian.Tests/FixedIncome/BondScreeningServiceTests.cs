using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
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
    [InlineData("European")]
    [InlineData("Bermudan")]
    public async Task CallProtectionDiscardsExpiredDiscreteExercisesAndTheirLifecycleSummary(string style)
    {
        var prior = AsOf.AddMonths(-5);
        var next = prior.AddYears(1);
        var bond = Bond(1, callable: true, callDate: prior) with
        {
            CallSchedule = style == "European"
                ? Calls(Call(prior, style), Call(next, style))
                : Calls(Call(prior, style, next, "Annual"))
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddMonths(1) });

        page.Rows.Should().ContainSingle().Which.Reference.SecurityId.Should().Be(Id(1));
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(30, false)]
    [InlineData(31, false)]
    public async Task CallProtectionUsesTheNextBermudanExerciseRatherThanItsExpiredWindowStart(int days, bool matches)
    {
        var prior = AsOf.AddMonths(-5);
        var bond = Bond(1, callable: true, callDate: prior) with
        {
            CallSchedule = Calls(Call(prior, "Bermudan", prior.AddYears(2), "Quarterly"))
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddDays(days) });

        page.Rows.Any().Should().Be(matches, "the next quarterly exercise is December 12 and the protection boundary is exclusive");
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task DiscreteExerciseExpiryUsesTheScreeningDate(int days, bool matches)
    {
        var exercise = AsOf.AddDays(days);
        var bond = Bond(1, callable: true, callDate: exercise) with
        {
            CallSchedule = Calls(Call(exercise, "European"), Call(AsOf.AddYears(1), "European"))
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddDays(5) });

        page.Rows.Any().Should().Be(matches, "discrete dates on or before the economic screening date have expired");
    }

    [Theory]
    [InlineData("European")]
    [InlineData("Bermudan")]
    public async Task FullyExpiredDiscreteCallSchedulesDoNotInventAnotherIssuerRight(string style)
    {
        var prior = AsOf.AddMonths(-5);
        var bond = Bond(1, callable: true, callDate: prior) with
        {
            CallSchedule = Calls(Call(prior, style, prior, "Annual"))
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddYears(1) });

        page.Rows.Should().ContainSingle();
    }

    [Theory]
    [InlineData("American")]
    [InlineData("Continuous")]
    [InlineData(null)]
    public async Task AnElapsedContinuousOrUnknownStartDoesNotEstablishCallProtection(string? style)
    {
        var prior = AsOf.AddMonths(-5);
        var bond = Bond(1, callable: true, callDate: prior) with
        {
            CallSchedule = Calls(Call(prior, style), Call(AsOf.AddYears(2), "European"))
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddMonths(1) });

        page.Rows.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Daily")]
    [InlineData(null)]
    public async Task UnresolvableBermudanDatesRemainUnverified(string? frequency)
    {
        var prior = AsOf.AddMonths(-5);
        var bond = Bond(1, callable: true, callDate: prior) with
        {
            CallSchedule = Calls(Call(prior, "Bermudan", AsOf.AddYears(1), frequency))
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddMonths(1) });

        page.Rows.Should().BeEmpty();
        page.Gaps.Should().Contain(gap => gap.Reason.Contains("discrete call dates cannot be resolved", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("European")]
    [InlineData("Bermudan")]
    public async Task AnExplicitSingleExerciseDateDoesNotRequireAMonthBasedGrid(string style)
    {
        var exercise = AsOf.AddDays(10);
        var bond = Bond(1, callable: true) with
        {
            CallSchedule = Calls(Call(exercise, style, exercise, "Daily"))
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddDays(5) });

        page.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task AnUnmatchedHistoricalScalarRemainsConservativeEligibilityEvidence()
    {
        var bond = Bond(1, callable: true, callDate: AsOf.AddMonths(-6)) with
        {
            CallSchedule = Calls(Call(AsOf.AddMonths(-5), "European"), Call(AsOf.AddYears(1), "European"))
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddMonths(1) });

        page.Rows.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Following")]
    [InlineData("Unsupported")]
    public async Task RawDatesDoNotCertifyExpiryWhenTheirDeclaredAdjustmentCannotBeResolved(string convention)
    {
        var bond = Bond(1, callable: true) with
        {
            CallSchedule = Calls(Call(AsOf.AddDays(-1), "European")),
            ScheduleConventions = ScheduleConventionTerms.Empty with
            {
                BusinessDayConvention = convention,
                PaymentCalendars = ["retained-custom-holidays"]
            }
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddDays(5) });

        page.Rows.Should().BeEmpty();
        page.Gaps.Should().Contain(gap => gap.Reason.Contains("retained calendar resolution", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictingStartAliasesCannotHideAnEligibleFutureCall(bool swap)
    {
        var past = AsOf.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var future = AsOf.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var bond = Bond(1, callable: true) with
        {
            CallSchedule = Calls(new
            {
                exerciseDate = swap ? future : past,
                callDate = swap ? past : future,
                exerciseStyle = "European"
            })
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddDays(5) });

        page.Rows.Should().BeEmpty();
        page.Gaps.Should().Contain(gap => gap.Reason.Contains("conflicting eligibility dates", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConflictingEndAliasesCannotFallBackToAnAssumedWindow(bool swap)
    {
        var prior = AsOf.AddMonths(-5);
        var earlyEnd = prior.AddMonths(3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lateEnd = prior.AddYears(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var bond = Bond(1, callable: true) with
        {
            CallSchedule = Calls(new
            {
                callDate = prior.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                callEndDate = swap ? lateEnd : earlyEnd,
                windowEnd = swap ? earlyEnd : lateEnd,
                exerciseStyle = "Bermudan",
                exerciseFrequency = "Quarterly"
            })
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddDays(5) });

        page.Rows.Should().BeEmpty();
        page.Gaps.Should().Contain(gap => gap.Reason.Contains("conflicting end dates", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("start")]
    [InlineData("end")]
    public async Task PresentMalformedDateAliasesRemainUnverified(string field)
    {
        var entry = new Dictionary<string, object?>
        {
            ["callDate"] = AsOf.AddMonths(-5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["exerciseStyle"] = "Bermudan",
            ["exerciseFrequency"] = "Annual"
        };
        entry[field == "start" ? "exerciseDate" : "callEndDate"] = "not-a-date";
        var bond = Bond(1, callable: true) with { CallSchedule = Calls(entry) };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddDays(5) });

        page.Rows.Should().BeEmpty();
        page.Gaps.Should().Contain(gap => gap.Reason.Contains("invalid or conflicting", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IdenticalDateAliasesRemainSupported()
    {
        var start = AsOf.AddMonths(-5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = AsOf.AddMonths(7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var bond = Bond(1, callable: true) with
        {
            CallSchedule = Calls(new
            {
                callDate = start,
                exerciseDate = start,
                callEndDate = end,
                windowEnd = end,
                exerciseStyle = "Bermudan",
                exerciseFrequency = "Annual"
            })
        };

        var page = await new BondScreeningService(new References([bond]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddDays(5) });

        page.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task HolderPutsDoNotBecomeIssuerCallEligibilityOrReplaceMissingCallTerms()
    {
        var putOnly = Bond(1) with { PutSchedule = Calls(new { putDate = AsOf.AddDays(1), exerciseStyle = "European" }) };
        var missingCall = Bond(2, callable: true) with { PutSchedule = putOnly.PutSchedule };

        var page = await new BondScreeningService(new References([putOnly, missingCall]), new Ratings([]), new FrozenClock())
            .ScreenAsync("tenant", Query() with { CallProtectedThrough = AsOf.AddMonths(1) });

        page.Rows.Should().ContainSingle().Which.Reference.SecurityId.Should().Be(Id(1));
        page.Gaps.Should().Contain(gap => gap.SecurityId == Id(2) && gap.Reason.Contains("call date", StringComparison.Ordinal));
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
    private static JsonElement Calls(params object[] entries) => JsonSerializer.SerializeToElement(entries);
    private static object Call(DateOnly date, string? style, DateOnly? end = null, string? frequency = null)
        => new
        {
            callDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            callEndDate = end?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            callPrice = 100m,
            exerciseStyle = style,
            exerciseFrequency = frequency
        };
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
