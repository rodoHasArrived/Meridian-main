using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Instruments.AssetOperations;
using Meridian.Storage.AssetOperations;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net;
using System.Net.Http.Json;

namespace Meridian.Tests.Ui;

public sealed class AccountCashForecastEvidenceAdapterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "forecast-source", Guid.NewGuid().ToString("N"));
    private readonly AccountCashForecastScopeDto _scope = new("tenant-source", "company-source", Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), "USD");
    private readonly DateOnly _date = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task IndependentlyReviewedRetainedBalanceFlowAndDatedFx_CertifyAndSurviveRestart()
    {
        var request = await ValidRequestAsync();
        await Adapter().VerifyAsync(request);
        var store = new FileAccountCashForecastStore(_root);
        await store.InitializeAsync();
        var result = await new AccountCashForecastService(store).CaptureAsync(request);

        result.IsCertified.Should().BeTrue();
        result.ReconciledClosingBaseCash.Should().Be(115m);
        result.ClosingVariance.Should().Be(0m);
        var retained = await new AccountCashForecastService(new FileAccountCashForecastStore(_root)).GetAsync(_scope, result.SnapshotId);
        retained.Should().BeEquivalentTo(result);
        await new AccountCashForecastEvidenceAdapter(Store()).VerifyAsync(retained!.Inputs);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("settlement-date")]
    [InlineData("account")]
    [InlineData("book")]
    [InlineData("fx-rate")]
    [InlineData("evidence-version")]
    [InlineData("reviewer")]
    public async Task CallerSubstitutionCannotSupplySourceAuthority(string alteration)
    {
        var request = await ValidRequestAsync();
        var flow = request.Flows[0];
        request = alteration switch
        {
            "amount" => request with { Flows = [flow with { Amount = 11m }] },
            "settlement-date" => request with { Flows = [flow with { SettlementDate = _date.AddDays(-1) }] },
            "account" => request with { Scope = _scope with { AccountId = Guid.NewGuid().ToString("D") } },
            "book" => request with { Scope = _scope with { BookId = Guid.NewGuid().ToString("D") } },
            "fx-rate" => request with { FxConversions = [request.FxConversions[0] with { BaseUnitsPerCurrencyUnit = 2m }] },
            "evidence-version" => request with
            {
                Flows = [flow with { Provenance = flow.Provenance with
                { Evidence = flow.Provenance.Evidence with { EvidenceVersion = 99 } } }]
            },
            "reviewer" => request with
            {
                Flows = [flow with { Provenance = flow.Provenance with
                { Evidence = flow.Provenance.Evidence with { ReviewedBy = "claimed-reviewer" } } }]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(alteration))
        };

        var action = () => Adapter().VerifyAsync(request);
        await action.Should().ThrowAsync<AccountCashForecastSourceAuthorityException>();
    }

    [Theory]
    [InlineData("unreviewed")]
    [InlineData("same-maker")]
    [InlineData("wrong-confirmed-hash")]
    [InlineData("unsupported-json")]
    [InlineData("duplicate-json")]
    public async Task UnsupportedOrUnapprovedSourceCannotCertify(string defect)
    {
        var source = BalanceSource("opening", 100m);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source, Json);
        if (defect == "unsupported-json")
            bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"unknownSourceField\":1", StringComparison.Ordinal));
        if (defect == "duplicate-json")
            bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"amount\":100", "\"amount\":100,\"amount\":100", StringComparison.Ordinal));
        var provenance = await RetainAsync(source, bytes,
            review: defect != "unreviewed", reviewer: defect == "same-maker" ? "SOURCE-MAKER" : "source-reviewer",
            confirmedHash: defect == "wrong-confirmed-hash" ? new string('a', 64) : null);
        var request = Request(new(_scope.AccountId, _scope.BookId, _date, 100m, "USD", true, provenance), null, [], []);

        var action = () => Adapter().VerifyAsync(request);
        await action.Should().ThrowAsync<AccountCashForecastSourceAuthorityException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrChangedRetainedBytesCannotCertify(bool delete)
    {
        var request = await ValidRequestAsync();
        var evidence = request.OpeningCash!.Provenance.Evidence;
        EvidenceVaultReference.TryParseCanonical(evidence.EvidenceUri, out var vaultId).Should().BeTrue();
        var identity = await Store().TryGetVaultIdentityAsync(vaultId, _scope.TenantId, _scope.CompanyId);
        var path = Path.Combine(_root, identity!.Artifacts.Single().RelativePath);
        if (delete)
            File.Delete(path);
        else
            await File.WriteAllTextAsync(path, "{}");

        var action = () => Adapter().VerifyAsync(request);
        await action.Should().ThrowAsync<AccountCashForecastSourceAuthorityException>();
    }

    internal async Task<AccountCashForecastRequestDto> ValidRequestAsync()
    {
        var opening = BalanceSource("opening", 100m);
        var closing = BalanceSource("closing", 115m);
        var flow = new AccountCashSourceFlow("coupon-1", "coupon-1.actual.v1", AccountCashFlowStatus.Actual,
            "Coupon", 10m, "EUR", _date, _date, _date);
        var flowSource = new AccountCashForecastSourceDocument(1, "flow", "bank", "coupon-1.actual.v1", 1,
            _scope.AccountId, _scope.BookId, _date, AccountCashEvidenceRole.Observation, Flow: flow);
        var fx = new AccountCashSourceFx("EUR", "USD", _date, 1.5m);
        var fxSource = new AccountCashForecastSourceDocument(1, "fx", "declared-fx", "EURUSD.v1", 1,
            _scope.AccountId, _scope.BookId, _date, AccountCashEvidenceRole.DeclaredFx, Fx: fx);
        return Request(
            new(_scope.AccountId, _scope.BookId, _date, 100m, "USD", true, await RetainAsync(opening)),
            new(_scope.AccountId, _scope.BookId, _date, 115m, "USD", true, await RetainAsync(closing)),
            [new(flow.FlowId, flow.VersionId, _scope.AccountId, _scope.BookId, flow.Status, flow.FlowType,
                flow.Amount, flow.Currency, flow.EconomicDate, flow.SettlementDate, flow.CashReceiptDate, await RetainAsync(flowSource))],
            [new(fx.Currency, fx.BaseCurrency, fx.ConversionDate, fx.BaseUnitsPerCurrencyUnit, await RetainAsync(fxSource))]);
    }

    private AccountCashForecastRequestDto Request(AccountSettledCashObservationDto opening, AccountSettledCashObservationDto? closing,
        IReadOnlyList<AccountCashFlowDto> flows, IReadOnlyList<AccountCashFxConversionDto> fx) =>
        new("source-backed-run", _scope, _date, _date, opening, closing, flows, fx, DateTimeOffset.UtcNow.AddMinutes(1));

    private AccountCashForecastSourceDocument BalanceSource(string id, decimal amount) =>
        new(1, "balance", "bank", id, 1, _scope.AccountId, _scope.BookId, _date,
            AccountCashEvidenceRole.Observation, Balance: new(_date, amount, "USD", true));

    private async Task<AccountCashProvenanceDto> RetainAsync(AccountCashForecastSourceDocument source, byte[]? bytes = null,
        bool review = true, string reviewer = "source-reviewer", string? confirmedHash = null)
    {
        bytes ??= JsonSerializer.SerializeToUtf8Bytes(source, Json);
        var store = Store();
        var retained = await store.WriteIntakeArtifactAsync(new("account", _scope.AccountId, "ManualUpload",
            source.SourceEntityId + ".json", Convert.ToBase64String(bytes), "application/json",
            source.SourceDomain, source.SourceEntityId, "source-maker")
        {
            Actor = "source-maker",
            TenantId = _scope.TenantId,
            Scope = _scope.CompanyId
        });
        var document = retained.Document!;
        var reviewedAt = DateTimeOffset.UtcNow;
        if (review)
        {
            var decision = await store.ReviewDocumentAsync(retained.VaultIdentity.VaultId, document.DocumentId,
                _scope.TenantId, _scope.CompanyId,
                new(EvidenceDocumentReviewStatusDto.Accepted, reviewer)
                {
                    ConfirmedFields = [new("sourceContentHashSha256", confirmedHash ?? Sha256Digest.Compute(bytes), reviewer, reviewedAt)]
                });
            document = decision!.Entry.Document;
            reviewedAt = document.ReviewerState.ReviewedAt!.Value;
        }
        return new(source.SourceDomain, source.SourceEntityId, Sha256Digest.Compute(bytes), source.Role,
            new(document.DocumentId, EvidenceVaultReference.CanonicalPrefix + retained.VaultIdentity.VaultId,
                Sha256Digest.Compute(bytes), source.SourceDomain, source.SourceEntityId, "Accepted", reviewer,
                reviewedAt, source.EffectiveDate, source.EvidenceVersion, reviewedAt, reviewer, "account", _scope.AccountId));
    }

    internal string Root => _root;
    internal FileEvidenceArtifactStore Store() => new(_root, NullLogger<FileEvidenceArtifactStore>.Instance);
    private AccountCashForecastEvidenceAdapter Adapter() => new(Store());
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
}

public sealed partial class WorkstationEndpointsTests
{
    [Fact]
    public async Task AccountOversight_ForecastCaptureCertifiesExactReviewedBytesAndRefusesSubstitutedEconomics()
    {
        using var source = new AccountCashForecastEvidenceAdapterTests();
        var request = await source.ValidRequestAsync();
        request = request with { RecordedAtUtc = DateTimeOffset.UtcNow };
        var bookId = Guid.Parse(request.Scope.BookId);
        var accountId = Guid.Parse(request.Scope.AccountId);
        var store = new FileAccountCashForecastStore(source.Root);
        await store.InitializeAsync();
        var service = new AccountCashForecastService(store);
        var books = new Mock<ILedgerBookService>(MockBehavior.Strict);
        books.Setup(item => item.GetBookAsync(bookId, It.IsAny<CancellationToken>())).ReturnsAsync(new LedgerBookDto(
            bookId, "source-fund", accountId, FundStructureNodeKindDto.Account, "Reviewed source account", "USD",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(books.Object);
            services.AddSingleton<IEvidenceArtifactStore>(source.Store());
            services.AddSingleton<IAccountCashForecastService>(service);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserTenantId: request.Scope.TenantId,
            currentUserCompanyId: request.Scope.CompanyId, mapLedgerApi: true);
        var route = $"/api/ledger/books/{bookId:D}/accounts/{accountId:D}/cash-forecasts";

        using var captured = await app.GetTestClient().PostAsJsonAsync(route, request);
        captured.StatusCode.Should().Be(HttpStatusCode.OK, await captured.Content.ReadAsStringAsync());
        var snapshot = await captured.Content.ReadFromJsonAsync<AccountCashForecastSnapshotDto>(ServerJsonOptions);
        snapshot!.IsCertified.Should().BeTrue();
        var changed = request with { RunId = "substituted-run", Flows = [request.Flows[0] with { Amount = 1000m }] };
        using var rejected = await app.GetTestClient().PostAsJsonAsync(route, changed);
        rejected.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("CASH_FORECAST_SOURCE_AUTHORITY_REQUIRED");
        (await service.GetHistoryAsync(request.Scope)).Should().ContainSingle().Which.SnapshotId.Should().Be(snapshot.SnapshotId);
    }
}
