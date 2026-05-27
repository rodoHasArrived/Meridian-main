namespace Meridian.Application.Exceptions;

/// <summary>
/// Exception raised when an asset class is not supported by a requested workflow.
/// </summary>
public sealed class UnsupportedAssetClassException : MeridianException
{
    public UnsupportedAssetClassException(string assetClass)
        : base($"Asset class '{assetClass}' is not supported by the UFL projection rebuild pipeline.")
    {
        AssetClass = assetClass;
    }

    public string AssetClass { get; }
}
