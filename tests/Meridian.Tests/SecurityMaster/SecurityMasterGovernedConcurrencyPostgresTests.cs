using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Storage.SecurityMaster;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;

namespace Meridian.Tests.SecurityMaster;

[Trait("Category", "Integration")]
public sealed class SecurityMasterGovernedConcurrencyPostgresTests(SecurityMasterDatabaseFixture fixture)
    : IClassFixture<SecurityMasterDatabaseFixture>
{
    [SecurityMasterDatabaseFact]
    public async Task TwoInstances_CannotCombineIndividuallyValidDates_AndRetryRevalidates()
    {
        var id = await SeedAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Service(id, beforeValidation: async () =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20));
        });
        var second = Service(id);
        var editStart = Edit(id, "assetSpecificTerms.profileFields.startDate", "2026-06-01");
        var editEnd = Edit(id, "assetSpecificTerms.profileFields.endDate", "2026-03-01");

        // Both dates are valid against the retained Jan–Dec interval, but invalid together.
        // Separate services and store instances use separate database connections; PostgreSQL
        // compositions deliberately do not take the process-local field-edit semaphore.
        var winner = first.UpdateSecurityFieldAsync(editStart);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var loser = second.UpdateSecurityFieldAsync(editEnd);
        try
        {
            await WaitForContendingGenerationAsync();
        }
        finally
        {
            release.TrySetResult();
        }
        await winner;
        await ((Func<Task>)(async () => await loser)).Should().ThrowAsync<SecurityMasterMutationConflictException>();
        await second.Invoking(s => s.UpdateSecurityFieldAsync(editEnd)).Should()
            .ThrowAsync<ArgumentException>().WithMessage("*PF_DATE_ORDER*");

        var overlay = await new PostgresOperatorOverridesStore(fixture.Options).GetAsync(id);
        overlay!.Values.Should().ContainSingle().Which.Key.Should().EndWith("startDate");
        (await new PostgresSecurityMasterRevisionStore(fixture.Options).ListBySecurityAsync(id)).Should().ContainSingle();
    }

    [SecurityMasterDatabaseFact]
    public async Task TwoInstances_ApprovalCannotObservePatchWithoutItsDraft()
    {
        var id = await SeedAsync();
        var overlays = new PostgresOperatorOverridesStore(fixture.Options);
        await overlays.PatchAsync(id, Patch("rating", "AA"), "maker");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revisionStore = new PostgresSecurityMasterRevisionStore(fixture.Options);
        var delayed = RevisionProxy(async (security, actor, path, effective, reason, fund, value, ct) =>
        {
            entered.SetResult(); // The patch ran, but the INSERT has not yet run.
            await release.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            return await revisionStore.CreateDraftAsync(security, actor, path, effective, reason, fund, value, ct);
        });
        var edit = Service(id, revisions: delayed).UpdateSecurityFieldAsync(Edit(id, "rating", "BBB"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        // An unrelated reader still sees the committed predecessor, never the half-edit.
        (await overlays.GetAsync(id))!.Values["rating"].Should().Be("AA");
        var reviewer = Service(id);
        var decision = new OperatorOverrideDecision(SecurityOverrideApprovalStatusDto.Approved, "checker");
        var approval = reviewer.RecordOperatorOverrideDecisionAsync(id, decision);
        try
        {
            await WaitForContendingGenerationAsync();
        }
        finally
        {
            release.TrySetResult();
        }
        await edit;
        await ((Func<Task>)(async () => await approval)).Should().ThrowAsync<SecurityMasterMutationConflictException>();
        await reviewer.Invoking(s => s.RecordOperatorOverrideDecisionAsync(id, decision)).Should()
            .ThrowAsync<InvalidOperationException>().WithMessage("*governed revision workflow*");
        var final = await overlays.GetAsync(id);
        final!.ApprovalStatus.Should().Be(SecurityOverrideApprovalStatusDto.Pending);
        final.Values["rating"].Should().Be("BBB");
        final.AuditTrail.Should().NotContain(entry => entry.EventType == "Approved");
    }

    [SecurityMasterDatabaseFact]
    public async Task DraftInsertFailure_RollsBackOverlayAuditAndGeneration()
    {
        var id = await SeedAsync();
        var overlays = new PostgresOperatorOverridesStore(fixture.Options);
        await overlays.PatchAsync(id, Patch("rating", "AA"), "maker");
        await overlays.RecordApprovalDecisionAsync(id, new(SecurityOverrideApprovalStatusDto.Approved, "checker"));
        var before = await overlays.GetAsync(id);
        var generation = await GenerationAsync(id);
        var store = new PostgresSecurityMasterRevisionStore(fixture.Options);
        // A fixture-only database constraint forces the draft INSERT to fail after overlay write.
        var constraint = "reject_draft_" + id.ToString("N");
        await ExecuteSqlAsync($"alter table {fixture.Options.Schema}.security_master_revisions add constraint {constraint} check (security_id <> '{id:D}'::uuid);");
        await Service(id).Invoking(s => s.UpdateSecurityFieldAsync(Edit(id, "rating", "BBB")))
            .Should().ThrowAsync<PostgresException>().Where(ex => ex.SqlState == PostgresErrorCodes.CheckViolation);

        (await new PostgresOperatorOverridesStore(fixture.Options).GetAsync(id)).Should().BeEquivalentTo(before);
        (await GenerationAsync(id)).Should().Be(generation);
        (await store.ListBySecurityAsync(id)).Should().BeEmpty();
        await ExecuteSqlAsync($"alter table {fixture.Options.Schema}.security_master_revisions drop constraint {constraint};");
        // A fresh instance can retry; the failed transaction left neither a draft nor a fence lock.
        await Service(id).UpdateSecurityFieldAsync(Edit(id, "rating", "BBB"));
    }

    [SecurityMasterDatabaseFact]
    public async Task CancellationAfterDraftInsert_RollsBackFirstEditAndAllowsRetry()
    {
        var id = await SeedAsync();
        using var cancellation = new CancellationTokenSource();
        var store = new PostgresSecurityMasterRevisionStore(fixture.Options);
        var cancel = RevisionProxy(async (security, actor, path, effective, reason, fund, value, ct) =>
        {
            var draft = await store.CreateDraftAsync(security, actor, path, effective, reason, fund, value, ct);
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return draft;
        });
        await Service(id, revisions: cancel).Invoking(s => s.UpdateSecurityFieldAsync(Edit(id, "rating", "AA"), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        (await new PostgresOperatorOverridesStore(fixture.Options).GetAsync(id)).Should().BeNull();
        (await GenerationAsync(id)).Should().Be(0);
        (await store.ListBySecurityAsync(id)).Should().BeEmpty();
        await Service(id).UpdateSecurityFieldAsync(Edit(id, "rating", "AA"));
    }

    [SecurityMasterDatabaseFact]
    public async Task ParallelPassportReads_SeeTheSameUncommittedMutation()
    {
        var id = await SeedAsync();
        var overlays = new PostgresOperatorOverridesStore(fixture.Options);
        var revisions = new PostgresSecurityMasterRevisionStore(fixture.Options);
        await PostgresSecurityMasterMutation.ExecuteAsync(fixture.Options, id, async () =>
        {
            await overlays.PatchAsync(id, Patch("rating", "AA"), "maker");
            await revisions.CreateDraftAsync(id, "maker");
            var overlayRead = overlays.GetAsync(id);
            var revisionRead = revisions.ListBySecurityAsync(id);
            await Task.WhenAll(overlayRead, revisionRead);
            (await overlayRead)!.Values["rating"].Should().Be("AA");
            (await revisionRead).Should().ContainSingle();
            return true;
        });
    }

    [SecurityMasterDatabaseFact]
    public async Task MixedDurabilityComposition_RefusesBeforeWritingOverlay()
    {
        var id = await SeedAsync();
        await Service(id, revisions: new InMemorySecurityMasterRevisionStore())
            .Invoking(s => s.UpdateSecurityFieldAsync(Edit(id, "rating", "AA")))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*same database and schema*");
        (await new PostgresOperatorOverridesStore(fixture.Options).GetAsync(id)).Should().BeNull();
    }

    private ISecurityMasterRevisionStore RevisionProxy(
        Func<Guid, string, string, DateTimeOffset, string, string?, SecurityMasterRevisionFieldValue?, CancellationToken, Task<SecurityMasterRevisionRecord>> create)
    {
        var mock = new Mock<ISecurityMasterRevisionStore>(MockBehavior.Strict);
        mock.As<ISecurityMasterMutationParticipant>().SetupGet(s => s.MutationOptions).Returns(fixture.Options);
        mock.Setup(s => s.CreateDraftAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<SecurityMasterRevisionFieldValue?>(), It.IsAny<CancellationToken>())).Returns(create);
        return mock.Object;
    }

    private SecurityMasterWorkbenchCommandService Service(Guid id, Func<Task>? beforeValidation = null,
        ISecurityMasterRevisionStore? revisions = null)
    {
        var query = new Mock<ISecurityMasterWorkbenchQueryService>();
        query.Setup(q => q.GetInstrumentPassportAsync(id, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                if (beforeValidation is not null)
                    await beforeValidation();
                return new InstrumentPassportDto(id, null!,
                    new SecurityMasterEconomicDefinitionDrillInDto(id, "CustomAsset", "USD", 1, DateTimeOffset.UtcNow,
                        null, null, null, null, null, null, null, null, null, null),
                    null!, [], [], [], null!, null!, null!, DateTimeOffset.UtcNow);
            });
        var projection = new Mock<ISecurityMasterStore>();
        projection.Setup(p => p.GetProjectionAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(
            new SecurityProjectionRecord(id, "CustomAsset", SecurityStatusDto.Active, "Dated asset", "USD", "InternalCode", id.ToString("N"),
                JsonSerializer.SerializeToElement(new { displayName = "Dated asset", currency = "USD" }),
                JsonSerializer.SerializeToElement(new
                {
                    customProfileId = "dated-profile",
                    profileVersion = 1,
                    profileFields = new { startDate = "2026-01-01", endDate = "2026-12-01" }
                }),
                JsonSerializer.SerializeToElement(new { sourceSystem = "test", asOf = "2026-01-01T00:00:00Z", updatedBy = "test" }),
                1, DateTimeOffset.UtcNow, null, [], []));
        var profile = new SecurityAssetProfileDefinitionDto("dated-profile", 1, "Dated Profile", "PrivateFunds", null,
            SecurityAssetProfileStatusDto.Approved,
            [new("startDate", "Start date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false),
             new("endDate", "End date", SecurityAssetProfileFieldTypeDto.Date, false, [], null, null, null, false, false)],
            [], ["Active"], [], [new("startDate", "endDate", "PF_DATE_ORDER", "startDate must be on or before endDate.")],
            new DateOnly(2026, 1, 1), null, "governance", DateTimeOffset.UtcNow, "test");
        return new SecurityMasterWorkbenchCommandService(new PostgresSecurityMasterEventStore(fixture.Options),
            new PostgresOperatorOverridesStore(fixture.Options), Mock.Of<ISecurityMasterConflictAuthorityPolicy>(),
            Mock.Of<ISecurityMasterConflictService>(), query.Object, Mock.Of<IOperationsContinuityWorkflowService>(),
            revisions ?? new PostgresSecurityMasterRevisionStore(fixture.Options), Mock.Of<IPeriodAwareRestatementResolver>(),
            Mock.Of<IAffectedLedgerBookResolver>(), [], NullLogger<SecurityMasterWorkbenchCommandService>.Instance,
            projectionStore: projection.Object, assetProfileCatalog: new StaticSecurityAssetProfileCatalog([profile]));
    }

    private static UpdateSecurityFieldRequest Edit(Guid id, string path, string value)
        => new(id, 1, path, value, DateTimeOffset.UtcNow, "maker", "Reference correction.");

    private static OperatorOverridesPatchRequest Patch(string key, string value)
        => new(new Dictionary<string, string> { [key] = value }, null);

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<Guid> SeedAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $$"""
            insert into {{fixture.Options.Schema}}.securities
                (security_id, asset_class, status, display_name, currency, primary_identifier_kind,
                 primary_identifier_value, common_terms, asset_specific_terms, provenance, version, effective_from)
            values (@id, 'CustomAsset', 'Active', 'Concurrency fixture', 'USD', 'InternalCode', @identifier,
                    '{}', '{}', '{}', 1, now());
            insert into {{fixture.Options.Schema}}.security_events
                (security_id, stream_version, event_type, event_timestamp, actor, payload)
            values (@id, 1, 'created', now(), 'test', '{}');
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("identifier", id.ToString("N"));
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<long> GenerationAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"select generation from {fixture.Options.Schema}.security_workbench_generations where security_id = @id", connection);
        command.Parameters.AddWithValue("id", id);
        return (long?)await command.ExecuteScalarAsync() ?? 0L;
    }

    private async Task WaitForContendingGenerationAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var connection = new NpgsqlConnection(fixture.Options.ConnectionString);
        await connection.OpenAsync(timeout.Token);
        // Assert the losing connection actually reached PostgreSQL's fence before releasing the
        // winner. An arbitrary delay could pass without exercising the race.
        await using var command = new NpgsqlCommand("""
            select exists(select 1 from pg_stat_activity
                where pid <> pg_backend_pid() and state = 'active' and wait_event_type = 'Lock'
                  and query like @pattern);
            """, connection);
        command.Parameters.AddWithValue("pattern", $"%{fixture.Options.Schema}%security_workbench_generations%");
        while (!(bool)(await command.ExecuteScalarAsync(timeout.Token))!)
        {
            await Task.Delay(10, timeout.Token);
        }
    }
}
