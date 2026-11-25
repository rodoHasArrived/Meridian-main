using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Onboarding;
using Npgsql;
using Fixture = Meridian.Tests.Ui.OnboardingWorkspaceServiceTests.Fixture;

namespace Meridian.Tests.Storage;

public sealed class OnboardingWorkspacePostgresTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task Workspace_RoundTripsWithItsHistoryAndStaysScoped()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        using var fixture = new Fixture();
        var store = new PostgresOnboardingWorkspaceStore(database.Options);
        var service = Service(store, fixture);
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        fixture.Source.Set("retained", fixture.Capture(Fixture.January, "retained", 101m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "retained");

        var restarted = new PostgresOnboardingWorkspaceStore(database.Options);
        var retained = await restarted.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId);
        JsonSerializer.Serialize(retained).Should().Be(JsonSerializer.Serialize(workspace));
        (await restarted.ListAsync(Fixture.Tenant, Fixture.Company)).Should().ContainSingle()
            .Which.WorkspaceId.Should().Be(workspace.WorkspaceId);
        (await restarted.ListByLedgerBookAsync(Fixture.Tenant, null, fixture.Scope.LedgerBookId)).Should().ContainSingle();
        (await restarted.ListByLedgerBookAsync(Fixture.Tenant, "other-company", fixture.Scope.LedgerBookId)).Should().BeEmpty();
        (await restarted.GetAsync("other-tenant", Fixture.Company, workspace.WorkspaceId)).Should().BeNull();
        (await restarted.GetAsync(Fixture.Tenant, "other-company", workspace.WorkspaceId)).Should().BeNull();
        (await restarted.ListAsync("other-tenant", Fixture.Company)).Should().BeEmpty();
        (await restarted.ListByLedgerBookAsync("other-tenant", null, fixture.Scope.LedgerBookId)).Should().BeEmpty();
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task Save_RefusesStaleVersionsHistoryRewritesAndScopeReplacement()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        using var fixture = new Fixture();
        var store = new PostgresOnboardingWorkspaceStore(database.Options);
        var service = Service(store, fixture);
        var created = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        fixture.Source.Set("retained", fixture.Capture(Fixture.January, "retained", 101m, 10m, 100m));
        var compared = await fixture.Compare(service, created, Fixture.January, "retained");

        var stale = () => store.SaveAsync(created with { Version = created.Version + 1 }, created.Version);
        await stale.Should().ThrowAsync<OnboardingConcurrencyException>();
        var skipped = () => store.SaveAsync(compared with { Version = compared.Version + 2 }, compared.Version);
        await skipped.Should().ThrowAsync<OnboardingConcurrencyException>().WithMessage("*advance exactly once*");
        var rewrite = () => store.SaveAsync(compared with { Version = compared.Version + 1, Comparisons = [] }, compared.Version);
        await rewrite.Should().ThrowAsync<OnboardingValidationException>().WithMessage("*immutable*");
        var otherTenant = () => store.SaveAsync(compared with
        {
            Version = compared.Version + 1,
            Scope = compared.Scope with { TenantId = "other-tenant" }
        }, compared.Version);
        await otherTenant.Should().ThrowAsync<OnboardingValidationException>().WithMessage("*cannot be replaced*");

        (await store.GetAsync(Fixture.Tenant, Fixture.Company, compared.WorkspaceId))!.Version.Should().Be(compared.Version);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentWritersOfOneVersion_OnlyOneAdvancesIt()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        using var fixture = new Fixture();
        var created = await Service(new PostgresOnboardingWorkspaceStore(database.Options), fixture)
            .CreateAsync(fixture.Request(), Fixture.Owner);
        var next = created with { Version = created.Version + 1, UpdatedAtUtc = Fixture.Now.AddMinutes(1) };

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try
            {
                await new PostgresOnboardingWorkspaceStore(database.Options).SaveAsync(next, created.Version);
                return true;
            }
            catch (OnboardingConcurrencyException)
            {
                return false;
            }
        }));

        outcomes.Count(won => won).Should().Be(1);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task ShadowDesignation_IsExclusivePerLedgerBookAcrossStoreInstances()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        using var fixture = new Fixture();
        var service = Service(new PostgresOnboardingWorkspaceStore(database.Options), fixture);
        var first = await service.CreateAsync(fixture.Request() with { Name = "First trial" }, Fixture.Owner);
        var second = await service.CreateAsync(fixture.Request() with { Name = "Second trial" }, Fixture.Owner);
        var third = await service.CreateAsync(fixture.Request() with { Name = "Third trial" }, Fixture.Owner);

        // Independent stores (as in separate hosts) race to designate the same book: the index admits one.
        var attempts = await Task.WhenAll(new[] { first, second, third }.Select(async workspace =>
        {
            try
            {
                await Service(new PostgresOnboardingWorkspaceStore(database.Options), fixture).DesignateShadowAsync(
                    Fixture.Tenant, Fixture.Company, workspace.WorkspaceId, new(workspace.Version, "Race."), Fixture.Owner);
                return workspace.WorkspaceId;
            }
            catch (Exception ex) when (ex is OnboardingConcurrencyException or OnboardingValidationException)
            {
                return null;
            }
        }));
        var winner = attempts.Should().ContainSingle(id => id != null).Subject!;

        // The losing side of a race that bypassed the service pre-check is refused by the database.
        var loser = new[] { first, second, third }.First(workspace => workspace.WorkspaceId != winner);
        var bypass = () => new PostgresOnboardingWorkspaceStore(database.Options).SaveAsync(loser with
        {
            Version = loser.Version + 1,
            ShadowDesignation = new(OnboardingShadowStates.Shadow, loser.Scope.LedgerBookId, Fixture.Owner, Fixture.Now, "Bypass.")
        }, loser.Version);
        await bypass.Should().ThrowAsync<OnboardingConcurrencyException>().WithMessage("*already holds this ledger book*");
        (await service.GetAsync(Fixture.Tenant, Fixture.Company, loser.WorkspaceId))!.ShadowDesignation.Should().BeNull();

        // Withdrawing the trial releases the book for a new designation.
        var held = (await service.GetAsync(Fixture.Tenant, Fixture.Company, winner))!;
        await service.WithdrawShadowAsync(Fixture.Tenant, Fixture.Company, winner, new(held.Version, "Restart."), Fixture.Owner);
        var redesignated = await service.DesignateShadowAsync(Fixture.Tenant, Fixture.Company, loser.WorkspaceId,
            new(loser.Version, "Restart."), Fixture.Owner);
        redesignated.ShadowDesignation!.State.Should().Be(OnboardingShadowStates.Shadow);

        var guard = new OnboardingBookOfRecordGuard(new PostgresOnboardingWorkspaceStore(database.Options));
        var status = await guard.GetStatusAsync(Fixture.Tenant, Fixture.Company, fixture.Scope.LedgerBookId);
        status.IsBookOfRecord.Should().BeFalse();
        status.ShadowWorkspaceId.Should().Be(loser.WorkspaceId);
    }

    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task RetainedRows_CannotBeDeletedOrTamperedWithUndetected()
    {
        await using var database = await LedgerPostgresTestDatabase.CreateAsync();
        using var fixture = new Fixture();
        var store = new PostgresOnboardingWorkspaceStore(database.Options);
        var service = Service(store, fixture);
        var workspace = await service.CreateAsync(fixture.Request(), Fixture.Owner);
        fixture.Source.Set("retained", fixture.Capture(Fixture.January, "retained", 101m, 10m, 100m));
        workspace = await fixture.Compare(service, workspace, Fixture.January, "retained");

        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync();
        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = $"delete from \"{database.Options.SchemaName}\".onboarding_workspaces where workspace_id = @id;";
            delete.Parameters.AddWithValue("id", workspace.WorkspaceId);
            var remove = () => delete.ExecuteNonQueryAsync();
            await remove.Should().ThrowAsync<PostgresException>().WithMessage("*cannot be deleted*");
        }

        var tampered = workspace with
        {
            Comparisons = [workspace.Comparisons[0] with { MappingVersion = "rewritten-out-of-band" }]
        };
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = $"update \"{database.Options.SchemaName}\".onboarding_workspaces set payload_json = @payload where workspace_id = @id;";
            update.Parameters.AddWithValue("id", workspace.WorkspaceId);
            update.Parameters.AddWithValue("payload", OnboardingWorkspaceRetention.Serialize(tampered));
            await update.ExecuteNonQueryAsync();
        }

        var read = () => store.GetAsync(Fixture.Tenant, Fixture.Company, workspace.WorkspaceId);
        await read.Should().ThrowAsync<InvalidDataException>().WithMessage("*integrity*");
    }

    private static OnboardingWorkspaceService Service(IOnboardingWorkspaceStore store, Fixture fixture)
        => new(store, fixture.Source, TimeProvider.System);
}
