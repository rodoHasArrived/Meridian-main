using System.Text.Json.Serialization;

namespace Meridian.Application.Config;

[JsonConverter(typeof(JsonStringEnumConverter<CoordinationMode>))]
public enum CoordinationMode
{
    SingleInstance,
    SharedStorage
}

/// <summary>
/// Configuration for multi-instance coordination and lease ownership.
/// </summary>
public sealed record CoordinationConfig(
    bool Enabled = false,
    CoordinationMode Mode = CoordinationMode.SingleInstance,
    string? InstanceId = null,
    int LeaseTtlSeconds = 30,
    int RenewIntervalSeconds = 10,
    int TakeoverDelaySeconds = 5,
    string? RootPath = null)
{
    public bool IsSharedStorageEnabled => Enabled && Mode == CoordinationMode.SharedStorage;

    public string GetResolvedInstanceId()
        => string.IsNullOrWhiteSpace(InstanceId)
            ? Environment.MachineName
            : InstanceId.Trim();

    public string GetResolvedRootPath(string dataRoot)
    {
        var configured = string.IsNullOrWhiteSpace(RootPath)
            ? Path.Combine(dataRoot, "_coordination")
            : RootPath.Trim();

        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(dataRoot, configured);
    }
}
