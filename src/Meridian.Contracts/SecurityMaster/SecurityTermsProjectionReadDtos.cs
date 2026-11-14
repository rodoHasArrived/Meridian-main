using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

/// <summary>
/// One security's relational terms projection, read generically from the projection registry
/// rather than through a per-class typed row. Served for any registry-projected asset class,
/// including the classes that have no typed reader. Column names are the SQL column names; the
/// column set and order are the registry descriptor's, so the shape follows the table.
/// </summary>
/// <param name="SecurityId">The security the rows belong to.</param>
/// <param name="AssetClass">The canonical asset class the projection was read for.</param>
/// <param name="TableName">The parent projection table (unqualified).</param>
/// <param name="Columns">The parent row: column name to value, in descriptor order.</param>
/// <param name="ChildTables">Each child (one-to-many) table's rows for the security, in ordinal order.</param>
/// <param name="SatelliteTables">Each satellite (one-to-one) table's row, or a null row when the security has none.</param>
public sealed record SecurityTermsProjectionReadDto(
    Guid SecurityId,
    string AssetClass,
    string TableName,
    IReadOnlyDictionary<string, JsonElement> Columns,
    IReadOnlyList<SecurityTermsProjectionChildRowsDto> ChildTables,
    IReadOnlyList<SecurityTermsProjectionSatelliteRowDto> SatelliteTables);

/// <summary>The rows one child projection table holds for a security, in ordinal order.</summary>
public sealed record SecurityTermsProjectionChildRowsDto(
    string TableName,
    IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows);

/// <summary>The row one satellite projection table holds for a security, or null when it holds none.</summary>
public sealed record SecurityTermsProjectionSatelliteRowDto(
    string TableName,
    IReadOnlyDictionary<string, JsonElement>? Row);

/// <summary>Provides source-generated JSON metadata for the generic registry projection read.</summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(SecurityTermsProjectionReadDto))]
public sealed partial class SecurityTermsProjectionReadJsonContext : JsonSerializerContext;
