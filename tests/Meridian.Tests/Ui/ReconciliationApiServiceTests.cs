using FluentAssertions;
using Meridian.Application.Reconciliation;
using Meridian.Contracts.Workstation;
using Meridian.Domain.Reconciliation;
using Meridian.Infrastructure.Reconciliation;
using Meridian.Ui.Shared.Contracts.Reconciliation;
using Meridian.Ui.Services.Services.Reconciliation;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed class ReconciliationApiServiceTests
{
    [Fact]
    public async Task CreateStatementRunAsync_CustodianStatement_ShouldPersistBreaksAndCases()
    {
        var root = Path.Combine(Path.GetTempPath(), $"meridian-reconciliation-api-{Guid.NewGuid():N}");
        var statementPath = Path.Combine(root, "custodian-statement.csv");
        Directory.CreateDirectory(root);
        await File.WriteAllLinesAsync(statementPath,
        [
            "account,symbol,quantity,price,cashAmount,activityType,tradeDate",
            "FUND-1,SPY,10,500,0,position,2026-05-28",
            "FUND-1,,0,0,2500.25,cash,2026-05-28",
            "FUND-1,MSFT,1,15.75,0,fee,2026-05-28"
        ]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<StatementReconciliationService>();
        services.AddSingleton<StatementReconciliationContextAdapter>();
        services.AddSingleton<IStatementReconciliationValidationService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.AddSingleton<IDataIntegrationIngestionService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.AddSingleton<IReconciliationCaseIntakeService>(sp => sp.GetRequiredService<StatementReconciliationContextAdapter>());
        services.AddSingleton<ICanonicalStatementStore>(_ => new JsonCanonicalStatementStore(root));
        services.AddSingleton<IReconciliationCaseStore>(_ => new JsonReconciliationCaseStore(root));
        services.AddSingleton<IReconciliationBreakStore>(_ => new JsonReconciliationBreakStore(root));
        services.AddSingleton<IBrokerStatementService>(sp => new CsvBrokerStatementService(sp.GetRequiredService<ICanonicalStatementStore>()));
        services.AddSingleton<IStatementRunWorkflowService, StatementRunWorkflowService>();
        services.AddSingleton<IReconciliationApiService, ReconciliationApiService>();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IReconciliationApiService>();

        var created = await service.CreateStatementRunAsync(
            new StatementRunCreateDto(
                Broker: "custodian",
                SourceInstitution: "Sample Custodian",
                FundAccountId: "fund-account-1",
                ExternalAccountId: "external-account-1",
                StatementPeriodStart: new DateOnly(2026, 5, 1),
                StatementPeriodEnd: new DateOnly(2026, 5, 31),
                SourcePath: statementPath,
                OriginalFileName: "custodian-statement.csv",
                MappingProfileId: "canonical-csv-v1",
                ToleranceProfileId: "statement-default",
                ImportedBy: "ops-user"),
            CancellationToken.None);

        created.Should().NotBeNull();
        created!.Status.Should().Be(StatementRunStatus.ReviewRequired);
        created.MatchSummary!.StatementItemCount.Should().Be(3);
        created.MatchSummary.BreakCount.Should().Be(2);
        created.Breaks.Should().HaveCount(2);
        created.Cases.Should().HaveCount(2);
        created.Cases.Should().OnlyContain(item =>
            item.Owner == "fund-ops" &&
            item.Priority == "High" &&
            item.Disposition == "NeedsInvestigation" &&
            item.AgingDays == 0 &&
            item.DueAtUtc.HasValue &&
            item.EvidenceLink!.Contains("/api/workstation/reconciliation/statement-runs/", StringComparison.OrdinalIgnoreCase));
        foreach (var item in created.Cases)
        {
            item.CommentThreads.Should().Contain(thread =>
                thread.Subject == "External statement intake" &&
                thread.Comments!.Any(comment =>
                    comment.Actor == "system" &&
                    comment.Body!.Contains("Suggested next action:", StringComparison.OrdinalIgnoreCase)));
            item.Attachments.Should().Contain(attachment =>
                attachment.EvidenceKind == "ExternalStatementRow" &&
                attachment.SourceSystem == "custodian");
            item.BreakExplanation.Should().NotBeNull();
            item.BreakExplanation!.SourceSystems.Should().Contain("Sample Custodian");
            item.BreakExplanation.SourceSystems.Should().Contain("Meridian ledger");
            item.BreakExplanation.ProbableCause.Should().NotBeNullOrWhiteSpace();
            item.BreakExplanation.LedgerImpact.Should().NotBeNullOrWhiteSpace();
            item.BreakExplanation.SuggestedNextAction.Should().NotBeNullOrWhiteSpace();
            item.AuditEvents.Should().Contain(audit => audit.EventType == "ExternalStatementCaseCreated");
        }

        var openCases = await service.ListOpenCasesAsync(CancellationToken.None);
        openCases.Should().HaveCount(2);
        openCases.Should().OnlyContain(item =>
            item.Assignee == "fund-ops" &&
            item.SlaState == "OnTrack" &&
            item.Version > 0);

        var exceptions = await service.ListOpenExceptionsAsync(CancellationToken.None);
        exceptions.Should().HaveCount(2);
        exceptions.Should().OnlyContain(item => item.ImportId == created.ImportId);

        var reloaded = await service.GetStatementRunAsync(created.RunId!, CancellationToken.None);
        reloaded.Should().NotBeNull();
        reloaded!.Cases.Should().HaveCount(2);
        reloaded.Cases.Should().OnlyContain(item =>
            item.Attachments != null &&
            item.Attachments.Count > 0 &&
            item.CommentThreads != null &&
            item.CommentThreads.Count > 0 &&
            item.BreakExplanation != null &&
            item.AuditEvents != null &&
            item.AuditEvents.Count > 0);
        reloaded.Breaks.Should().HaveCount(2);
    }
}
