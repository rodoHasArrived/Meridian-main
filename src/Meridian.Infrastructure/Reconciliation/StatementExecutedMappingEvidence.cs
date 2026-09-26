using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Domain.Reconciliation;

namespace Meridian.Infrastructure.Reconciliation;

internal static class StatementExecutedMappingEvidence
{
    public static string? Capture(BrokerStatementImportRequest request, string parserRevision)
    {
        if (request.ExecutedMappingFingerprint is { } proof && !Sha256Digest.IsWellFormed(proof))
            throw new InvalidDataException("Executed statement mapping evidence must be a SHA-256 digest.");
        if (!string.IsNullOrWhiteSpace(request.CanonicalSourcePath) && request.ExecutedMappingFingerprint is null)
            return null;
        return Sha256Digest.ComputeUtf8(JsonSerializer.Serialize(new
        {
            ParserRevision = parserRevision,
            request.MappingProfileId,
            Upstream = request.ExecutedMappingFingerprint?.ToLowerInvariant()
        }));
    }
}
