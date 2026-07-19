using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Reporting;

internal static class CertifiedReportingSnapshotBuilder
{
    private const string SchemaVersion = "meridian.reporting.snapshot.v1";
    private const string HashAlgorithm = "SHA-256";
    private const string LegacySourceKind = "legacy-in-memory-ledger";

    public static CertifiedReportingSnapshot Build(
        string fundId,
        DateTimeOffset asOf,
        ReportKind reportKind,
        ReportingSnapshotSourceContext? sourceContext,
        IReadOnlyList<ReportingSnapshotRowInput> rowInputs,
        int ledgerEntryCount,
        int ledgerLineCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundId);
        ArgumentNullException.ThrowIfNull(rowInputs);

        var source = NormalizeSource(sourceContext);
        var orderedInputs = rowInputs
            .OrderBy(static row => row.AccountType, StringComparer.Ordinal)
            .ThenBy(static row => row.AccountName, StringComparer.Ordinal)
            .ThenBy(static row => row.Symbol, StringComparer.Ordinal)
            .ThenBy(static row => BuildDimensionsKey(row.Dimensions), StringComparer.Ordinal)
            .ToArray();
        var blockers = BuildCertificationBlockers(source, orderedInputs);
        var rows = orderedInputs.Select(BuildRow).ToArray();
        var hash = ComputeHash(
            fundId,
            asOf,
            reportKind,
            source,
            orderedInputs,
            ledgerEntryCount,
            ledgerLineCount,
            blockers);
        var referenceCount = orderedInputs
            .Select(static row => row.SecurityReference?.Detail.SecurityId)
            .Where(static securityId => securityId.HasValue)
            .Select(static securityId => securityId!.Value)
            .Distinct()
            .Count();
        var status = blockers.Count == 0
            ? ReportingSnapshotCertificationStatus.Certifiable
            : ReportingSnapshotCertificationStatus.NonCertifiable;
        var receipt = new ReportingSnapshotReceipt(
            SnapshotId: $"reporting-snapshot-{hash[..24].ToLowerInvariant()}",
            ContentHash: hash,
            HashAlgorithm,
            SchemaVersion,
            status,
            source.SourceKind,
            source.SourceCheckpoint,
            ledgerEntryCount,
            ledgerLineCount,
            referenceCount,
            blockers);

        return new CertifiedReportingSnapshot(fundId, asOf, reportKind, rows, receipt);
    }

    private static ReportingSnapshotSourceContext NormalizeSource(ReportingSnapshotSourceContext? source)
    {
        if (source is null)
        {
            return new ReportingSnapshotSourceContext(LegacySourceKind, IsAuthoritative: false);
        }

        var sourceKind = string.IsNullOrWhiteSpace(source.SourceKind)
            ? "unspecified"
            : source.SourceKind.Trim();
        var checkpoint = string.IsNullOrWhiteSpace(source.SourceCheckpoint)
            ? null
            : source.SourceCheckpoint.Trim();
        return source with { SourceKind = sourceKind, SourceCheckpoint = checkpoint };
    }

    private static IReadOnlyList<string> BuildCertificationBlockers(
        ReportingSnapshotSourceContext source,
        IReadOnlyList<ReportingSnapshotRowInput> rows)
    {
        var blockers = new HashSet<string>(StringComparer.Ordinal);
        if (!source.IsAuthoritative)
        {
            blockers.Add($"ledger-source-non-authoritative:{source.SourceKind}");
        }

        if (string.IsNullOrWhiteSpace(source.SourceCheckpoint))
        {
            blockers.Add($"ledger-source-checkpoint-missing:{source.SourceKind}");
        }

        foreach (var row in rows.Where(static row => !string.IsNullOrWhiteSpace(row.Symbol)))
        {
            var symbol = row.Symbol!.Trim();
            if (row.SecurityReference is null)
            {
                blockers.Add($"security-master-missing:{symbol}");
                continue;
            }

            if (row.SecurityReference.ResolutionMode != SecurityMasterReportingResolutionMode.HistoricalEvent)
            {
                blockers.Add(
                    $"security-master-non-historical:{symbol}:{row.SecurityReference.ResolutionMode}");
            }

            if (row.SecurityReference.EconomicDefinition is null)
            {
                blockers.Add($"security-master-economic-definition-missing:{symbol}");
            }
        }

        return blockers.OrderBy(static blocker => blocker, StringComparer.Ordinal).ToArray();
    }

    private static EnrichedLedgerRow BuildRow(ReportingSnapshotRowInput input)
    {
        var detail = input.SecurityReference?.Detail;
        var economicDefinition = input.SecurityReference?.EconomicDefinition;
        var primaryIdentifier = economicDefinition?.Identifiers
            .FirstOrDefault(static identifier => identifier.IsPrimary);

        return new EnrichedLedgerRow(
            AccountName: input.AccountName,
            AccountType: input.AccountType,
            Symbol: input.Symbol,
            Currency: detail?.Currency,
            AssetClass: detail?.AssetClass,
            PrimaryIdentifierKind: primaryIdentifier?.Kind.ToString(),
            PrimaryIdentifierValue: primaryIdentifier?.Value,
            SubType: economicDefinition?.SubType,
            AssetFamily: economicDefinition?.AssetFamily,
            IssuerType: economicDefinition?.IssuerType,
            RiskCountry: economicDefinition?.RiskCountry,
            LookupQuality: ResolveLookupQuality(detail, economicDefinition, primaryIdentifier),
            DisplayName: detail?.DisplayName,
            NetBalance: input.NetBalance,
            Dimensions: input.Dimensions);
    }

    private static string ResolveLookupQuality(
        SecurityDetailDto? detail,
        SecurityEconomicDefinitionRecord? economicDefinition,
        SecurityIdentifierDto? primaryIdentifier)
    {
        if (detail is null)
        {
            return "missing";
        }

        if (economicDefinition is null)
        {
            return "partial";
        }

        var hasPrimaryIdentifier = primaryIdentifier is not null
                                   && !string.IsNullOrWhiteSpace(primaryIdentifier.Value);
        var hasGovernanceDimensions =
            !string.IsNullOrWhiteSpace(economicDefinition.SubType)
            && !string.IsNullOrWhiteSpace(economicDefinition.AssetFamily)
            && !string.IsNullOrWhiteSpace(economicDefinition.IssuerType)
            && !string.IsNullOrWhiteSpace(economicDefinition.RiskCountry);

        return hasPrimaryIdentifier && hasGovernanceDimensions
            ? "resolved"
            : "partial";
    }

    private static string ComputeHash(
        string fundId,
        DateTimeOffset asOf,
        ReportKind reportKind,
        ReportingSnapshotSourceContext source,
        IReadOnlyList<ReportingSnapshotRowInput> rows,
        int ledgerEntryCount,
        int ledgerLineCount,
        IReadOnlyList<string> blockers)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", SchemaVersion);
            writer.WriteString("fundId", fundId);
            WriteTimestamp(writer, "asOf", asOf);
            writer.WriteString("reportKind", reportKind.ToString());
            writer.WriteStartObject("source");
            writer.WriteString("kind", source.SourceKind);
            WriteNullableString(writer, "checkpoint", source.SourceCheckpoint);
            writer.WriteBoolean("authoritative", source.IsAuthoritative);
            writer.WriteEndObject();
            writer.WriteNumber("ledgerEntryCount", ledgerEntryCount);
            writer.WriteNumber("ledgerLineCount", ledgerLineCount);
            writer.WriteStartArray("rows");
            foreach (var row in rows)
            {
                WriteRow(writer, row);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("certificationBlockers");
            foreach (var blocker in blockers)
            {
                writer.WriteStringValue(blocker);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteRow(Utf8JsonWriter writer, ReportingSnapshotRowInput row)
    {
        writer.WriteStartObject();
        writer.WriteString("accountName", row.AccountName);
        writer.WriteString("accountType", row.AccountType);
        WriteNullableString(writer, "symbol", row.Symbol);
        writer.WriteNumber("netBalance", row.NetBalance);
        WriteDimensions(writer, row.Dimensions);
        WriteSecurityReference(writer, row.SecurityReference);
        writer.WriteEndObject();
    }

    private static void WriteDimensions(Utf8JsonWriter writer, LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            writer.WriteNull("dimensions");
            return;
        }

        writer.WriteStartObject("dimensions");
        WriteNullableString(writer, "fundId", dimensions.FundId);
        WriteNullableString(writer, "entityId", dimensions.EntityId);
        WriteNullableString(writer, "sleeveId", dimensions.SleeveId);
        WriteNullableString(writer, "strategyId", dimensions.StrategyId);
        WriteNullableString(writer, "investorId", dimensions.InvestorId);
        WriteNullableString(writer, "capitalAccountId", dimensions.CapitalAccountId);
        WriteNullableGuid(writer, "instrumentId", dimensions.InstrumentId);
        WriteNullableGuid(writer, "positionId", dimensions.PositionId);
        WriteNullableString(writer, "taxLotId", dimensions.TaxLotId);
        WriteNullableString(writer, "costCenterId", dimensions.CostCenterId);
        WriteNullableString(writer, "counterpartyId", dimensions.CounterpartyId);
        WriteNullableString(writer, "organizationId", dimensions.OrganizationId);
        WriteNullableString(writer, "portfolioId", dimensions.PortfolioId);
        WriteNullableString(writer, "bookId", dimensions.BookId);
        WriteNullableString(writer, "accountId", dimensions.AccountId);
        WriteNullableString(writer, "customerId", dimensions.CustomerId);
        WriteNullableString(writer, "vendorId", dimensions.VendorId);
        WriteNullableString(writer, "projectId", dimensions.ProjectId);
        writer.WriteStartObject("externalGlDimensions");
        foreach (var pair in dimensions.ExternalGlDimensions.OrderBy(
                     static pair => pair.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteString(pair.Key, pair.Value);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteSecurityReference(
        Utf8JsonWriter writer,
        SecurityMasterReportingReference? reference)
    {
        if (reference is null)
        {
            writer.WriteNull("securityReference");
            return;
        }

        writer.WriteStartObject("securityReference");
        writer.WriteString("resolutionMode", reference.ResolutionMode.ToString());
        WriteTimestamp(writer, "asOf", reference.AsOfUtc);
        WriteNullableNumber(writer, "eventGlobalSequence", reference.EventGlobalSequence);
        WriteNullableNumber(writer, "eventStreamVersion", reference.EventStreamVersion);
        WriteNullableTimestamp(writer, "eventTimestamp", reference.EventTimestamp);
        WriteSecurityDetail(writer, reference.Detail);
        WriteEconomicDefinition(writer, reference.EconomicDefinition);
        writer.WriteEndObject();
    }

    private static void WriteSecurityDetail(Utf8JsonWriter writer, SecurityDetailDto detail)
    {
        writer.WriteStartObject("detail");
        writer.WriteString("securityId", detail.SecurityId);
        writer.WriteString("assetClass", detail.AssetClass);
        writer.WriteString("status", detail.Status.ToString());
        writer.WriteString("displayName", detail.DisplayName);
        writer.WriteString("currency", detail.Currency);
        writer.WriteNumber("version", detail.Version);
        WriteTimestamp(writer, "effectiveFrom", detail.EffectiveFrom);
        WriteNullableTimestamp(writer, "effectiveTo", detail.EffectiveTo);
        writer.WriteString("commonTerms", detail.CommonTerms.GetRawText());
        writer.WriteString("assetSpecificTerms", detail.AssetSpecificTerms.GetRawText());
        WriteIdentifiers(writer, detail.Identifiers);
        WriteAliases(writer, detail.Aliases);
        writer.WriteEndObject();
    }

    private static void WriteEconomicDefinition(
        Utf8JsonWriter writer,
        SecurityEconomicDefinitionRecord? definition)
    {
        if (definition is null)
        {
            writer.WriteNull("economicDefinition");
            return;
        }

        writer.WriteStartObject("economicDefinition");
        writer.WriteString("securityId", definition.SecurityId);
        writer.WriteString("assetClass", definition.AssetClass);
        WriteNullableString(writer, "assetFamily", definition.AssetFamily);
        writer.WriteString("subType", definition.SubType);
        writer.WriteString("typeName", definition.TypeName);
        WriteNullableString(writer, "issuerType", definition.IssuerType);
        WriteNullableString(writer, "riskCountry", definition.RiskCountry);
        writer.WriteString("status", definition.Status.ToString());
        writer.WriteString("displayName", definition.DisplayName);
        writer.WriteString("currency", definition.Currency);
        writer.WriteNumber("version", definition.Version);
        WriteTimestamp(writer, "effectiveFrom", definition.EffectiveFrom);
        WriteNullableTimestamp(writer, "effectiveTo", definition.EffectiveTo);
        writer.WriteString("classification", definition.Classification.GetRawText());
        writer.WriteString("commonTerms", definition.CommonTerms.GetRawText());
        writer.WriteString("economicTerms", definition.EconomicTerms.GetRawText());
        writer.WriteString("provenance", definition.Provenance.GetRawText());
        WriteNullableString(writer, "legacyAssetClass", definition.LegacyAssetClass);
        WriteNullableString(
            writer,
            "legacyAssetSpecificTerms",
            definition.LegacyAssetSpecificTerms?.GetRawText());
        WriteIdentifiers(writer, definition.Identifiers);
        writer.WriteEndObject();
    }

    private static void WriteIdentifiers(
        Utf8JsonWriter writer,
        IReadOnlyList<SecurityIdentifierDto> identifiers)
    {
        writer.WriteStartArray("identifiers");
        foreach (var identifier in identifiers
                     .OrderBy(static item => item.Kind)
                     .ThenBy(static item => item.Value, StringComparer.Ordinal)
                     .ThenBy(static item => item.Provider, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", identifier.Kind.ToString());
            writer.WriteString("value", identifier.Value);
            writer.WriteBoolean("primary", identifier.IsPrimary);
            WriteTimestamp(writer, "validFrom", identifier.ValidFrom);
            WriteNullableTimestamp(writer, "validTo", identifier.ValidTo);
            WriteNullableString(writer, "provider", identifier.Provider);
            WriteNullableString(writer, "normalizedValue", identifier.NormalizedValue);
            WriteNullableString(writer, "normalizedProvider", identifier.NormalizedProvider);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteAliases(Utf8JsonWriter writer, IReadOnlyList<SecurityAliasDto> aliases)
    {
        writer.WriteStartArray("aliases");
        foreach (var alias in aliases
                     .OrderBy(static item => item.AliasKind, StringComparer.Ordinal)
                     .ThenBy(static item => item.AliasValue, StringComparer.Ordinal)
                     .ThenBy(static item => item.Provider, StringComparer.Ordinal)
                     .ThenBy(static item => item.AliasId))
        {
            writer.WriteStartObject();
            writer.WriteString("aliasId", alias.AliasId);
            writer.WriteString("securityId", alias.SecurityId);
            writer.WriteString("kind", alias.AliasKind);
            writer.WriteString("value", alias.AliasValue);
            WriteNullableString(writer, "provider", alias.Provider);
            writer.WriteString("scope", alias.Scope.ToString());
            WriteNullableString(writer, "reason", alias.Reason);
            writer.WriteString("createdBy", alias.CreatedBy);
            WriteTimestamp(writer, "createdAt", alias.CreatedAt);
            WriteTimestamp(writer, "validFrom", alias.ValidFrom);
            WriteNullableTimestamp(writer, "validTo", alias.ValidTo);
            writer.WriteBoolean("enabled", alias.IsEnabled);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static string BuildDimensionsKey(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return string.Empty;
        }

        return string.Join(
            "|",
            dimensions.FundId,
            dimensions.EntityId,
            dimensions.SleeveId,
            dimensions.StrategyId,
            dimensions.InvestorId,
            dimensions.CapitalAccountId,
            dimensions.InstrumentId?.ToString("D"),
            dimensions.PositionId?.ToString("D"),
            dimensions.TaxLotId,
            dimensions.CostCenterId,
            dimensions.CounterpartyId,
            dimensions.OrganizationId,
            dimensions.PortfolioId,
            dimensions.BookId,
            dimensions.AccountId,
            dimensions.CustomerId,
            dimensions.VendorId,
            dimensions.ProjectId,
            string.Join(
                ";",
                dimensions.ExternalGlDimensions
                    .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(static pair => $"{pair.Key}={pair.Value}")));
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static void WriteNullableGuid(Utf8JsonWriter writer, string propertyName, Guid? value)
    {
        if (value.HasValue)
        {
            writer.WriteString(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static void WriteNullableNumber(Utf8JsonWriter writer, string propertyName, long? value)
    {
        if (value.HasValue)
        {
            writer.WriteNumber(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static void WriteTimestamp(Utf8JsonWriter writer, string propertyName, DateTimeOffset value)
        => writer.WriteString(
            propertyName,
            value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    private static void WriteNullableTimestamp(
        Utf8JsonWriter writer,
        string propertyName,
        DateTimeOffset? value)
    {
        if (value.HasValue)
        {
            WriteTimestamp(writer, propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }
}
