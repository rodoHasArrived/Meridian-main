using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Identity.Auth;
using Meridian.Storage.Ledger;
using Meridian.Tests.Storage;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Meridian.Tests.Ui;

public sealed partial class WorkstationEndpointsTests
{
    [LedgerDatabaseFact]
    [Trait("Category", "Integration")]
    public async Task CreateLedgerPeriod_OverridesClientCreatorWithAuthenticatedActorInDurableAudit()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var ledger = new PostgresLedgerBookService(database.JournalStore);
        var book = await ledger.CreateBookAsync(new CreateLedgerBookRequest(
            "fund-audit", Guid.NewGuid(), FundStructureNodeKindDto.Fund, "Audit actor", "USD"), ct);
        await using var app = await CreateAppAsync(
            configureServices: services => services.AddSingleton<ILedgerBookService>(ledger),
            currentUserPermissions: RolePermissions.For(UserRole.Controller),
            currentUserRole: UserRole.Controller, currentUserName: "authenticated-controller", mapLedgerApi: true);
        var response = await app.GetTestClient().PostAsJsonAsync("/api/ledger/periods",
            new CreateLedgerPeriodRequest(book.LedgerBookId, 2026, 5, "May", new(2026, 5, 1), new(2026, 5, 31))
            { CreatedBy = "forged-client-creator" }, ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        (await database.JournalStore.VerifyLedgerEventAuditAsync(ct)).ChainedEvents.Should().Be(1);
        await using var connection = new NpgsqlConnection(database.Options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = $"select actor from \"{database.Options.SchemaName}\".ledger_event_audit_events";
        (await command.ExecuteScalarAsync(ct)).Should().Be("authenticated-controller");
    }
}
