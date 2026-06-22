using Meridian.Contracts.Extensibility;

namespace Meridian.Ui.Shared.Extensibility;

public sealed class ExtensibilityCatalogService
{
    private readonly IReadOnlyList<IExtensibilityCatalogProvider> _providers;

    public ExtensibilityCatalogService(IEnumerable<IExtensibilityCatalogProvider> providers)
    {
        _providers = providers?.ToArray() ?? [];
    }

    public ExtensibilityCatalogDto GetCatalog(DateTimeOffset generatedAt)
    {
        var registrations = _providers
            .SelectMany(static provider => provider.GetRegistrations())
            .Where(static registration => !string.IsNullOrWhiteSpace(registration.RegistrationId))
            .GroupBy(static registration => registration.RegistrationId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .OrderBy(static registration => registration.Area)
            .ThenBy(static registration => registration.OwningContext, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static registration => registration.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ExtensibilityCatalogDto(
            CoreExtensibilityCatalog.SchemaVersion,
            generatedAt,
            CoreExtensibilityCatalog.StableCoreObjects,
            CoreExtensibilityCatalog.ConfigurableLayers,
            CoreExtensibilityCatalog.GovernedFoundations,
            registrations);
    }
}
