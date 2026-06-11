using Meridian.Wpf.Models;

namespace Meridian.Wpf.Services;

internal static class WorkstationOperatingContextScopeResolver
{
    public static Guid? ResolveFundAccountId(WorkstationOperatingContext? context)
    {
        if (context is null)
        {
            return null;
        }

        var accountId = context.AccountId;
        if (string.IsNullOrWhiteSpace(accountId) &&
            context.ScopeKind == OperatingContextScopeKind.Account)
        {
            accountId = context.ScopeId;
        }

        return Guid.TryParse(accountId, out var parsed)
            ? parsed
            : null;
    }

    public static string? ResolveFundAccountIdString(WorkstationOperatingContext? context)
        => ResolveFundAccountId(context)?.ToString("D");
}
