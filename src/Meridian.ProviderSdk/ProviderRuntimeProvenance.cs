using System.Runtime.CompilerServices;
using Meridian.Contracts.Operations;
using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>Binds safe runtime identity to the adapter instance constructed from that connection.</summary>
public static class ProviderRuntimeProvenance
{
    private static readonly ConditionalWeakTable<object, Connection> Connections = new();

    public static T Attach<T>(T adapter, ICredentialContext context) where T : class
    {
        var id = context.ProvenanceConnectionId;
        if (id is not { Length: 35 } || !id.StartsWith("connection-", StringComparison.Ordinal) ||
            !id.Skip("connection-".Length).All(char.IsAsciiHexDigitLower))
            id = null;
        Connections.Add(adapter, new(id, ProviderProvenanceLabels.Environment(context.CredentialEnvironment),
            context.ProvenanceTenantId, context.ProvenanceRuntimeRevision));
        return adapter;
    }

    public static Connection? Find(object adapter) => Connections.TryGetValue(adapter, out var value) ? value : null;

    public sealed record Connection(string? Id, string Environment, string? TenantId, string? RuntimeRevision);
}
