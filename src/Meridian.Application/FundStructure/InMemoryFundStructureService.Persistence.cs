using System.Text.Json;
using Meridian.Application.Durability;

namespace Meridian.Application.FundStructure;

public sealed partial class InMemoryFundStructureService
{
    private void LoadState()
    {
        try
        {
            var json = _stateStore.Load();
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var state = JsonSerializer.Deserialize<PersistedState>(json, JsonOptions);
            if (state is null)
            {
                return;
            }

            foreach (var organization in state.Organizations)
            {
                _organizations[organization.OrganizationId] = organization;
            }

            foreach (var business in state.Businesses)
            {
                _businesses[business.BusinessId] = business;
            }

            foreach (var client in state.Clients)
            {
                _clients[client.ClientId] = client;
            }

            foreach (var fund in state.Funds)
            {
                _funds[fund.FundId] = fund;
            }

            foreach (var sleeve in state.Sleeves)
            {
                _sleeves[sleeve.SleeveId] = sleeve;
            }

            foreach (var vehicle in state.Vehicles)
            {
                _vehicles[vehicle.VehicleId] = vehicle;
            }

            foreach (var entity in state.Entities)
            {
                _entities[entity.EntityId] = entity;
            }

            foreach (var portfolio in state.InvestmentPortfolios)
            {
                _investmentPortfolios[portfolio.InvestmentPortfolioId] = portfolio;
            }

            foreach (var link in state.OwnershipLinks)
            {
                _ownershipLinks[link.OwnershipLinkId] = link;
            }

            foreach (var assignment in state.Assignments)
            {
                _assignments[assignment.AssignmentId] = assignment;
            }

            foreach (var linkedAccountId in state.LinkedAccountIds)
            {
                _linkedAccountIds.Add(linkedAccountId);
            }

            _stateVersion = 1;
            _persistedVersion = 1;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not corruption: the snapshot is intact and must not be quarantined.
            throw;
        }
        catch (Exception ex)
        {
            QuarantineUnreadableSnapshot(ex);
        }
    }

    /// <summary>
    /// Preserves an unreadable snapshot, then resets to a genuinely empty working set.
    /// </summary>
    /// <remarks>
    /// Startup availability is preserved for malformed snapshots, but this service follows a
    /// load-mutate-save pattern: the next mutation calls <c>AtomicFileWriter</c>, which would
    /// overwrite the unreadable file and destroy the only copy of the governance graph. The
    /// quarantine copy is what makes that recoverable, which is why failing to take one is fatal
    /// by design — see <see cref="CorruptStoreQuarantine.PreserveOrThrow"/>.
    /// <para>
    /// The catch is deliberately broad. <c>File.ReadAllText</c> also throws
    /// <see cref="UnauthorizedAccessException"/>, <see cref="PathTooLongException"/> and
    /// <see cref="NotSupportedException"/>, none of which derive from <see cref="IOException"/>,
    /// so a narrower filter would let a permissions problem take down startup despite the stated
    /// intent of keeping it available.
    /// </para>
    /// </remarks>
    private void QuarantineUnreadableSnapshot(Exception loadFailure)
    {
        // LoadState fills the dictionaries incrementally, so a mid-load failure leaves a partially
        // materialised graph behind. That is worse than nothing because it still looks valid.
        DiscardPartiallyLoadedState();

        var snapshotPath = _stateStore.BackingFilePath;
        if (snapshotPath is null)
        {
            Log.Error(
                loadFailure,
                "Fund structure snapshot could not be loaded from a store with no backing file; starting from an empty working set (persistence enabled: {PersistenceEnabled})",
                _persistenceEnabled);
            return;
        }

        var quarantinePath = CorruptStoreQuarantine.PreserveOrThrow(snapshotPath, loadFailure);

        // Error, not Warning: this is a total reset of governance state, and an operator has to act
        // on the quarantine copy before the next write makes that reset permanent.
        Log.Error(
            loadFailure,
            "Fund structure snapshot at {SnapshotPath} could not be loaded; unreadable file preserved at {QuarantinePath}, starting from an empty working set (persistence enabled: {PersistenceEnabled})",
            snapshotPath,
            quarantinePath,
            _persistenceEnabled);
    }

    private void DiscardPartiallyLoadedState()
    {
        _organizations.Clear();
        _businesses.Clear();
        _clients.Clear();
        _funds.Clear();
        _sleeves.Clear();
        _vehicles.Clear();
        _entities.Clear();
        _investmentPortfolios.Clear();
        _ownershipLinks.Clear();
        _assignments.Clear();
        _linkedAccountIds.Clear();
    }
}
