using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Application.SecurityMaster;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.ReferenceData.SecurityMaster;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Http;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

// JSON readers and small formatting helpers shared by the other parts.
public sealed partial class SecurityMasterWorkbenchQueryService
{
    private static string? TryGetJsonString(JsonElement element, string propertyName)
        => TryGetPropertyCaseInsensitive(element, propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? TryGetJsonDateTimeOffset(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTimeOffset.TryParse(property.GetString(), out var parsed)
            ? parsed
            : null;
    }

    private static int? TryGetJsonInt(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        // CommonTerms may encode numeric fields as strings in some older records.
        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? TryGetJsonDecimal(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static decimal? TryGetNestedJsonDecimal(JsonElement? element, string objectName, string propertyName)
    {
        if (!element.HasValue
            || !TryGetPropertyCaseInsensitive(element.Value, objectName, out var nested)
            || nested.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return TryGetJsonDecimal(nested, propertyName);
    }

    private static DateOnly? TryGetNestedJsonDateOnly(JsonElement? element, string objectName, string propertyName)
    {
        if (!element.HasValue
            || !TryGetPropertyCaseInsensitive(element.Value, objectName, out var nested)
            || nested.ValueKind != JsonValueKind.Object
            || !TryGetPropertyCaseInsensitive(nested, propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateOnly.TryParse(property.GetString(), out var parsed) ? parsed : null;
    }

    private static JsonElement? TryGetNestedJsonElement(JsonElement? element, string objectName, string propertyName)
    {
        if (!element.HasValue
            || !TryGetPropertyCaseInsensitive(element.Value, objectName, out var nested)
            || nested.ValueKind != JsonValueKind.Object
            || !TryGetPropertyCaseInsensitive(nested, propertyName, out var property))
        {
            return null;
        }

        return property;
    }

    private static DateOnly? TryGetJsonDateOnly(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(element, propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateOnly.TryParse(property.GetString(), out var parsed) ? parsed : null;
    }

    private static string? FormatNullableDecimal(decimal? value)
        => value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : null;

    private static string FormatLotSizeSuffix(decimal? lotSize)
        => lotSize is > 0m
            ? $" (lot size {lotSize:0.########})"
            : string.Empty;

    private static SecurityAssetClassDescriptor GetAssetCapability(string assetClass)
        => SecurityAssetClassCatalog.GetOrDefault(assetClass);

    private static bool IsIdentifierActive(DateTimeOffset validFrom, DateTimeOffset? validTo, DateTimeOffset asOf)
        => validFrom <= asOf && (!validTo.HasValue || validTo.Value > asOf);

    private static string NormalizeAliasValue(SecurityAliasDto alias)
        => SecurityIdentifierNormalizer.NormalizeAliasValue(alias.AliasKind, alias.AliasValue);
}
