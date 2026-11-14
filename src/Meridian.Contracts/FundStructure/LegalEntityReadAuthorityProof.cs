namespace Meridian.Contracts.FundStructure;

/// <summary>Invocation-bound proof of a live PostgreSQL legal-entity read fence.</summary>
/// <remarks>Only the accounting storage assembly can mint a proof; consumers cannot manufacture a fence from request data.</remarks>
public sealed class LegalEntityReadAuthorityProof
{
    private readonly Func<bool> _isActive;
    internal LegalEntityReadAuthorityProof(string host, int port, string database, string schema, Func<bool> isActive)
    {
        Host = host;
        Port = port;
        Database = database;
        Schema = schema;
        _isActive = isActive;
    }
    public string Host { get; }
    public int Port { get; }
    public string Database { get; }
    public string Schema { get; }
    public bool IsActive => _isActive();
}

public interface ILegalEntityReadAuthorityLease : IAsyncDisposable
{
    LegalEntityReadAuthorityProof StructureAuthorityProof { get; }
}
