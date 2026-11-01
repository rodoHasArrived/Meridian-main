#if WINDOWS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels.Settings;
using Moq;

namespace Meridian.Wpf.Tests.ViewModels;

public sealed class ProviderOwnershipAdministrationViewModelTests
{
    private static ProviderConnectionDto Connection(string id, string family = "alpaca", string? account = "acct") => new(
        id, family, id, "Broker", "ReadOnly", true, null, null, account, null, [], null, false, "tenant-a", "paper");

    private static ProviderCredentialStoreStatus Status(string? generation) => new(
        "alpaca", "Alpaca", ProviderCredentialStateDto.Configured, ProviderCredentialSourceDto.LocalEncryptedStore,
        ProviderVerificationStateDto.NotVerified, null, null, null, null, null, null, null, "paper", null, [], ["KeyId"],
        generation is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["credentialGeneration"] = generation });

    private static ProviderCredentialMigrationReceipt Receipt(string migrationId, bool rolledBack) => new(
        migrationId, "alpaca", new ProviderCredentialScope("tenant-a", "paper-a", "acct", "paper"), "gen-1", "operator-a",
        new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero), rolledBack, rolledBack ? "operator-a" : null);

    [Fact]
    public async Task LoadConnections_KeepsOnlyCompleteUniqueOwnedConnectionsForTheProvider()
    {
        var client = new Mock<IWorkstationProviderOwnershipApiClient>();
        client.Setup(c => c.GetConnectionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ApiResponse<ProviderConnectionDto[]>.Ok(
        [
            Connection("paper-a"), Connection("dup"), Connection("dup"), Connection("no-account", account: null), Connection("other", family: "polygon")
        ]));
        var view = new ProviderOwnershipAdministrationViewModel(client.Object) { ProviderId = "Alpaca" };

        await view.LoadConnectionsCommand.ExecuteAsync(null);

        view.OwnedConnections.Should().ContainSingle().Which.ConnectionId.Should().Be("paper-a");
        view.MigrateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Migrate_EchoesPreviewedGeneration_ThenRollsBackTheSameReceipt()
    {
        var client = new Mock<IWorkstationProviderOwnershipApiClient>();
        client.Setup(c => c.PreviewMigrationAsync("paper-a", "alpaca", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProviderCredentialStoreStatus>.Ok(Status("gen-1")));
        ProviderCredentialMigrationRequest? migrated = null;
        client.Setup(c => c.MigrateAsync("paper-a", It.IsAny<ProviderCredentialMigrationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, ProviderCredentialMigrationRequest, CancellationToken>((_, request, _) => migrated = request)
            .ReturnsAsync((string _, ProviderCredentialMigrationRequest request, CancellationToken _) =>
                ApiResponse<ProviderCredentialMigrationReceipt>.Ok(Receipt(request.MigrationId, false)));
        ProviderCredentialMigrationRequest? rolledBack = null;
        client.Setup(c => c.RollbackAsync("paper-a", It.IsAny<ProviderCredentialMigrationRequest>(), It.IsAny<CancellationToken>()))
            .Callback<string, ProviderCredentialMigrationRequest, CancellationToken>((_, request, _) => rolledBack = request)
            .ReturnsAsync((string _, ProviderCredentialMigrationRequest request, CancellationToken _) =>
                ApiResponse<ProviderCredentialMigrationReceipt>.Ok(Receipt(request.MigrationId, true)));
        var view = new ProviderOwnershipAdministrationViewModel(client.Object) { ProviderId = "alpaca" };
        view.OwnedConnections.Add(Connection("paper-a"));
        view.SelectedConnection = view.OwnedConnections[0];

        view.MigrateCommand.CanExecute(null).Should().BeFalse("a migration needs the previewed generation");
        await view.PreviewMigrationCommand.ExecuteAsync(null);
        view.ExpectedGeneration.Should().Be("gen-1");
        view.MigrateCommand.CanExecute(null).Should().BeTrue();

        await view.MigrateCommand.ExecuteAsync(null);
        migrated.Should().NotBeNull();
        migrated!.ExpectedGeneration.Should().Be("gen-1");
        migrated.MigrationId.Should().NotBeNullOrWhiteSpace();
        view.Receipt!.RolledBack.Should().BeFalse();
        view.RollbackCommand.CanExecute(null).Should().BeTrue();

        await view.RollbackCommand.ExecuteAsync(null);
        rolledBack.Should().NotBeNull();
        rolledBack!.MigrationId.Should().Be(migrated.MigrationId, "rollback addresses the exact retained receipt");
        rolledBack.ExpectedGeneration.Should().BeNull();
        view.Receipt!.RolledBack.Should().BeTrue();
        view.RollbackCommand.CanExecute(null).Should().BeFalse();
        view.StatusText.Should().Contain("rolled back by operator-a");
    }

    [Fact]
    public async Task PreviewWithoutGeneration_BlocksMigration_AndForbiddenBindExplainsPermissions()
    {
        var client = new Mock<IWorkstationProviderOwnershipApiClient>();
        client.Setup(c => c.PreviewMigrationAsync("paper-a", "alpaca", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProviderCredentialStoreStatus>.Ok(Status(null)));
        client.Setup(c => c.BindRuntimeCredentialAsync("paper-a", It.IsAny<BindProviderRuntimeCredentialRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProviderRuntimeCredentialBinding>.Fail("Forbidden", 403));
        var view = new ProviderOwnershipAdministrationViewModel(client.Object) { ProviderId = "alpaca" };
        view.OwnedConnections.Add(Connection("paper-a"));
        view.SelectedConnection = view.OwnedConnections[0];

        await view.PreviewMigrationCommand.ExecuteAsync(null);
        view.ExpectedGeneration.Should().BeNull();
        view.MigrateCommand.CanExecute(null).Should().BeFalse();
        view.StatusText.Should().Contain("nothing to migrate");

        await view.BindCommand.ExecuteAsync(null);
        view.StatusText.Should().Contain("tenant-scoped session");
    }

    [Fact]
    public async Task Bind_ShowsPendingRestart_AndApplyClearsItOnlyWhenTheHostRecycles()
    {
        var client = new Mock<IWorkstationProviderOwnershipApiClient>();
        client.Setup(c => c.BindRuntimeCredentialAsync("paper-a", It.IsAny<BindProviderRuntimeCredentialRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProviderRuntimeCredentialBinding>.Ok(new ProviderRuntimeCredentialBinding("alpaca", "tenant-a", "paper-a")));
        var bound = new DateTimeOffset(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);
        client.Setup(c => c.GetRuntimeCredentialStatusAsync("paper-a", "alpaca", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProviderRuntimeCredentialApplicationStatus>.Ok(new("alpaca", "tenant-a", "paper-a", true, true, bound, null, "pending")));
        client.SetupSequence(c => c.ApplyRuntimeCredentialAsync("paper-a", It.IsAny<ApplyProviderRuntimeCredentialRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<ProviderRuntimeCredentialApplicationStatus>.Ok(new("alpaca", "tenant-a", "paper-a", true, true, bound, null, "pending")))
            .ReturnsAsync(ApiResponse<ProviderRuntimeCredentialApplicationStatus>.Ok(new("alpaca", "tenant-a", "paper-a", true, false, bound, bound.AddMinutes(5), "applied")));
        var view = new ProviderOwnershipAdministrationViewModel(client.Object) { ProviderId = "alpaca" };
        view.OwnedConnections.Add(Connection("paper-a"));
        view.SelectedConnection = view.OwnedConnections[0];

        view.ApplyRuntimeCommand.CanExecute(null).Should().BeFalse("nothing is pending before a bind or check");
        await view.BindCommand.ExecuteAsync(null);
        view.RuntimeApplicationText.Should().StartWith("Pending restart");
        view.ApplyRuntimeCommand.CanExecute(null).Should().BeTrue();

        await view.ApplyRuntimeCommand.ExecuteAsync(null);
        view.StatusText.Should().Contain("no in-process provider recycle");
        view.ApplyRuntimeCommand.CanExecute(null).Should().BeTrue();

        await view.ApplyRuntimeCommand.ExecuteAsync(null);
        view.RuntimeApplicationText.Should().StartWith("Applied: running instances resolve credentials under paper-a");
        view.ApplyRuntimeCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task CreateConnection_SendsNoTenantOrActor()
    {
        var client = new Mock<IWorkstationProviderOwnershipApiClient>();
        CreateOwnedProviderConnectionRequest? sent = null;
        client.Setup(c => c.CreateOwnedConnectionAsync(It.IsAny<CreateOwnedProviderConnectionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<CreateOwnedProviderConnectionRequest, CancellationToken>((request, _) => sent = request)
            .ReturnsAsync(ApiResponse<ProviderConnectionDto>.Ok(Connection("paper-c")));
        var view = new ProviderOwnershipAdministrationViewModel(client.Object)
        {
            ProviderId = "alpaca", NewConnectionId = "paper-c", NewDisplayName = "Paper C", NewExternalAccountId = "acct-c", NewEnvironment = "paper"
        };

        await view.CreateConnectionCommand.ExecuteAsync(null);

        sent.Should().NotBeNull();
        sent!.Connection.ConnectionId.Should().Be("paper-c");
        sent.Connection.ProviderFamilyId.Should().Be("alpaca");
        sent.Connection.ExternalAccountId.Should().Be("acct-c");
        sent.Connection.Scope.Should().BeNull();
        sent.Environment.Should().Be("paper");
        view.StatusText.Should().Contain("paper-c created for the authenticated tenant");
    }
}
#endif
