using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Domain.Reconciliation;
using Meridian.FinancialOperations.Reconciliation;
using Meridian.Identity.Auth;
using Meridian.Storage.AssetOperations;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    private static readonly DateTimeOffset OversightObservedAt = new(2026, 5, 31, 18, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly OversightDate = new(2026, 5, 31);
    private const UserPermission OversightWritePermissions = UserPermission.ManageLedgerReports | UserPermission.ModifySecurityMaster;
    private static readonly JsonSerializerOptions OversightResponseJsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(UserPermission.ViewLedgerReports, false, false, false, false)]
    [InlineData(UserPermission.ViewLedgerReports | UserPermission.ViewSecurityMaster, false, true, false, false)]
    [InlineData(OversightWritePermissions, true, true, true, false)]
    [InlineData(UserPermission.AdminMaintenance | UserPermission.ModifySecurityMaster, true, true, true, true)]
    public async Task AccountOversight_ContextUsesSessionPermissionsAndRetainedAccountBook(
        UserPermission permissions, bool canWrite, bool canReadPrices, bool canManagePrices, bool canProvision)
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var book = OversightBook(bookId, accountId);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, book).Object);
            services.AddSingleton(OversightOwnership(new("oversight-fund", "tenant-alpha", "company-alpha")).Object);
        }, currentUserPermissions: permissions, currentUserTenantId: "tenant-alpha", currentUserCompanyId: "company-alpha",
            currentUserName: "reviewer-session", mapLedgerApi: true);

        using var response = await app.GetTestClient().GetAsync(OversightBaseRoute(bookId, accountId)
            + "/oversight/context?actor=forged&canWrite=true&canProvision=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var context = await response.Content.ReadFromJsonAsync<AccountOversightContextDto>();
        context.Should().Be(new AccountOversightContextDto(accountId.ToString("D"), bookId.ToString("D"),
            book.BaseCurrency, book.DisplayName, "reviewer-session", canWrite, canReadPrices, canManagePrices, canProvision));
    }

    [Fact]
    public async Task AccountOversight_ClosePriceHistoryUsesExactRouteScopeAndRequiresPriceReadPermission()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var pricing = new Mock<ISecurityMasterPricingService>(MockBehavior.Strict);
        var retained = new SecurityClosePriceSetDto(new SecurityClosePriceSnapshotDto(Guid.NewGuid(),
            accountId.ToString("D"), bookId.ToString("D"), OversightDate, 1, null, null,
            OversightObservedAt, OversightObservedAt, 2m, "preparer", OversightObservedAt, [], []), null);
        pricing.Setup(service => service.GetClosePriceSetsAsync(accountId.ToString("D"), bookId.ToString("D"),
            OversightDate, It.IsAny<CancellationToken>())).ReturnsAsync([retained]);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(pricing.Object);
        }, currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ViewSecurityMaster, mapLedgerApi: true);
        var client = app.GetTestClient();
        var route = OversightBaseRoute(bookId, accountId) + "/close-price-sets/2026-05-31";

        using var response = await client.GetAsync(route + "?accountId=foreign&bookId=foreign");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var results = await response.Content.ReadFromJsonAsync<SecurityClosePriceSetDto[]>();
        results.Should().ContainSingle().Which.Snapshot.SnapshotId.Should().Be(retained.Snapshot.SnapshotId);
        var deniedBooks = new Mock<ILedgerBookService>(MockBehavior.Strict);
        await using var readOnlyApp = await CreateAppAsync(services =>
        {
            services.AddSingleton(deniedBooks.Object);
            services.AddSingleton(pricing.Object);
        }, currentUserPermissions: UserPermission.ViewLedgerReports, mapLedgerApi: true);
        using var denied = await readOnlyApp.GetTestClient().GetAsync(route);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        deniedBooks.VerifyNoOtherCalls();
        pricing.Verify(service => service.GetClosePriceSetsAsync(accountId.ToString("D"), bookId.ToString("D"),
            OversightDate, It.IsAny<CancellationToken>()), Times.Once);
        pricing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AccountOversight_DecimalTextInputAndResponsePreserveEveryDigitWithoutChangingDomainEvidence()
    {
        const string exact = "999999999999999999.123456789";
        const decimal amount = 999999999999999999.123456789m;
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var securityId = Guid.NewGuid();
        var manual = new SecurityClosePriceOverrideDto(amount, SecurityPriceUnit.CurrencyPerUnit, "Exact source quote", "dealer/row-1");
        var retained = new SecurityClosePriceSetDto(new SecurityClosePriceSnapshotDto(Guid.NewGuid(),
            accountId.ToString("D"), bookId.ToString("D"), OversightDate, 1, null, null,
            OversightObservedAt, OversightObservedAt, amount, "preparer-session", OversightObservedAt,
            [new(securityId, null, [new("dealer", amount, OversightObservedAt, SecurityPriceUnit.CurrencyPerUnit)],
                manual, amount, SecurityPriceUnit.CurrencyPerUnit)], []), null);
        var digest = SecurityClosePriceEvidence.Digest(retained.Snapshot);
        var pricing = new Mock<ISecurityMasterPricingService>(MockBehavior.Strict);
        pricing.Setup(service => service.CreateClosePriceSetAsync(It.IsAny<CreateSecurityClosePriceSetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retained);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(pricing.Object);
        }, currentUserPermissions: OversightWritePermissions, currentUserName: "preparer-session", mapLedgerApi: true);

        using var response = await app.GetTestClient().PostAsJsonAsync(
            OversightBaseRoute(bookId, accountId) + "/close-price-sets/2026-05-31", new
            {
                accountId = accountId.ToString("D"),
                bookId = bookId.ToString("D"),
                valuationDate = "2026-05-31",
                evaluatedAsOf = OversightObservedAt,
                maxSourceDispersionPercent = exact,
                createdBy = "forged",
                securities = new[] { new { securityId, @override = new { price = exact, unit = "CurrencyPerUnit",
                    rationale = "Exact source quote", evidenceReference = "dealer/row-1" } } }
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var snapshot = json.RootElement.GetProperty("snapshot");
        snapshot.GetProperty("version").GetInt32().Should().Be(1);
        snapshot.GetProperty("maxSourceDispersionPercent").GetString().Should().Be(exact);
        var line = snapshot.GetProperty("lines")[0];
        line.GetProperty("closePrice").GetString().Should().Be(exact);
        line.GetProperty("observations")[0].GetProperty("price").GetString().Should().Be(exact);
        line.GetProperty("override").GetProperty("price").GetString().Should().Be(exact);
        pricing.Verify(service => service.CreateClosePriceSetAsync(It.Is<CreateSecurityClosePriceSetRequest>(request =>
            request.MaxSourceDispersionPercent == amount && request.Securities[0].Override!.Price == amount
            && request.CreatedBy == "preparer-session"), It.IsAny<CancellationToken>()), Times.Once);
        SecurityClosePriceEvidence.Digest(retained.Snapshot).Should().Be(digest);
    }

    [Theory]
    [InlineData("missing-book")]
    [InlineData("foreign-book")]
    [InlineData("foreign-account")]
    [InlineData("fund-node")]
    [InlineData("empty-account")]
    public async Task AccountOversight_ReadsRejectNonMatchingAccountBookBeforeEvidenceAccess(string mismatch)
    {
        var bookId = Guid.NewGuid();
        var accountId = mismatch == "empty-account" ? Guid.Empty : Guid.NewGuid();
        LedgerBookDto? book = OversightBook(bookId, accountId);
        book = mismatch switch
        {
            "missing-book" => null,
            "foreign-book" => book with { LedgerBookId = Guid.NewGuid() },
            "foreign-account" => book with { FundStructureNodeId = Guid.NewGuid() },
            "fund-node" => book with { FundStructureNodeKind = FundStructureNodeKindDto.Fund },
            _ => book
        };
        var books = OversightBooks(bookId, book);
        var ownership = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        var pricing = new Mock<ISecurityMasterPricingService>(MockBehavior.Strict);
        var forecasts = new Mock<IAccountCashForecastService>(MockBehavior.Strict);
        using var cash = new OversightCashFixture();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(books.Object);
            services.AddSingleton(ownership.Object);
            services.AddSingleton(pricing.Object);
            services.AddSingleton(forecasts.Object);
            services.AddSingleton(cash.Service);
        }, currentUserPermissions: OversightWritePermissions, mapLedgerApi: true);

        foreach (var route in OversightReadRoutes(bookId, accountId))
        {
            using var response = await app.GetTestClient().GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        ownership.VerifyNoOtherCalls();
        pricing.VerifyNoOtherCalls();
        forecasts.VerifyNoOtherCalls();
        Directory.Exists(cash.Root).Should().BeFalse("scope rejection must precede cash-store access");
    }

    [Theory]
    [InlineData("unowned")]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-company")]
    [InlineData("missing-company")]
    public async Task AccountOversight_ReadsRequireRetainedTenantAndCompanyOwnership(string mismatch)
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var books = OversightBooks(bookId, OversightBook(bookId, accountId));
        FundProfileOwnership? owner = mismatch switch
        {
            "unowned" => null,
            "foreign-tenant" => new("oversight-fund", "tenant-other", "company-alpha"),
            "foreign-company" => new("oversight-fund", "tenant-alpha", "company-other"),
            _ => new("oversight-fund", "tenant-alpha", null)
        };
        var ownership = OversightOwnership(owner);
        var pricing = new Mock<ISecurityMasterPricingService>(MockBehavior.Strict);
        var forecasts = new Mock<IAccountCashForecastService>(MockBehavior.Strict);
        using var cash = new OversightCashFixture();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(books.Object);
            services.AddSingleton(ownership.Object);
            services.AddSingleton(pricing.Object);
            services.AddSingleton(forecasts.Object);
            services.AddSingleton(cash.Service);
        }, currentUserPermissions: OversightWritePermissions, currentUserTenantId: "tenant-alpha",
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);

        foreach (var route in OversightReadRoutes(bookId, accountId))
        {
            using var response = await app.GetTestClient().GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        ownership.Verify(service => service.ResolveAsync("oversight-fund", It.IsAny<CancellationToken>()), Times.Exactly(5));
        ownership.VerifyNoOtherCalls();
        pricing.VerifyNoOtherCalls();
        forecasts.VerifyNoOtherCalls();
        Directory.Exists(cash.Root).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, null, "ops-user", HttpStatusCode.Forbidden)]
    [InlineData(null, "tenant-alpha", "ops-user", HttpStatusCode.Forbidden)]
    [InlineData("company-alpha", "tenant-alpha", "", HttpStatusCode.Unauthorized)]
    public async Task AccountOversight_MissingSessionScopeDeniesBeforeBookLookup(
        string? companyId, string? tenantId, string actor, HttpStatusCode expectedStatus)
    {
        var books = new Mock<ILedgerBookService>(MockBehavior.Strict);
        var forecasts = new Mock<IAccountCashForecastService>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(books.Object);
            services.AddSingleton(forecasts.Object);
        }, currentUserPermissions: UserPermission.ViewLedgerReports, currentUserCompanyId: companyId,
            currentUserTenantId: tenantId, currentUserName: actor, mapLedgerApi: true);

        using var response = await app.GetTestClient().GetAsync(OversightBaseRoute(Guid.NewGuid(), Guid.NewGuid()) + "/cash-forecasts");

        response.StatusCode.Should().Be(expectedStatus);
        books.VerifyNoOtherCalls();
        forecasts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AccountOversight_ReadOnlyLedgerPermissionCannotMutateAnyEvidenceLane()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var books = new Mock<ILedgerBookService>(MockBehavior.Strict);
        var pricing = new Mock<ISecurityMasterPricingService>(MockBehavior.Strict);
        var forecasts = new Mock<IAccountCashForecastService>(MockBehavior.Strict);
        using var cash = new OversightCashFixture();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(books.Object);
            services.AddSingleton(pricing.Object);
            services.AddSingleton(forecasts.Object);
            services.AddSingleton(cash.Service);
        }, currentUserPermissions: UserPermission.ViewLedgerReports | UserPermission.ModifySecurityMaster, mapLedgerApi: true);
        var route = OversightBaseRoute(bookId, accountId);
        var scope = new AccountCashReconciliationScope("tenant-test", "tenant-test", accountId.ToString("D"), bookId);
        var snapshotId = Guid.NewGuid();
        var requests = new (string Route, object Body)[]
        {
            (route + "/cash-reconciliation/runs", OversightCashRequest(scope)),
            (route + "/cash-reconciliation/cases/case-one/assignments", new AssignAccountCashCaseRequest(1, "owner", "Reviewed")),
            (route + "/close-price-sets/2026-05-31", OversightCreatePriceRequest(bookId, accountId)),
            ($"{route}/close-price-sets/2026-05-31/{snapshotId:D}/approval", new ApproveSecurityClosePriceSetRequest(
                accountId.ToString("D"), bookId.ToString("D"), OversightDate, snapshotId, "forged-reviewer", "Reviewed")),
            (route + "/cash-forecasts", OversightForecastRequest(bookId, accountId))
        };

        foreach (var request in requests)
        {
            using var response = await app.GetTestClient().PostAsJsonAsync(request.Route, request.Body);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        books.VerifyNoOtherCalls();
        pricing.VerifyNoOtherCalls();
        forecasts.VerifyNoOtherCalls();
        Directory.Exists(cash.Root).Should().BeFalse();
    }

    [Fact]
    public async Task AccountOversight_ProvisionRequiresAdminBeforeBookLookup()
    {
        var books = new Mock<ILedgerBookService>(MockBehavior.Strict);
        using var cash = new OversightCashFixture();
        var forecastStore = new FileAccountCashForecastStore(cash.Root);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(books.Object);
            services.AddSingleton(cash.Store);
            services.AddSingleton<IAccountCashForecastStore>(forecastStore);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, mapLedgerApi: true);

        using var response = await app.GetTestClient().PostAsync(
            OversightBaseRoute(Guid.NewGuid(), Guid.NewGuid()) + "/oversight/provision", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        books.VerifyNoOtherCalls();
        Directory.Exists(cash.Root).Should().BeFalse();
    }

    [Fact]
    public async Task AccountOversight_AdminProvisionRequiresExactAccountAndInitializesBothStores()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        using var cash = new OversightCashFixture();
        var forecastStore = new FileAccountCashForecastStore(cash.Root);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(cash.Store);
            services.AddSingleton<IAccountCashForecastStore>(forecastStore);
        }, currentUserPermissions: UserPermission.AdminMaintenance, mapLedgerApi: true);

        using var foreign = await app.GetTestClient().PostAsync(
            OversightBaseRoute(bookId, Guid.NewGuid()) + "/oversight/provision", null);
        foreign.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Directory.Exists(cash.Root).Should().BeFalse();
        using var provisioned = await app.GetTestClient().PostAsync(OversightBaseRoute(bookId, accountId) + "/oversight/provision", null);

        provisioned.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cash.Service.GetCasesAsync(new("tenant-test", "tenant-test", accountId.ToString("D"), bookId))).Should().BeEmpty();
        (await forecastStore.GetHistoryAsync(new("tenant-test", "tenant-test", accountId.ToString("D"), bookId.ToString("D"), "USD"))).Should().BeEmpty();
        File.Exists(Path.Combine(cash.Root, "reconciliation", "account-cash-continuity", "initialized")).Should().BeTrue();
        File.Exists(Path.Combine(cash.Root, "asset-operations", "account-cash-forecasts", "initialized")).Should().BeTrue();
    }

    [Theory]
    [InlineData("cash", false, HttpStatusCode.ServiceUnavailable)]
    [InlineData("cash", true, HttpStatusCode.ServiceUnavailable)]
    [InlineData("forecast", false, HttpStatusCode.ServiceUnavailable)]
    [InlineData("forecast", true, HttpStatusCode.ServiceUnavailable)]
    public async Task AccountOversight_ProvisionNeverResetsMissingOrCorruptInitializedState(
        string storeKind, bool missing, HttpStatusCode expectedStatus)
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        using var cash = new OversightCashFixture();
        var forecastStore = new FileAccountCashForecastStore(cash.Root);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(cash.Store);
            services.AddSingleton<IAccountCashForecastStore>(forecastStore);
        }, currentUserPermissions: UserPermission.AdminMaintenance, mapLedgerApi: true);
        var route = OversightBaseRoute(bookId, accountId) + "/oversight/provision";
        using var initial = await app.GetTestClient().PostAsync(route, null);
        initial.StatusCode.Should().Be(HttpStatusCode.OK);
        var cashDirectory = Path.Combine(cash.Root, "reconciliation", "account-cash-continuity");
        var forecastDirectory = Path.Combine(cash.Root, "asset-operations", "account-cash-forecasts");
        // Provisioning registers no scopes yet, so the scope index is the only retained state file.
        var invalidStatePath = Path.Combine(storeKind == "cash" ? cashDirectory : forecastDirectory, "scope-index.json");
        var unaffectedStatePath = Path.Combine(storeKind == "cash" ? forecastDirectory : cashDirectory, "scope-index.json");
        var retainedUnaffectedState = await File.ReadAllBytesAsync(unaffectedStatePath);
        var markerPath = Path.Combine(storeKind == "cash" ? cashDirectory : forecastDirectory, "initialized");
        var retainedMarker = await File.ReadAllBytesAsync(markerPath);
        if (missing)
            File.Delete(invalidStatePath);
        else
            await File.WriteAllTextAsync(invalidStatePath, "{}");

        using var retry = await app.GetTestClient().PostAsync(route, null);

        retry.StatusCode.Should().Be(expectedStatus);
        if (missing)
            File.Exists(invalidStatePath).Should().BeFalse("provisioning must not replace missing initialized history");
        else
            (await File.ReadAllTextAsync(invalidStatePath)).Should().Be("{}", "provisioning must not reset invalid retained bytes");
        (await File.ReadAllBytesAsync(markerPath)).Should().Equal(retainedMarker);
        (await File.ReadAllBytesAsync(unaffectedStatePath)).Should().Equal(retainedUnaffectedState);
    }

    [Fact]
    public async Task AccountOversight_FutureForecastRecordingTimeRejectsBeforeEvidenceOrForecastCalls()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var forecasts = new Mock<IAccountCashForecastService>(MockBehavior.Strict);
        var evidence = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(forecasts.Object);
            services.AddSingleton(evidence.Object);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, mapLedgerApi: true);
        var request = OversightForecastRequest(bookId, accountId) with { RecordedAtUtc = DateTimeOffset.UtcNow.AddDays(1) };

        using var response = await app.GetTestClient().PostAsJsonAsync(
            OversightBaseRoute(bookId, accountId) + "/cash-forecasts", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        forecasts.VerifyNoOtherCalls();
        evidence.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("flow")]
    [InlineData("fx")]
    public async Task AccountOversight_NullForecastEntriesRejectBeforeEvidenceOrForecastCalls(string collection)
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var forecasts = new Mock<IAccountCashForecastService>(MockBehavior.Strict);
        var evidence = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(forecasts.Object);
            services.AddSingleton(evidence.Object);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, mapLedgerApi: true);
        var request = OversightForecastRequest(bookId, accountId);
        request = collection == "flow" ? request with { Flows = [null!] }
            : request with { FxConversions = [null!] };

        using var response = await app.GetTestClient().PostAsJsonAsync(
            OversightBaseRoute(bookId, accountId) + "/cash-forecasts", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("cannot contain null entries");
        forecasts.VerifyNoOtherCalls();
        evidence.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("book")]
    [InlineData("date")]
    public async Task AccountOversight_ClosePricePayloadCannotChangeRouteScope(string mismatch)
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var pricing = new Mock<ISecurityMasterPricingService>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(pricing.Object);
        }, currentUserPermissions: OversightWritePermissions, mapLedgerApi: true);
        var create = OversightCreatePriceRequest(bookId, accountId);
        create = mismatch switch
        {
            "account" => create with { AccountId = Guid.NewGuid().ToString("D") },
            "book" => create with { BookId = Guid.NewGuid().ToString("D") },
            _ => create with { ValuationDate = OversightDate.AddDays(1) }
        };
        var snapshotId = Guid.NewGuid();
        var approval = new ApproveSecurityClosePriceSetRequest(create.AccountId, create.BookId,
            create.ValuationDate, snapshotId, "forged-reviewer", "Reviewed");
        var route = OversightBaseRoute(bookId, accountId) + "/close-price-sets/2026-05-31";

        using var createResponse = await app.GetTestClient().PostAsJsonAsync(route, create);
        using var approvalResponse = await app.GetTestClient().PostAsJsonAsync($"{route}/{snapshotId:D}/approval", approval);

        createResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        approvalResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        pricing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AccountOversight_ClosePriceApprovalCannotReplaceRouteSnapshot()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var pricing = new Mock<ISecurityMasterPricingService>(MockBehavior.Strict);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(pricing.Object);
        }, currentUserPermissions: OversightWritePermissions, mapLedgerApi: true);
        var route = OversightBaseRoute(bookId, accountId) + $"/close-price-sets/2026-05-31/{Guid.NewGuid():D}/approval";
        var request = new ApproveSecurityClosePriceSetRequest(accountId.ToString("D"), bookId.ToString("D"),
            OversightDate, Guid.NewGuid(), "forged-reviewer", "Reviewed");

        using var response = await app.GetTestClient().PostAsJsonAsync(route, request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        pricing.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("account")]
    [InlineData("book")]
    public async Task AccountOversight_CashPayloadCannotChangeRouteAccountBook(string mismatch)
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        using var cash = new OversightCashFixture();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(cash.Service);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, mapLedgerApi: true);
        var scope = new AccountCashReconciliationScope("tenant-test", "tenant-test", accountId.ToString("D"), bookId);
        scope = mismatch == "account" ? scope with { AccountId = Guid.NewGuid().ToString("D") }
            : scope with { LedgerBookId = Guid.NewGuid() };

        using var response = await app.GetTestClient().PostAsJsonAsync(
            OversightBaseRoute(bookId, accountId) + "/cash-reconciliation/runs", OversightCashRequest(scope));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Directory.Exists(cash.Root).Should().BeFalse();
    }

    [Fact]
    public async Task AccountOversight_ClosePriceActorsComeFromAuthenticatedSession()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var pricing = new Mock<ISecurityMasterPricingService>(MockBehavior.Strict);
        var retained = new SecurityClosePriceSetDto(new SecurityClosePriceSnapshotDto(snapshotId,
            accountId.ToString("D"), bookId.ToString("D"), OversightDate, 1, null, null,
            OversightObservedAt, OversightObservedAt, 2m, "preparer-session", OversightObservedAt, [], []), null);
        pricing.Setup(service => service.CreateClosePriceSetAsync(It.IsAny<CreateSecurityClosePriceSetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retained);
        pricing.Setup(service => service.ApproveClosePriceSetAsync(It.IsAny<ApproveSecurityClosePriceSetRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(retained);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(pricing.Object);
        }, currentUserPermissions: OversightWritePermissions, currentUserName: "preparer-session", mapLedgerApi: true);
        var client = app.GetTestClient();
        var route = OversightBaseRoute(bookId, accountId) + "/close-price-sets/2026-05-31";

        using var created = await client.PostAsJsonAsync(route, OversightCreatePriceRequest(bookId, accountId));
        client.DefaultRequestHeaders.Add("X-Meridian-Test-User", "reviewer-session");
        using var approved = await client.PostAsJsonAsync($"{route}/{snapshotId:D}/approval",
            new ApproveSecurityClosePriceSetRequest(accountId.ToString("D"), bookId.ToString("D"), OversightDate,
                snapshotId, "forged-reviewer", "Independent review"));

        created.StatusCode.Should().Be(HttpStatusCode.OK);
        approved.StatusCode.Should().Be(HttpStatusCode.OK);
        pricing.Verify(service => service.CreateClosePriceSetAsync(It.Is<CreateSecurityClosePriceSetRequest>(request =>
            request.CreatedBy == "preparer-session" && request.AccountId == accountId.ToString("D") && request.BookId == bookId.ToString("D")),
            It.IsAny<CancellationToken>()), Times.Once);
        pricing.Verify(service => service.ApproveClosePriceSetAsync(It.Is<ApproveSecurityClosePriceSetRequest>(request =>
            request.Reviewer == "reviewer-session" && request.SnapshotId == snapshotId), It.IsAny<CancellationToken>()), Times.Once);
        pricing.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AccountOversight_ForecastHistoryUsesServerTenantCompanyAndBookCurrency()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var expectedScope = new AccountCashForecastScopeDto("tenant-alpha", "company-alpha", accountId.ToString("D"), bookId.ToString("D"), "EUR");
        var forecasts = new Mock<IAccountCashForecastService>(MockBehavior.Strict);
        forecasts.Setup(service => service.GetHistoryAsync(expectedScope, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AccountCashForecastSnapshotDto>());
        forecasts.Setup(service => service.GetAsync(expectedScope, "retained-snapshot", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AccountCashForecastSnapshotDto?)null);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId) with { BaseCurrency = "EUR" }).Object);
            services.AddSingleton(OversightOwnership(new("oversight-fund", "tenant-alpha", "company-alpha")).Object);
            services.AddSingleton(forecasts.Object);
        }, currentUserPermissions: UserPermission.ViewLedgerReports, currentUserTenantId: "tenant-alpha",
            currentUserCompanyId: "company-alpha", mapLedgerApi: true);
        var route = OversightBaseRoute(bookId, accountId) + "/cash-forecasts";
        const string forgedQuery = "?tenantId=forged&companyId=forged&baseCurrency=USD";

        using var listed = await app.GetTestClient().GetAsync(route + forgedQuery);
        using var retrieved = await app.GetTestClient().GetAsync(route + "/retained-snapshot" + forgedQuery);

        listed.StatusCode.Should().Be(HttpStatusCode.OK);
        retrieved.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forecasts.Verify(service => service.GetHistoryAsync(expectedScope, It.IsAny<CancellationToken>()), Times.Once);
        forecasts.Verify(service => service.GetAsync(expectedScope, "retained-snapshot", It.IsAny<CancellationToken>()), Times.Once);
        forecasts.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task AccountOversight_CashRunAndAssignmentRetainServerScopeAndActor()
    {
        var bookId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var scope = new AccountCashReconciliationScope("tenant-alpha", "company-alpha", accountId.ToString("D"), bookId);
        using var cash = new OversightCashFixture();
        await cash.Store.InitializeAsync();
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton(OversightBooks(bookId, OversightBook(bookId, accountId)).Object);
            services.AddSingleton(OversightOwnership(new("oversight-fund", "tenant-alpha", "company-alpha")).Object);
            services.AddSingleton(cash.Service);
        }, currentUserPermissions: UserPermission.ManageLedgerReports, currentUserTenantId: "tenant-alpha",
            currentUserCompanyId: "company-alpha", currentUserName: "authenticated-assigner", mapLedgerApi: true);
        var request = OversightCashRequest(scope with { TenantId = "forged-tenant", CompanyId = "forged-company" });
        var route = OversightBaseRoute(bookId, accountId) + "/cash-reconciliation";

        // Match the host's Web request options, including numeric unannotated source enums.
        using var runResponse = await app.GetTestClient().PostAsJsonAsync(route + "/runs", request);
        runResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var retainedRun = await runResponse.Content.ReadFromJsonAsync<AccountCashReconciliationRun>(OversightResponseJsonOptions);
        retainedRun!.Input.Scope.Should().Be(scope);
        var retainedCase = (await cash.Service.GetCasesAsync(scope)).Should().ContainSingle().Subject;
        using var assignmentResponse = await app.GetTestClient().PostAsJsonAsync($"{route}/cases/{retainedCase.CaseId}/assignments",
            new AssignAccountCashCaseRequest(retainedCase.Version, "new-owner", "Investigated source discrepancy"));

        assignmentResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var assigned = await assignmentResponse.Content.ReadFromJsonAsync<AccountCashReconciliationCase>(OversightResponseJsonOptions);
        assigned!.Scope.Should().Be(scope);
        assigned.Owner.Should().Be("new-owner");
        assigned.Assignments.Should().ContainSingle().Which.Actor.Should().Be("authenticated-assigner");
        (await cash.Service.GetCasesAsync(request.Scope)).Should().BeEmpty();
    }

    private static LedgerBookDto OversightBook(Guid bookId, Guid accountId)
        => new(bookId, "oversight-fund", accountId, FundStructureNodeKindDto.Account, "Account book", "USD", OversightObservedAt, OversightObservedAt);

    private static Mock<ILedgerBookService> OversightBooks(Guid bookId, LedgerBookDto? book)
    {
        var service = new Mock<ILedgerBookService>(MockBehavior.Strict);
        service.Setup(item => item.GetBookAsync(bookId, It.IsAny<CancellationToken>())).ReturnsAsync(book);
        return service;
    }

    private static Mock<IFundProfileTenancyRegistry> OversightOwnership(FundProfileOwnership? owner)
    {
        var service = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        service.Setup(item => item.ResolveAsync("oversight-fund", It.IsAny<CancellationToken>())).ReturnsAsync(owner);
        return service;
    }

    private static string OversightBaseRoute(Guid bookId, Guid accountId) => $"/api/ledger/books/{bookId:D}/accounts/{accountId:D}";

    private static string[] OversightReadRoutes(Guid bookId, Guid accountId)
        => [OversightBaseRoute(bookId, accountId) + "/cash-reconciliation/cases",
            OversightBaseRoute(bookId, accountId) + "/oversight/context",
            OversightBaseRoute(bookId, accountId) + "/close-price-sets/2026-05-31",
            OversightBaseRoute(bookId, accountId) + $"/close-price-sets/2026-05-31/{Guid.NewGuid():D}",
            OversightBaseRoute(bookId, accountId) + "/cash-forecasts"];

    private static CreateSecurityClosePriceSetRequest OversightCreatePriceRequest(Guid bookId, Guid accountId)
        => new(accountId.ToString("D"), bookId.ToString("D"), OversightDate, OversightObservedAt,
            [new(Guid.NewGuid())], 2m, "forged-preparer");

    private static AccountCashForecastRequestDto OversightForecastRequest(Guid bookId, Guid accountId)
        => new("forecast-one", new("tenant-test", "tenant-test", accountId.ToString("D"), bookId.ToString("D"), "USD"),
            OversightDate, OversightDate, null, null, [], [], OversightObservedAt);

    private static AccountCashReconciliationRequest OversightCashRequest(AccountCashReconciliationScope scope)
    {
        AccountCashBalanceSource Source(string sourceId, ReconciliationSourceType sourceType, decimal amount)
            => new(sourceId, sourceType, "source-v1", "retained:" + sourceId, OversightObservedAt.AddHours(-1), true,
                [new(new NormalizedCashEntry(sourceId + ":USD:balance", scope.AccountId, amount, "USD", amount, "USD",
                    OversightObservedAt.AddHours(-2), OversightDate, sourceId + ":row-one", "settled-balance", "balance-check", "Balance observation"),
                    "{\"retainedBalance\":" + amount.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}")]);
        return new("run-one", scope, OversightDate, OversightObservedAt,
            Source("accounting-ledger", ReconciliationSourceType.InternalLedger, 1000m),
            Source("custodian", ReconciliationSourceType.Custodian, 990m), StatementToleranceProfile.Default, "initial-owner");
    }

    private sealed class OversightCashFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "meridian-tests", "account-oversight", Guid.NewGuid().ToString("N"));
        public FileAccountCashReconciliationStore Store { get; }
        public AccountCashReconciliationService Service { get; }

        public OversightCashFixture()
        {
            Store = new(Root);
            Service = new(Store);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
