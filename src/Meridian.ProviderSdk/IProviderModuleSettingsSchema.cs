namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Optional interface implemented by provider modules to advertise typed settings fields.
/// When absent, the UI falls back to a generic key/value pair editor.
/// </summary>
public interface IProviderModuleSettingsSchema
{
    IReadOnlyList<ProviderSettingDescriptor> SettingDescriptors { get; }
}

/// <summary>
/// Describes a single configurable setting field for a provider module.
/// </summary>
public sealed record ProviderSettingDescriptor(
    string Key,
    string Label,
    string? Description = null,
    SettingType Type = SettingType.String,
    string? DefaultValue = null,
    bool Required = false,
    string[]? SelectOptions = null);

/// <summary>
/// Value type for a provider setting field.
/// </summary>
public enum SettingType
{
    String,
    Int,
    Bool,
    Select
}
