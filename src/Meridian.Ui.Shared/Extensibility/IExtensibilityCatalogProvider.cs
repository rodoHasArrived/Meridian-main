using Meridian.Contracts.Extensibility;

namespace Meridian.Ui.Shared.Extensibility;

/// <summary>
/// Supplies extension registrations to the workstation catalog.
/// </summary>
public interface IExtensibilityCatalogProvider
{
    IReadOnlyList<ExtensibilityRegistrationDto> GetRegistrations();
}
