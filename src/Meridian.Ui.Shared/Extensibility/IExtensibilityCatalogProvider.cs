using Meridian.Contracts.Extensibility;

namespace Meridian.Ui.Shared.Extensibility;

public interface IExtensibilityCatalogProvider
{
    IReadOnlyList<ExtensibilityRegistrationDto> GetRegistrations();
}
