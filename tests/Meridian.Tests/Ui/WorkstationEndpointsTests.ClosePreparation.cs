using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [Theory]
    [InlineData("capture")]
    [InlineData("preview")]
    [InlineData("create")]
    public async Task ClosePreparation_ReadOnlyCallerCannotMutateTemplatesOrPlans(string operation)
    {
        var fixture = new ClosePreparationEndpointFixture();
        await using var app = await CreateAppAsync(fixture.Register,
            mapLedgerApi: true, currentUserPermissions: UserPermission.ViewLedgerReports);

        using var response = await fixture.PostAsync(app.GetTestClient(), operation);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Preparation.VerifyNoOtherCalls();
        fixture.Close.VerifyNoOtherCalls();
        fixture.Books.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("capture", null, null)]
    [InlineData("preview", null, null)]
    [InlineData("create", null, null)]
    [InlineData("capture", "tenant-test", null)]
    [InlineData("preview", "tenant-test", null)]
    [InlineData("create", "tenant-test", null)]
    public async Task ClosePreparation_RequiresTrustedTenantAndCompany(
        string operation, string? tenantId, string? companyId)
    {
        var fixture = new ClosePreparationEndpointFixture();
        await using var app = await CreateAppAsync(fixture.Register,
            mapLedgerApi: true, currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserTenantId: tenantId, currentUserCompanyId: companyId);

        using var response = await fixture.PostAsync(app.GetTestClient(), operation);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Preparation.VerifyNoOtherCalls();
        fixture.Close.VerifyNoOtherCalls();
        fixture.Books.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("preview")]
    [InlineData("create")]
    public async Task ClosePreparation_MissingAuthenticatedActorCannotUseClientActor(string operation)
    {
        var fixture = new ClosePreparationEndpointFixture();
        await using var app = await CreateAppAsync(fixture.Register,
            mapLedgerApi: true, currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserName: "");

        using var response = await fixture.PostAsync(app.GetTestClient(), operation);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Preparation.VerifyNoOtherCalls();
        fixture.Close.VerifyNoOtherCalls();
        fixture.Books.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ClosePreparation_AllWritesUseAuthenticatedActorAndScope()
    {
        var fixture = new ClosePreparationEndpointFixture();
        fixture.AllowPreparation();
        await using var app = await CreateAppAsync(fixture.Register,
            mapLedgerApi: true, currentUserPermissions: UserPermission.ManageLedgerReports,
            currentUserName: "authenticated-accountant");
        var client = app.GetTestClient();

        foreach (var operation in new[] { "capture", "preview", "create" })
        {
            using var response = await fixture.PostAsync(client, operation);
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        fixture.Preparation.Verify(service => service.CaptureTemplateAsync(
            It.Is<CaptureClosePlanTemplateRequestDto>(request => request.Actor == "authenticated-accountant"),
            "authenticated-accountant", "tenant-test", "tenant-test", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Preparation.Verify(service => service.PreviewAsync(
            It.Is<PreviewClosePreparationRequestDto>(request => request.Actor == "authenticated-accountant"),
            "authenticated-accountant", "tenant-test", "tenant-test", It.IsAny<CancellationToken>()), Times.Once);
        fixture.Preparation.Verify(service => service.CreateAsync(
            It.Is<CreatePreparedClosePlanRequestDto>(request => request.Actor == "authenticated-accountant"),
            "authenticated-accountant", "tenant-test", "tenant-test", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("preview")]
    [InlineData("create")]
    public async Task ClosePreparation_ForeignTargetBookIsDeniedBeforePreparation(string operation)
    {
        var fixture = new ClosePreparationEndpointFixture(foreignTarget: true);
        await using var app = await CreateAppAsync(fixture.Register,
            mapLedgerApi: true, currentUserPermissions: UserPermission.ManageLedgerReports);

        using var response = await fixture.PostAsync(app.GetTestClient(), operation);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Preparation.Verify(service => service.PreviewAsync(
            It.IsAny<PreviewClosePreparationRequestDto>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Preparation.Verify(service => service.CreateAsync(
            It.IsAny<CreatePreparedClosePlanRequestDto>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ClosePreparation_StalePreviewIsReportedAsConflict()
    {
        var fixture = new ClosePreparationEndpointFixture();
        fixture.Preparation.Setup(service => service.CreateAsync(
                It.IsAny<CreatePreparedClosePlanRequestDto>(), "ops-user", "tenant-test", "tenant-test",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ClosePreparationPreviewStaleException("The preparation preview is stale. Preview again."));
        await using var app = await CreateAppAsync(fixture.Register,
            mapLedgerApi: true, currentUserPermissions: UserPermission.ManageLedgerReports);

        using var response = await fixture.PostAsync(app.GetTestClient(), "create");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("preview is stale");
        (await response.Content.ReadAsStringAsync()).Should().Contain("PREPARATION_PREVIEW_STALE");
    }

    private sealed class ClosePreparationEndpointFixture
    {
        private readonly ClosePeriodPlanDto _sourcePlan;
        private readonly ClosePlanTemplateDto _template;
        private readonly ClosePreparationPreviewDto _preview;
        private readonly Mock<IFundProfileTenancyRegistry> _registry = new(MockBehavior.Strict);

        public Mock<IAccountingClosePreparationService> Preparation { get; } = new(MockBehavior.Strict);
        public Mock<IAccountingCloseManagementService> Close { get; } = new(MockBehavior.Strict);
        public Mock<ILedgerBookService> Books { get; } = new(MockBehavior.Strict);

        public ClosePreparationEndpointFixture(bool foreignTarget = false)
        {
            var sourceWorkflowId = Guid.NewGuid();
            var accountId = Guid.NewGuid();
            var sourceBookId = Guid.NewGuid();
            var now = DateTimeOffset.Parse("2026-10-06T12:00:00Z");
            var sourceBook = new LedgerBookDto(sourceBookId, "fund-source", accountId,
                FundStructureNodeKindDto.Account, "Source book", "USD", now, now);
            var targetBook = sourceBook with
            {
                LedgerBookId = Guid.NewGuid(),
                FundProfileId = foreignTarget ? "fund-foreign" : "fund-source",
                DisplayName = "Target book"
            };
            var sourcePeriod = new LedgerPeriodDto(Guid.NewGuid(), sourceBookId, 2026, 9, "2026-09",
                new(2026, 9, 1), new(2026, 9, 30), LedgerPeriodStatusDto.HardClosed, now, now, 4);
            var targetPeriod = new LedgerPeriodDto(Guid.NewGuid(), targetBook.LedgerBookId, 2026, 10, "2026-10",
                new(2026, 10, 1), new(2026, 10, 31), LedgerPeriodStatusDto.Open, now, null, 1);
            _sourcePlan = BuildScopedClosePlan(sourceWorkflowId, sourceBookId, accountId) with
            {
                PeriodId = sourcePeriod.PeriodId.ToString("D"),
                PeriodStart = sourcePeriod.StartDate,
                PeriodEnd = sourcePeriod.EndDate,
                WorkflowId = sourceWorkflowId,
                FundAccountId = accountId
            };
            _template = new(Guid.NewGuid(), 1, "Monthly close", sourceWorkflowId, sourceBookId,
                sourcePeriod.PeriodId.ToString("D"), accountId, sourceBook.FundProfileId,
                sourceBook.AccountingPolicyId, sourceBook.AccountingPolicyVersion, _sourcePlan.MaterialityPolicy,
                new("weekdays", "1", [0, 6], []), [], now, "template-owner", []);
            _preview = new(Guid.NewGuid(), _template.TemplateId, 1, sourceWorkflowId, targetBook, targetPeriod,
                _template.Calendar, [], [], false, true, now, now.AddMinutes(30));

            Close.Setup(service => service.GetPeriodPlanScopedAsync(sourceWorkflowId,
                "tenant-test", "tenant-test", It.IsAny<CancellationToken>())).ReturnsAsync(_sourcePlan);
            Books.Setup(service => service.GetBookAsync(sourceBookId, It.IsAny<CancellationToken>())).ReturnsAsync(sourceBook);
            Books.Setup(service => service.GetBookAsync(targetBook.LedgerBookId, It.IsAny<CancellationToken>())).ReturnsAsync(targetBook);
            Books.Setup(service => service.ListPeriodsAsync(It.Is<LedgerPeriodQuery>(query => query.LedgerBookId == sourceBookId),
                It.IsAny<CancellationToken>())).ReturnsAsync([sourcePeriod]);
            Books.Setup(service => service.ListPeriodsAsync(It.Is<LedgerPeriodQuery>(query => query.LedgerBookId == targetBook.LedgerBookId),
                It.IsAny<CancellationToken>())).ReturnsAsync([targetPeriod]);
            _registry.Setup(registry => registry.ResolveAsync("fund-source", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundProfileOwnership("fund-source", "tenant-test", "tenant-test"));
            _registry.Setup(registry => registry.ResolveAsync("fund-foreign", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FundProfileOwnership("fund-foreign", "tenant-foreign", "tenant-foreign"));
            Preparation.Setup(service => service.GetTemplateAsync(_template.TemplateId, 1,
                It.IsAny<CancellationToken>())).ReturnsAsync(_template);
            Preparation.Setup(service => service.GetPreviewAsync(_preview.PreviewId,
                It.IsAny<CancellationToken>())).ReturnsAsync(_preview);
        }

        public void Register(IServiceCollection services)
        {
            services.AddSingleton(Preparation.Object);
            services.AddSingleton(Close.Object);
            services.AddSingleton(Books.Object);
            services.AddSingleton(Mock.Of<ILedgerJournalStore>());
            services.AddSingleton(_registry.Object);
        }

        public void AllowPreparation()
        {
            Preparation.Setup(service => service.CaptureTemplateAsync(It.IsAny<CaptureClosePlanTemplateRequestDto>(),
                It.IsAny<string>(), "tenant-test", "tenant-test", It.IsAny<CancellationToken>())).ReturnsAsync(_template);
            Preparation.Setup(service => service.PreviewAsync(It.IsAny<PreviewClosePreparationRequestDto>(),
                It.IsAny<string>(), "tenant-test", "tenant-test", It.IsAny<CancellationToken>())).ReturnsAsync(_preview);
            Preparation.Setup(service => service.CreateAsync(It.IsAny<CreatePreparedClosePlanRequestDto>(),
                    It.IsAny<string>(), "tenant-test", "tenant-test", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PreparedClosePlanResultDto(Guid.NewGuid(), _template.TemplateId, 1,
                    _sourcePlan.WorkflowId!.Value, _preview.TargetBook.LedgerBookId, _preview.TargetPeriod.PeriodId,
                    _preview.CreatedAtUtc, "authenticated-accountant", false, _sourcePlan with { IsPeriodLocked = false }, []));
        }

        public Task<HttpResponseMessage> PostAsync(HttpClient client, string operation)
        {
            const string forgedScope = "?tenantId=forged-tenant&companyId=forged-company";
            return operation switch
            {
                "capture" => client.PostAsJsonAsync(UiApiRoutes.LedgerCloseManagementTemplates + forgedScope,
                    new CaptureClosePlanTemplateRequestDto(_template.SourceWorkflowId, "Monthly close", [],
                        _template.Calendar, Actor: "forged-actor"), ServerJsonOptions),
                "preview" => client.PostAsJsonAsync(UiApiRoutes.LedgerCloseManagementPreparationPreview + forgedScope,
                    new PreviewClosePreparationRequestDto(_template.TemplateId, 1, _preview.TargetBook.LedgerBookId,
                        _preview.TargetPeriod.PeriodId, [], Actor: "forged-actor"), ServerJsonOptions),
                "create" => client.PostAsJsonAsync(UiApiRoutes.LedgerCloseManagementPreparationCreate + forgedScope,
                    new CreatePreparedClosePlanRequestDto(_preview.PreviewId, "stable-retry-key", "forged-actor"), ServerJsonOptions),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
        }
    }
}
