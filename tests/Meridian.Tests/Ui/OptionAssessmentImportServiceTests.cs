using System.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Options;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.PortfolioRecords.Accounts;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class OptionAssessmentImportServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-option-assessments", Guid.NewGuid().ToString("N"));
    private static readonly Guid Account = Guid.Parse("f3550de4-a0d8-459d-9c8a-cb2eb3eb6f98");
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T12:00:00Z");
    private OptionAssessmentImportService Service() => new(_root,
        new FileEvidenceArtifactStore(_root, NullLogger<FileEvidenceArtifactStore>.Instance), new Clock());

    [Fact]
    public void Normalization_PreservesExactAmountsAndArbitraryLongShortOptionEquityLegs()
    {
        var value = OptionAssessmentValidation.Normalize(Assessment());
        Assert.Equal("123.1234567890123456789012345", value.InitialMarginAfter);
        Assert.Equal(3, value.Basket.Legs.Count);
        Assert.Equal("Sell", value.Basket.Legs[0].Side);
        Assert.Contains(value.Basket.Legs, leg => leg.InstrumentType == "Equity");
        Assert.Contains(value.Basket.Legs, leg => leg.Right == "Put");
    }

    [Fact]
    public void LegKinds_NormalizeWireStringsWithoutChangingCanonicalBasketFingerprint()
    {
        var canonical = Assessment();
        var mixedCase = canonical with
        {
            Basket = canonical.Basket with
            {
                Legs = canonical.Basket.Legs.Select(leg => leg with
                {
                    InstrumentType = leg.LegId == "c" ? " eQuItY " : " oPtIoN "
                }).ToArray()
            }
        };

        var normalized = OptionAssessmentValidation.Normalize(mixedCase);

        Assert.Equal(["Option", "Option", "Equity"], normalized.Basket.Legs.Select(leg => leg.InstrumentType));
        Assert.Equal(OptionAssessmentValidation.CanonicalBytes(OptionAssessmentValidation.Normalize(canonical)),
            OptionAssessmentValidation.CanonicalBytes(normalized));
        Assert.Equal(OptionAssessmentValidation.BasketFingerprint(canonical),
            OptionAssessmentValidation.BasketFingerprint(normalized));
    }

    [Theory]
    [InlineData("Warrant")]
    [InlineData("Bond")]
    [InlineData("1")]
    [InlineData("Option, Equity")]
    [InlineData("")]
    [InlineData(null)]
    public void LegKinds_RejectUnknownWireValuesBeforeTypedDispatch(string? instrumentType)
    {
        var assessment = Assessment();
        var changed = assessment with
        {
            Basket = assessment.Basket with
            {
                Legs = [assessment.Basket.Legs[0] with { InstrumentType = instrumentType! }]
            }
        };

        var error = Assert.Throws<ArgumentException>(() => OptionAssessmentValidation.Normalize(changed));

        Assert.Equal("instrumentType must be one of Option, Equity.", error.Message);
    }

    [Fact]
    public void LegKinds_EquityCannotRetainOptionIdentityFields()
    {
        var assessment = Assessment();
        var changed = assessment with
        {
            Basket = assessment.Basket with
            {
                Legs = [assessment.Basket.Legs[0] with { InstrumentType = "Equity" }]
            }
        };

        var error = Assert.Throws<ArgumentException>(() => OptionAssessmentValidation.Normalize(changed));

        Assert.Equal("Equity legs cannot carry option-specific identity fields.", error.Message);
    }

    [Theory]
    [InlineData("0.12345678901234567890123456789")]
    [InlineData("1e3")]
    [InlineData("NaN")]
    [InlineData("79228162514264337593543950336")]
    public void DecimalBoundary_RefusesRoundingAndNonliteralValues(string value) =>
        Assert.Throws<ArgumentException>(() => OptionAssessmentValidation.ExactDecimal(value, "amount"));

    [Fact]
    public void DecimalBoundary_AcceptsTinyExactlyRepresentableFixedPointValues()
    {
        Assert.Equal("0.0000000000000000000000000001", OptionAssessmentValidation.ExactDecimal("0.0000000000000000000000000001", "amount"));
        Assert.Equal("-0.00000001", OptionAssessmentValidation.ExactDecimal("-0.0000000100", "amount"));
    }

    [Fact]
    public void BasketFingerprint_MatchesDecimalFormattingButChangesForTermsAccountAndIntent()
    {
        var first = OptionAssessmentValidation.Normalize(Assessment());
        var same = OptionAssessmentValidation.Normalize(Assessment() with { Basket = Assessment().Basket with { Quantity = "01.000" } });
        Assert.Equal(OptionAssessmentValidation.BasketFingerprint(first), OptionAssessmentValidation.BasketFingerprint(same));
        var modified = OptionAssessmentValidation.Normalize(first with { Basket = first.Basket with { LimitPrice = "2.01" } });
        Assert.NotEqual(OptionAssessmentValidation.BasketFingerprint(first), OptionAssessmentValidation.BasketFingerprint(modified));
        Assert.NotEqual(OptionAssessmentValidation.BasketFingerprint(first), OptionAssessmentValidation.BasketFingerprint(first with { ExternalAccountId = "other" }));
        var leg = first.Basket.Legs[0];
        var close = first with { Basket = first.Basket with { Legs = [leg with { PositionIntent = "Close" }, .. first.Basket.Legs.Skip(1)] } };
        Assert.NotEqual(OptionAssessmentValidation.BasketFingerprint(first), OptionAssessmentValidation.BasketFingerprint(close));
    }

    [Fact]
    public void Csv_HandlesQuotedDeliverablesAndRejectsMalformedRowsWithoutDroppingThem()
    {
        const string header = "legId,instrumentType,symbol,side,positionIntent,ratio,currency,underlyingSymbol,right,expiration,strike,multiplier,exchange,deliverable";
        var rows = OptionAssessmentValidation.ParseLegsCsv(header + "\r\na,Option,XYZ-P,Sell,Open,1,USD,XYZ,Put,2027-01-15,100,100,SMART,\"100 shares, adjusted\"\r\n");
        Assert.Equal("100 shares, adjusted", rows.Single().Deliverable);
        Assert.Throws<ArgumentException>(() => OptionAssessmentValidation.ParseLegsCsv(header + "\na,Option,missing"));
        Assert.Throws<ArgumentException>(() => OptionAssessmentValidation.ParseLegsCsv(header + "\n\"unterminated"));
    }

    [Fact]
    public void Preview_RequiresSourceRejectsFutureAndChangesWhenSourceOrAccountChanges()
    {
        var service = Service();
        var first = service.Preview(Import());
        Assert.False(Directory.Exists(_root));
        Assert.Throws<ArgumentException>(() => service.Preview(Import() with { SourceContentBase64 = "" }));
        Assert.Throws<ArgumentException>(() => service.Preview(Import() with { Assessment = Assessment() with { AssessedAt = Now.AddSeconds(1) } }));
        var different = service.Preview(Import() with { SourceContentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("corrected broker source")) });
        Assert.NotEqual(first.PreviewFingerprint, different.PreviewFingerprint);
    }

    [Fact]
    public void ReportedMargin_RequiresItsOwnCurrencyRatherThanUsingBasketPremiumCurrency()
    {
        Assert.Throws<ArgumentException>(() => OptionAssessmentValidation.Normalize(Assessment() with { MarginCurrency = null }));
        var value = OptionAssessmentValidation.Normalize(Assessment() with { MarginCurrency = "EUR", CashImpact = "2", CashImpactCurrency = "GBP" });
        Assert.Equal("USD", value.Basket.Currency);
        Assert.Equal("EUR", value.MarginCurrency);
        Assert.Equal("GBP", value.CashImpactCurrency);
    }

    [Fact]
    public async Task Accept_RetainsSourceSurvivesRestartAndRetriesSameCommandExactlyOnce()
    {
        var service = Service();
        var command = Command(service, Import());
        var first = await service.AcceptAsync("tenant", "company", "operator", Account, command);
        var retry = await Service().AcceptAsync("tenant", "company", "operator", Account, command);
        Assert.Equal(first.AssessmentId, retry.AssessmentId);
        Assert.Equal(first.ContentSha256, retry.ContentSha256);
        Assert.Equal(first.RecordedAt, retry.RecordedAt);
        var records = await Service().ListAsync("tenant", "company", Account);
        Assert.Single(records);
        Assert.Equal("Approved", records[0].Assessment.Outcome);
        Assert.NotEmpty(records[0].SourceVaultId);
        Assert.Empty(await Service().ListAsync("other-tenant", "company", Account));
        Assert.Empty(await Service().ListAsync("tenant", "other-company", Account));
        Assert.Empty(await Service().ListAsync("tenant", "company", Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().AcceptAsync("tenant", "company", "other-operator", Account, command));
    }

    [Fact]
    public async Task IdempotencyAndReviewedFingerprint_RejectChangedPayload()
    {
        var service = Service();
        var command = Command(service, Import());
        await service.AcceptAsync("tenant", "company", "operator", Account, command);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptAsync("tenant", "company", "operator", Account,
            command with { Import = Import() with { Assessment = Assessment() with { RawBrokerStatus = "changed" } } }));
        var changed = Command(service, Import() with { Assessment = Assessment() with { RawBrokerStatus = "changed" } })
            with
        { IdempotencyKey = command.IdempotencyKey };
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptAsync("tenant", "company", "operator", Account, changed));
    }

    [Fact]
    public async Task Corrections_AppendImmutableHistoryAndRefuseASecondFork()
    {
        var service = Service();
        var first = await service.AcceptAsync("tenant", "company", "operator", Account, Command(service, Import()));
        var corrected = Import() with { CorrectsAssessmentId = first.AssessmentId, Assessment = Assessment() with { Outcome = "Rejected", RawBrokerStatus = "Revised broker result" } };
        var second = await service.AcceptAsync("tenant", "company", "operator", Account, Command(service, corrected));
        var rows = await Service().ListAsync("tenant", "company", Account);
        Assert.Equal(2, rows.Count);
        Assert.True(rows.Single(row => row.AssessmentId == first.AssessmentId).IsSuperseded);
        Assert.Equal("Approved", rows.Single(row => row.AssessmentId == first.AssessmentId).Assessment.Outcome);
        Assert.Equal(first.AssessmentId, second.CorrectsAssessmentId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcceptAsync("tenant", "company", "operator", Account, Command(service, corrected)));
    }

    [Fact]
    public async Task CorruptReceipts_FailExplicitlyInsteadOfReturningEmptyHistory()
    {
        var service = Service();
        await service.AcceptAsync("tenant", "company", "operator", Account, Command(service, Import()));
        var receipt = Directory.EnumerateFiles(Path.Combine(_root, "workstation", "option-assessments"), "*.json", SearchOption.AllDirectories).Single();
        await File.WriteAllTextAsync(receipt, "corrupt");
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => service.ListAsync("tenant", "company", Account));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task AccountEndpoints_Return403WithoutStandardAuthentication_WhenScopedGrantOrActualAccountIsMissing(
        bool scopeAllowed, bool accountExists)
    {
        var permissions = UserPermission.ViewOptionAssessments | UserPermission.ManageOptionAssessments;
        var authorization = Substitute.For<IScopedAuthorizationService>();
        authorization.AuthorizeAsync("operator", Arg.Any<UserPermission>(), AccessScopeKindDto.Account, Account,
            permissions, Arg.Any<CancellationToken>()).Returns(call => Task.FromResult(new ScopedAuthorizationDecisionDto(
                scopeAllowed, "operator", call.ArgAt<UserPermission>(1), AccessScopeKindDto.Account, Account, "Synthetic scoped decision")));
        var accounts = Substitute.For<IAccountQueryService>();
        accounts.GetAccountAsync(Account, Arg.Any<CancellationToken>()).Returns(Task.FromResult<AccountSummaryDto?>(accountExists
            ? new(Account, default, null, null, null, null, "synthetic-account", "Synthetic account", "USD", null,
                true, Now, null, null, null, null, null)
            : null));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMutationRateLimiter();
        builder.Services.AddSingleton(authorization);
        builder.Services.AddSingleton(accounts);
        // No standard ASP.NET authentication or assessment/evidence store is registered. A denial
        // must finish before touching those dependencies, using the workstation's custom principal.
        await using var app = builder.Build();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company";
            await next(context);
        });
        app.MapOptionVisualizationEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await app.StartAsync();
        Assert.Null(app.Services.GetService<IAuthenticationService>());
        var client = app.GetTestClient();
        var route = $"/api/options/assessments/accounts/{Account}";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route + "/missing-assessment/source")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route + "/preview", Import())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route + "/accept",
            new AcceptOptionAssessmentImportRequest(Guid.NewGuid(), "unreachable-preview", Import()))).StatusCode);
        Assert.False(Directory.Exists(_root));
        if (!scopeAllowed)
            await accounts.DidNotReceiveWithAnyArgs().GetAccountAsync(default, default);
    }

    private static AcceptOptionAssessmentImportRequest Command(OptionAssessmentImportService service, OptionAssessmentImportRequest request) =>
        new(Guid.NewGuid(), service.Preview(request).PreviewFingerprint, request);
    private static OptionAssessmentImportRequest Import() => new(Assessment(), "synthetic-broker-report.txt",
        Convert.ToBase64String(Encoding.UTF8.GetBytes("Synthetic broker what-if evidence. Not live broker verification.")), "text/plain");
    private static BrokerOptionAssessment Assessment() => new("1", Account, "synthetic-broker", "synthetic-account", Now.AddMinutes(-5),
        "synthetic-assessment", "Approved", "Broker assessment approved at stated time", new("1", "Limit", "Credit", "2", "USD",
        [new("a", "Option", "XYZ-C", "Sell", "Open", "1", "USD", "XYZ", "Call", "2027-01-15", "100", "100", "SMART", "100 XYZ shares"),
         new("b", "Option", "XYZ-P", "Buy", "Open", "2", "USD", "XYZ", "Put", "2027-03-19", "90", "100", "SMART", "100 XYZ shares"),
         new("c", "Equity", "XYZ", "Buy", "Open", "100", "USD")]), InitialMarginAfter: "123.1234567890123456789012345", MarginCurrency: "USD");
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
