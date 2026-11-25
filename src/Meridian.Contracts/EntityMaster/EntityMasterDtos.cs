namespace Meridian.Contracts.EntityMaster;

/// <summary>
/// ADR-033: a legal entity in the entity master, the bounded context shared by Security Master issuers,
/// counterparties and fund structure. Identified by <see cref="EntityId"/>; LEI and CIK are optional
/// external identifiers, each unique when present.
/// </summary>
public sealed record LegalEntityDto(
    Guid EntityId,
    string LegalName,
    string? Lei,
    string? Cik,
    string? Jurisdiction,
    DateTimeOffset RecordedAt,
    string RecordedBy);

/// <summary>Request to register a legal entity.</summary>
public sealed record CreateLegalEntityRequest(
    string LegalName,
    string? Lei,
    string? Cik,
    string? Jurisdiction,
    Guid? EntityId = null);

/// <summary>A direct-parent relationship valid over <c>[ValidFrom, ValidTo)</c>.</summary>
public sealed record EntityParentLinkDto(
    Guid EntityId,
    Guid ParentEntityId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    DateTimeOffset RecordedAt,
    string RecordedBy);

/// <summary>A security's issuer link valid over <c>[ValidFrom, ValidTo)</c>.</summary>
public sealed record SecurityIssuerLinkDto(
    Guid SecurityId,
    Guid IssuerEntityId,
    DateOnly ValidFrom,
    DateOnly? ValidTo,
    DateTimeOffset RecordedAt,
    string RecordedBy);

/// <summary>
/// Entity master persistence (ADR-033). Parent and issuer links carry valid-time windows; a new link
/// for an entity or security closes the open one at the new link's valid-from, so history is kept.
/// </summary>
public interface IEntityMasterStore
{
    Task<LegalEntityDto> CreateEntityAsync(CreateLegalEntityRequest request, string actor, CancellationToken ct = default);

    Task<LegalEntityDto?> GetEntityAsync(Guid entityId, CancellationToken ct = default);

    Task<LegalEntityDto?> FindByLeiAsync(string lei, CancellationToken ct = default);

    Task<LegalEntityDto?> FindByCikAsync(string cik, CancellationToken ct = default);

    Task<EntityParentLinkDto> SetParentAsync(Guid entityId, Guid parentEntityId, DateOnly validFrom, string actor, CancellationToken ct = default);

    Task<EntityParentLinkDto?> GetParentAsync(Guid entityId, DateOnly asOf, CancellationToken ct = default);

    Task<SecurityIssuerLinkDto> LinkIssuerAsync(Guid securityId, Guid issuerEntityId, DateOnly validFrom, string actor, CancellationToken ct = default);

    Task<SecurityIssuerLinkDto?> GetIssuerAsync(Guid securityId, DateOnly asOf, CancellationToken ct = default);

    /// <summary>Follows parent links as of <paramref name="asOf"/> to the top of the hierarchy (bounded depth).</summary>
    async Task<LegalEntityDto?> GetUltimateParentAsync(Guid entityId, DateOnly asOf, CancellationToken ct = default)
    {
        var current = entityId;
        for (var depth = 0; depth < 32; depth++)
        {
            var parent = await GetParentAsync(current, asOf, ct).ConfigureAwait(false);
            if (parent is null)
                return await GetEntityAsync(current, ct).ConfigureAwait(false);
            current = parent.ParentEntityId;
        }

        throw new InvalidOperationException($"The parent hierarchy of entity '{entityId:D}' is deeper than 32 levels or cyclic.");
    }
}

/// <summary>Shared validation for entity-master writes.</summary>
public static class EntityMasterRules
{
    /// <summary>Normalizes an LEI (ISO 17442: 20 alphanumerics); null when absent.</summary>
    public static string? NormalizeLei(string? lei)
    {
        if (string.IsNullOrWhiteSpace(lei))
            return null;
        var normalized = lei.Trim().ToUpperInvariant();
        if (normalized.Length != 20 || !normalized.All(char.IsLetterOrDigit))
            throw new ArgumentException($"LEI '{lei}' must be 20 letters or digits (ISO 17442).", nameof(lei));
        return normalized;
    }

    /// <summary>Normalizes a CIK to its 10-digit zero-padded form; null when absent.</summary>
    public static string? NormalizeCik(string? cik)
    {
        if (string.IsNullOrWhiteSpace(cik))
            return null;
        var digits = cik.Trim();
        if (!digits.All(char.IsDigit) || digits.Length > 10)
            throw new ArgumentException($"CIK '{cik}' must be at most 10 digits.", nameof(cik));
        return digits.PadLeft(10, '0');
    }
}

/// <summary>
/// ADR-033 issuer-level concentration: gross exposure grouped by the issuer each security is linked to
/// in the entity master, as of a date.
/// </summary>
/// <param name="IssuerEntityId">The direct issuer.</param>
/// <param name="LegalName">The issuer's legal name.</param>
/// <param name="UltimateParentEntityId">The top of the issuer's parent hierarchy as of the date; the issuer itself when it has no parent.</param>
/// <param name="GrossExposure">Sum of the absolute exposure of every position whose security this entity issued.</param>
/// <param name="ShareOfGross">Gross exposure over the report's total gross exposure; 0 when that total is 0.</param>
/// <param name="Symbols">The symbols contributing, ordered.</param>
public sealed record IssuerConcentrationDto(
    Guid IssuerEntityId,
    string LegalName,
    Guid? UltimateParentEntityId,
    decimal GrossExposure,
    decimal ShareOfGross,
    IReadOnlyList<string> Symbols);

/// <summary>
/// Issuer concentration for one exposure report. Exposure whose security is unresolved or has no issuer
/// link is reported as unlinked rather than dropped, so the linked share never overstates coverage.
/// </summary>
public sealed record IssuerConcentrationReport(
    DateOnly AsOf,
    IReadOnlyList<IssuerConcentrationDto> Issuers,
    decimal LinkedGrossExposure,
    decimal UnlinkedGrossExposure,
    IReadOnlyList<string> UnlinkedSymbols);
