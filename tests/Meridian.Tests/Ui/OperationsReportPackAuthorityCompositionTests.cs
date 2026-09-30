using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class OperationsReportPackAuthorityCompositionTests
{
    [Theory]
    [InlineData("all")]
    [InlineData("packages")]
    [InlineData("books")]
    [InlineData("ownership")]
    [InlineData("journals")]
    public async Task PartialHost_ValidatesRegistration_AndMissingSourceBlocksReadiness(string missing)
    {
        // Use the production descriptor, isolated from unrelated workstation features. This
        // reproduces ValidateOnBuild in a seeded host without requiring a database connection.
        var workstation = new ServiceCollection();
        workstation.AddWorkstationSharedServices();
        IServiceCollection services = new ServiceCollection();
        services.Add(workstation.Single(descriptor => descriptor.ServiceType == typeof(IOperationsReportPackAuthority)));
        var packages = new Mock<IAccountingReportPackageService>(MockBehavior.Strict);
        var books = new Mock<ILedgerBookService>(MockBehavior.Strict);
        var ownership = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        var journals = new Mock<ILedgerJournalStore>(MockBehavior.Strict);
        if (missing is not ("all" or "packages"))
            services.AddSingleton(packages.Object);
        if (missing is not ("all" or "books"))
            services.AddSingleton(books.Object);
        if (missing is not ("all" or "ownership"))
            services.AddSingleton(ownership.Object);
        if (missing is not ("all" or "journals"))
            services.AddSingleton(journals.Object);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        var authority = provider.GetRequiredService<IOperationsReportPackAuthority>();
        var derivation = new OperationsStatusDerivationService();
        var workflows = new OperationsContinuityWorkflowService(
            new InMemoryOperationsContinuityRepository(derivation),
            new InMemoryOperationsWorkflowAuditStore(), derivation);
        var workflow = (await workflows.StartWorkflowAsync(new OperationsStartWorkflowRequestDto(
            Guid.NewGuid(), "2026-05", null, "custodian", "operator", LedgerBookId: Guid.NewGuid()))).Workflow!;

        var result = await authority.ResolveAsync(workflow, "retained-report", "tenant-alpha", "company-alpha");

        result.IsReady.Should().BeFalse();
        result.ReportPackId.Should().BeNull();
        result.EvidenceLinks.Should().BeEmpty();
        result.BlockingReason.Should().Contain("unavailable");
        packages.VerifyNoOtherCalls();
        books.VerifyNoOtherCalls();
        ownership.VerifyNoOtherCalls();
        journals.VerifyNoOtherCalls();
    }
}
