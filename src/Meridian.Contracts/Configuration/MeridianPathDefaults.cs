using System.Text.Json;

namespace Meridian.Contracts.Configuration;

/// <summary>
/// Shared helpers for resolving Meridian config and data paths across hosts.
/// </summary>
public static class MeridianPathDefaults
{
    public const string ApplicationName = "Meridian";
    public const string ConfigFileName = "appsettings.json";
    public const string DefaultDataRoot = "data";

    private const string ConfigDirectoryName = "config";
    private const string DataRootPropertyName = "dataRoot";
    private const string StoragePropertyName = "storage";
    private const string LegacyBaseDirectoryPropertyName = "baseDirectory";

    public static string GetLocalApplicationDataRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, ApplicationName);
    }

    public static string GetDesktopConfigPath()
        => Path.Combine(GetLocalApplicationDataRoot(), ConfigFileName);

    public static string GetFirstRunMarkerPath()
        => Path.Combine(GetLocalApplicationDataRoot(), ".initialized");

    public static string GetCatalogRoot(string? configPath)
        => Path.Combine(GetConfigBaseDirectory(configPath), "_catalog");

    public static string GetConfigBaseDirectory(string? configPath)
    {
        var fullConfigPath = string.IsNullOrWhiteSpace(configPath)
            ? Path.GetFullPath(ConfigFileName)
            : Path.GetFullPath(configPath);

        var configDirectory = Path.GetDirectoryName(fullConfigPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
        {
            return Environment.CurrentDirectory;
        }

        var directoryInfo = new DirectoryInfo(configDirectory);
        if (directoryInfo.Name.Equals(ConfigDirectoryName, StringComparison.OrdinalIgnoreCase) &&
            directoryInfo.Parent is not null)
        {
            return directoryInfo.Parent.FullName;
        }

        return directoryInfo.FullName;
    }

    public static string ResolveDataRoot(string? configPath, string? configuredDataRoot)
    {
        var dataRoot = string.IsNullOrWhiteSpace(configuredDataRoot)
            ? DefaultDataRoot
            : configuredDataRoot.Trim();

        return Path.IsPathRooted(dataRoot)
            ? Path.GetFullPath(dataRoot)
            : Path.GetFullPath(Path.Combine(GetConfigBaseDirectory(configPath), dataRoot));
    }

    public static string ResolvePathFromConfigBase(string? configPath, string? configuredPath, string defaultRelativePath)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? defaultRelativePath
            : configuredPath.Trim();

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(GetConfigBaseDirectory(configPath), path));
    }

    public static string ResolveConfiguredDataRootFromJson(string json, string? currentDataRoot = null)
    {
        var configuredDataRoot = TryReadDataRoot(json);
        if (!string.IsNullOrWhiteSpace(configuredDataRoot))
        {
            return configuredDataRoot;
        }

        var legacyBaseDirectory = TryReadLegacyStorageBaseDirectory(json);
        if (!string.IsNullOrWhiteSpace(legacyBaseDirectory))
        {
            return legacyBaseDirectory;
        }

        return string.IsNullOrWhiteSpace(currentDataRoot)
            ? DefaultDataRoot
            : currentDataRoot;
    }

    public static string? TryReadDataRoot(string json)
        => TryReadStringProperty(json, DataRootPropertyName);

    public static string? TryReadLegacyStorageBaseDirectory(string json)
        => TryReadNestedStringProperty(json, StoragePropertyName, LegacyBaseDirectoryPropertyName);

    private static string? TryReadStringProperty(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryGetProperty(document.RootElement, propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadNestedStringProperty(string json, string parentPropertyName, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryGetProperty(document.RootElement, parentPropertyName, out var parent) ||
                parent.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return TryGetProperty(parent, propertyName, out var property) &&
                   property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) ||
                property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                propertyValue = property.Value;
                return true;
            }
        }

        propertyValue = default;
        return false;
    }
}
