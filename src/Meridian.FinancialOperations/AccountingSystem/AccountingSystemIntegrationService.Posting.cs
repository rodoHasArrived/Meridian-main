using System.Text.Json;
using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.AccountingSystem;

public sealed partial class AccountingSystemIntegrationService
{
    internal async Task<IReadOnlyList<ExternalGlMappingProfileDto>> ListPostingMappingsAsync(
        Guid bookId, string tenant, string company, string fund, ExternalGlDestination destination, CancellationToken ct)
    {
        if (_ledgerJournalStore is not PostgresLedgerJournalStore postgres)
            return [];

        var versions = await postgres.ReadExternalGlMappingProfilesAsync(bookId, tenant, company, fund,
            destination.ProviderId, null, ct).ConfigureAwait(false);
        return versions.Select(ReadRetainedMappingProfile).Select(row => row.Profile)
            .Where(profile => profile.CertificationState == AccountingCertificationStateDto.Certified &&
                profile.CertifiedPostingDestination == destination)
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal async Task<ExternalGlMappingVersion> GetPostingMappingAsync(
        ExternalGlPostingScope scope, ExternalGlDestination destination, string profileId, CancellationToken ct)
    {
        if (_ledgerJournalStore is not PostgresLedgerJournalStore postgres)
            throw new InvalidOperationException("External GL posting requires a durable certified mapping.");

        var versions = await postgres.ReadExternalGlMappingProfilesAsync(
            scope.LedgerBookId, scope.TenantId, scope.CompanyId, scope.FundProfileId,
            destination.ProviderId, profileId, ct).ConfigureAwait(false);
        if (versions.SingleOrDefault() is not { } content)
            throw new InvalidOperationException("Select a retained certified mapping for this provider, fund and ledger book.");
        var retained = ReadRetainedMappingProfile(content);
        if (retained.Profile.CertificationState != AccountingCertificationStateDto.Certified ||
            retained.Profile.CertifiedPostingDestination != destination || retained.EvidenceLinks.Count == 0)
            throw new InvalidOperationException("Certify the selected mapping profile for this exact external destination before preparing an export.");

        var profile = retained.Profile with
        {
            AccountMappings = new SortedDictionary<string, string>(
                retained.Profile.AccountMappings.ToDictionary(), StringComparer.OrdinalIgnoreCase),
            DimensionMappings = retained.Profile.DimensionMappings
                .OrderBy(row => row.ProfileId, StringComparer.Ordinal)
                .Select(row => row with
                {
                    MeridianDimensions = CanonicalPostingDimensions(row.MeridianDimensions),
                    ExternalDimensions = CanonicalPostingDimensions(row.ExternalDimensions)
                }).ToArray()
        };
        var evidence = retained.EvidenceLinks.Order(StringComparer.Ordinal).ToArray();
        var version = Sha256Digest.Compute(JsonSerializer.SerializeToUtf8Bytes(new { destination, profile, retained.Actor, evidence }));
        return new ExternalGlMappingVersion(version, profile, retained.Actor, evidence);
    }

    private static ScopedExternalGlMappingProfile ReadRetainedMappingProfile(string json) =>
        JsonSerializer.Deserialize<ScopedExternalGlMappingProfile>(json, JsonOptions)
        ?? throw new InvalidDataException("The retained external GL mapping profile is unreadable.");

    internal static LedgerDimensionSetDto CanonicalPostingDimensions(LedgerDimensionSetDto dimensions) =>
        dimensions with
        {
            ExternalGlDimensions = new SortedDictionary<string, string>(
                dimensions.ExternalGlDimensions.ToDictionary(), StringComparer.OrdinalIgnoreCase)
        };
}
