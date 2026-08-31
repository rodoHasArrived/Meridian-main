using System.Text.Json;

namespace Meridian.FinancialOperations.Reconciliation.Connectors;

public sealed record StatementCanonicalEvidenceEnvelope(
    string RelativePath,
    StatementCanonicalEvidenceArtifact Artifact);

/// <summary>Reads retained connector evidence for shared operator read models.</summary>
public sealed class StatementCanonicalEvidenceReader(string dataRoot)
{
    private readonly string _root = Path.Combine(dataRoot, "reconciliation", "statement-connector-imports");

    public async Task<IReadOnlyList<StatementCanonicalEvidenceEnvelope>> ListAsync(
        CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
            return [];

        var results = new List<StatementCanonicalEvidenceEnvelope>();
        foreach (var path in Directory.EnumerateFiles(_root, "canonical-evidence.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var stream = File.OpenRead(path);
                var artifact = await JsonSerializer.DeserializeAsync(
                    stream,
                    StatementCanonicalEvidenceJsonContext.Default.StatementCanonicalEvidenceArtifact,
                    ct).ConfigureAwait(false);
                if (artifact is not null)
                {
                    results.Add(new StatementCanonicalEvidenceEnvelope(
                        Path.GetRelativePath(dataRoot, path).Replace('\\', '/'),
                        artifact));
                }
            }
            catch (JsonException)
            {
                // A corrupt sidecar is ignored here and remains available as retained evidence;
                // the margin read model must never guess from a partially decoded artifact.
            }
            catch (IOException)
            {
                // A concurrent atomic replacement may briefly make the file unavailable.
            }
        }

        return results
            .OrderByDescending(static result => result.Artifact.RetainedAtUtc)
            .ToArray();
    }
}
