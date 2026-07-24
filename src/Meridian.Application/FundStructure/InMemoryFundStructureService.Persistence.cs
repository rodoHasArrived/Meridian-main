using System.Text.Json;

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
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Preserve startup availability for malformed or missing local snapshots — but a
            // discarded snapshot means governance state silently reset, so operators must see it.
            Log.Warning(
                ex,
                "Fund structure snapshot could not be loaded; starting from an empty working set (persistence enabled: {PersistenceEnabled})",
                _persistenceEnabled);
        }
    }
}
