using Meridian.Contracts.Configuration;

namespace Meridian.Ui.Shared.Services;

public interface IBackfillProviderConfigAuditReader
{
    IReadOnlyList<ProviderConfigAuditEntryDto> GetAuditLog(int maxEntries = 100);
}