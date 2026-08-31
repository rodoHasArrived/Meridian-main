using System.Collections.Immutable;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

public enum ReportingDeclaredArtifactKind
{
    Manifest,
    PrimaryOutput,
    Preview,
    CertifiedSourceSchedule,
    SupportingSchedules,
    EvidenceAppendix,
    ReportWriterGrid
}

/// <summary>One exact byte-producing artifact declared by the orchestration manifest.</summary>
public sealed record ReportingDeclaredArtifact(
    string ArtifactId,
    string FileName,
    string ContentType,
    ReportingDeclaredArtifactKind Kind,
    string? GridId = null);

/// <summary>
/// Single source of truth for orchestration declarations and certified artifact production. A
/// manifest may only name artifacts this catalog can deterministically produce.
/// </summary>
public static class ReportingArtifactDeclaration
{
    public static ImmutableArray<ReportingDeclaredArtifact> Build(
        string runId,
        ReportingTemplateMetadata template,
        ReportingRunParametersDto? parameters,
        bool includeCertifiedSourceSchedule = false)
    {
        ArgumentNullException.ThrowIfNull(template);
        var gridIds = (template.ReportWriterGrids ?? [])
            .Where(static grid => !string.IsNullOrWhiteSpace(grid.GridId))
            .GroupBy(static grid => grid.GridId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Key)
            .OrderBy(static gridId => gridId, StringComparer.OrdinalIgnoreCase);
        return BuildCore(
            runId,
            parameters,
            gridIds,
            includeCertifiedSourceSchedule,
            includePreview: true);
    }

    /// <summary>
    /// Reconstructs the same declaration set from a completed manifest. This is the producer/release
    /// path and deliberately avoids a mutable template-catalog lookup after orchestration.
    /// </summary>
    public static ImmutableArray<ReportingDeclaredArtifact> Build(ReportingOutputManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.RunId);
        var normalizedRunId = manifest.RunId.Trim();
        var grids = manifest.ReportWriterGrids.IsDefault
            ? []
            : manifest.ReportWriterGrids
                .Select(static grid => grid.GridId)
                .Where(static gridId => !string.IsNullOrWhiteSpace(gridId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static gridId => gridId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        return BuildCore(
            manifest.RunId,
            manifest.ResolvedParameters,
            grids,
            includeCertifiedSourceSchedule: manifest.AuthoritativeSource is not null,
            includePreview: !manifest.Artifacts.IsDefault
                && manifest.Artifacts.Contains(
                    $"{normalizedRunId}.preview.json",
                    StringComparer.Ordinal));
    }

    private static ImmutableArray<ReportingDeclaredArtifact> BuildCore(
        string runId,
        ReportingRunParametersDto? parameters,
        IEnumerable<string> gridIds,
        bool includeCertifiedSourceSchedule,
        bool includePreview)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var normalizedRunId = runId.Trim();
        var output = parameters?.OutputFormat ?? ReportingOutputFormatDto.Pdf;
        var artifacts = ImmutableArray.CreateBuilder<ReportingDeclaredArtifact>();
        artifacts.Add(new ReportingDeclaredArtifact(
            $"{normalizedRunId}.manifest.json",
            $"{normalizedRunId}.manifest.json",
            "application/json",
            ReportingDeclaredArtifactKind.Manifest));

        foreach (var primaryOutput in BuildPrimaryOutputs(normalizedRunId, output))
        {
            artifacts.Add(primaryOutput);
        }

        if (includePreview)
        {
            artifacts.Add(new ReportingDeclaredArtifact(
                $"{normalizedRunId}.preview.json",
                $"{normalizedRunId}.preview.json",
                "application/vnd.meridian.reporting-preview+json",
                ReportingDeclaredArtifactKind.Preview));
        }

        if (includeCertifiedSourceSchedule)
        {
            artifacts.Add(new ReportingDeclaredArtifact(
                $"{normalizedRunId}.certified-source.csv",
                $"{normalizedRunId}.certified-source.csv",
                "text/csv",
                ReportingDeclaredArtifactKind.CertifiedSourceSchedule));
        }

        if (parameters?.IncludeSupportingSchedules == true)
        {
            artifacts.Add(new ReportingDeclaredArtifact(
                $"{normalizedRunId}.supporting-schedules.csv",
                $"{normalizedRunId}.supporting-schedules.csv",
                "text/csv",
                ReportingDeclaredArtifactKind.SupportingSchedules));
        }

        if (parameters?.IncludeEvidenceAppendix == true)
        {
            artifacts.Add(new ReportingDeclaredArtifact(
                $"{normalizedRunId}.evidence-appendix.json",
                $"{normalizedRunId}.evidence-appendix.json",
                "application/vnd.meridian.reporting-evidence+json",
                ReportingDeclaredArtifactKind.EvidenceAppendix));
        }

        foreach (var gridIdValue in gridIds)
        {
            var gridId = gridIdValue.Trim();
            artifacts.Add(new ReportingDeclaredArtifact(
                $"report-writer://{normalizedRunId}/grids/{gridId}",
                $"{normalizedRunId}.grid.{NormalizeFileToken(gridId)}.json",
                "application/vnd.meridian.report-writer-grid+json",
                ReportingDeclaredArtifactKind.ReportWriterGrid,
                gridId));
        }

        var built = artifacts.ToImmutable();
        if (built.Select(static artifact => artifact.ArtifactId)
            .Distinct(StringComparer.Ordinal)
            .Count() != built.Length)
        {
            throw new InvalidOperationException(
                $"Reporting run '{normalizedRunId}' produced duplicate artifact declarations.");
        }
        if (built.Select(static artifact => artifact.FileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() != built.Length)
        {
            throw new InvalidOperationException(
                $"Reporting run '{normalizedRunId}' produced colliding artifact file names after normalization.");
        }

        return built;
    }

    private static IEnumerable<ReportingDeclaredArtifact> BuildPrimaryOutputs(
        string normalizedRunId,
        ReportingOutputFormatDto output)
    {
        if (output == ReportingOutputFormatDto.ClientPackage)
        {
            yield return Pdf(normalizedRunId);
            yield return Xlsx(normalizedRunId);
            yield break;
        }

        yield return output switch
        {
            ReportingOutputFormatDto.Xlsx => Xlsx(normalizedRunId),
            ReportingOutputFormatDto.Csv => new ReportingDeclaredArtifact(
                $"{normalizedRunId}.csv",
                $"{normalizedRunId}.csv",
                "text/csv",
                ReportingDeclaredArtifactKind.PrimaryOutput),
            ReportingOutputFormatDto.EvidenceVault => new ReportingDeclaredArtifact(
                $"{normalizedRunId}.evidence-vault.json",
                $"{normalizedRunId}.evidence-vault.json",
                "application/vnd.meridian.reporting-evidence+json",
                ReportingDeclaredArtifactKind.PrimaryOutput),
            _ => Pdf(normalizedRunId)
        };
    }

    private static ReportingDeclaredArtifact Pdf(string normalizedRunId) => new(
        $"{normalizedRunId}.pdf",
        $"{normalizedRunId}.pdf",
        "application/pdf",
        ReportingDeclaredArtifactKind.PrimaryOutput);

    private static ReportingDeclaredArtifact Xlsx(string normalizedRunId) => new(
        $"{normalizedRunId}.xlsx",
        $"{normalizedRunId}.xlsx",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ReportingDeclaredArtifactKind.PrimaryOutput);

    private static string NormalizeFileToken(string value)
    {
        var token = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(static character => char.IsAsciiLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');
        return string.IsNullOrWhiteSpace(token) ? "grid" : token;
    }
}
