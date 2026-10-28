using Meridian.Contracts.Configuration;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Reads recent historical-backfill provider configuration audit entries.
/// </summary>
public interface IBackfillProviderConfigAuditReader
{
    IReadOnlyList<ProviderConfigAuditEntryDto> GetAuditLog(int maxEntries = 100);
}
