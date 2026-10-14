using System.Text.Json.Serialization;

namespace Meridian.Contracts.Accounting.Lots;

/// <summary>
/// Economic direction is independent of quantity. Both long and short lots retain non-negative
/// original/open quantities and carrying amounts. Long is zero to preserve historical wire shapes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OpenLotDirection>))]
public enum OpenLotDirection
{
    Long = 0,
    Short = 1
}
