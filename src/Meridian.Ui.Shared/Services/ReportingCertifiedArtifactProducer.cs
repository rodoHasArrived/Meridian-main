using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Export;
using Meridian.Ui.Shared.Serialization;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Hash and byte-length declaration embedded in the retained manifest. The manifest entry itself
/// intentionally has no hash/length to avoid a circular self-hash; its exact hash and length are
/// authoritatively carried by the immutable package catalog.
/// </summary>
public sealed record ReportingRetainedManifestArtifactDescriptor(
    string ArtifactId,
    string FileName,
    string ContentType,
    long? ByteLength,
    string? ContentHashSha256);

/// <summary>
/// Canonical manifest bytes retained alongside every governed reporting package. This document is
/// deliberately sufficient to validate release without consulting the mutable orchestration store.
/// </summary>
public sealed record ReportingRetainedManifestDocument(
    string SchemaVersion,
    string RunId,
    string SeriesId,
    string TemplateId,
    VersionedReportTemplateIdDto ResolvedTemplate,
    DateOnly AsOfDate,
    string ParametersCanonicalJson,
    string ParametersHash,
    ReportingOperationalScope Scope,
    ReportingAccessScope Access,
    ReportingCertifiedSnapshotScope Snapshot,
    ReportingAuthoritativeSourceCheckpoint AuthoritativeSource,
    ReportingRunReadinessDto Readiness,
    ImmutableArray<ReportingSectionManifest> Sections,
    int CertifiedDatasetRowCount,
    string CertifiedDatasetHashSha256,
    ImmutableArray<ReportingRetainedManifestArtifactDescriptor> Artifacts);

/// <summary>
/// Production deterministic artifact implementation. Every byte is derived from the completed
/// certified manifest and, for a partners-capital primary document, an exact checkpoint-bound
/// canonical ledger presentation. Output uses stable ordering and is emitted exactly as declared
/// according to <see cref="ReportingRunParametersDto.OutputFormat"/> and the optional
/// evidence/schedule flags.
/// </summary>
public sealed class DeterministicReportingCertifiedArtifactProducer : IReportingCertifiedArtifactProducer
{
    public const string RetainedManifestSchemaVersion = "meridian.reporting.retained-manifest.v1";

    private const string DurableLedgerSourceKind = "durable-ledger-journal";
    private const int PdfCharactersPerLine = 86;
    private const int PdfLinesPerPage = 48;

    private readonly IReportingPrimaryDocumentRenderer? _primaryRenderer;
    private readonly IReportingCertifiedLedgerPresentationSource? _ledgerPresentationSource;

    /// <summary>
    /// Constructs the producer with an optional client-grade primary-document renderer and an
    /// optional exact canonical-ledger-presentation source. When the renderer is null
    /// (dependency-free hosts and the existing byte-exact tests) the built-in plain-text PDF/XLSX
    /// rendering is used. A capital-account primary document requires both a presentation-capable
    /// renderer and a checkpoint-bound canonical ledger report pack; it never falls back to
    /// recalculating partners' capital from certified display rows. Capital-account PDF, XLSX, and
    /// ClientPackage outputs all select bytes from that same canonical pair. Every other artifact
    /// (CSV, evidence vault, preview, manifest, grids) is unaffected by this choice.
    /// </summary>
    public DeterministicReportingCertifiedArtifactProducer(
        IReportingPrimaryDocumentRenderer? primaryRenderer = null,
        IReportingCertifiedLedgerPresentationSource? ledgerPresentationSource = null)
    {
        _primaryRenderer = primaryRenderer;
        _ledgerPresentationSource = ledgerPresentationSource;
    }

    public async ValueTask<ReportingGovernedArtifactProduction> ProduceAsync(
        ReportingOutputManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateManifest(manifest);
        var certifiedLedgerPresentation = await ResolveCertifiedLedgerPresentationAsync(
                manifest,
                cancellationToken)
            .ConfigureAwait(false);

        var declarations = ReportingArtifactDeclaration.Build(manifest);
        var declaredIds = declarations.Select(static artifact => artifact.ArtifactId).ToArray();
        if (!declaredIds.SequenceEqual(manifest.Artifacts, StringComparer.Ordinal))
        {
            throw new ReportingGovernanceException(
                $"Certified manifest '{manifest.RunId}' artifact declaration drifted before byte production.");
        }
        var canonicalLedgerDocuments = RenderCertifiedLedgerDocuments(
            manifest,
            declarations,
            certifiedLedgerPresentation);

        var rendered = ImmutableArray.CreateBuilder<ReportingRenderedArtifact>(declarations.Length);
        var descriptors = ImmutableArray.CreateBuilder<ReportingRetainedManifestArtifactDescriptor>(declarations.Length);
        var manifestDeclaration = declarations.Single(static artifact =>
            artifact.Kind == ReportingDeclaredArtifactKind.Manifest);

        foreach (var declaration in declarations.Where(static artifact =>
                     artifact.Kind != ReportingDeclaredArtifactKind.Manifest))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = RenderArtifact(manifest, declaration, canonicalLedgerDocuments);
            if (content.Length == 0)
            {
                throw new ReportingGovernanceException(
                    $"Certified artifact '{declaration.ArtifactId}' rendered no bytes.");
            }

            rendered.Add(new ReportingRenderedArtifact(
                declaration.ArtifactId,
                declaration.FileName,
                declaration.ContentType,
                content));
            descriptors.Add(new ReportingRetainedManifestArtifactDescriptor(
                declaration.ArtifactId,
                declaration.FileName,
                declaration.ContentType,
                content.LongLength,
                Sha256Digest.Compute(content)));
        }

        descriptors.Add(new ReportingRetainedManifestArtifactDescriptor(
            manifestDeclaration.ArtifactId,
            manifestDeclaration.FileName,
            manifestDeclaration.ContentType,
            ByteLength: null,
            ContentHashSha256: null));

        var retainedDocument = new ReportingRetainedManifestDocument(
            RetainedManifestSchemaVersion,
            manifest.RunId,
            NormalizeOptional(manifest.RunSeriesId) ?? manifest.RunId,
            manifest.TemplateId,
            manifest.ResolvedTemplate!,
            manifest.AsOfDate,
            manifest.CertifiedSnapshot!.ParametersCanonicalJson!,
            manifest.CertifiedSnapshot.ParametersHash!,
            manifest.OperationalScope!,
            manifest.ImmutableAccessScope!,
            manifest.CertifiedSnapshot,
            manifest.AuthoritativeSource!,
            manifest.Readiness!,
            manifest.Sections,
            manifest.CertifiedDatasetRows.Length,
            ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows),
            descriptors
                .OrderBy(static descriptor => descriptor.ArtifactId, StringComparer.Ordinal)
                .ToImmutableArray());
        var retainedManifestBytes = JsonSerializer.SerializeToUtf8Bytes(
            retainedDocument,
            ReportingCertifiedArtifactJsonContext.Default.ReportingRetainedManifestDocument);
        rendered.Add(new ReportingRenderedArtifact(
            manifestDeclaration.ArtifactId,
            manifestDeclaration.FileName,
            manifestDeclaration.ContentType,
            retainedManifestBytes));

        var orderedArtifacts = rendered
            .OrderBy(artifact => Array.IndexOf(declaredIds, artifact.ArtifactId))
            .ToImmutableArray();
        var source = manifest.AuthoritativeSource;
        var certification = new ReportingAuthoritativeAsOfCertification(
            manifest.AsOfDate,
            manifest.OperationalScope!,
            manifest.ImmutableAccessScope!,
            manifest.CertifiedSnapshot!,
            source!.CheckpointId,
            source.CheckpointHash,
            IsAuthoritative: true,
            source.EvidenceIds
                .Append($"reconciliation-checkpoint:{manifest.CertifiedSnapshot.ReconciliationCheckpointId}:{manifest.CertifiedSnapshot.ReconciliationCheckpointHash}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static evidence => evidence, StringComparer.Ordinal)
                .ToImmutableArray());

        return new ReportingGovernedArtifactProduction(
            manifest.RunId,
            manifestDeclaration.ArtifactId,
            certification,
            orderedArtifacts);
    }

    public static ReportingRetainedManifestDocument ParseRetainedManifest(
        ReadOnlySpan<byte> retainedBytes)
    {
        if (retainedBytes.IsEmpty)
        {
            throw new ReportingArtifactCatalogIntegrityException("Retained reporting manifest bytes are empty.");
        }

        ReportingRetainedManifestDocument document;
        try
        {
            document = JsonSerializer.Deserialize(
                    retainedBytes,
                    ReportingCertifiedArtifactJsonContext.Default.ReportingRetainedManifestDocument)
                ?? throw new ReportingArtifactCatalogIntegrityException(
                    "Retained reporting manifest deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained reporting manifest is not valid canonical JSON: {exception.Message}");
        }

        if (!string.Equals(document.SchemaVersion, RetainedManifestSchemaVersion, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(document.RunId)
            || string.IsNullOrWhiteSpace(document.SeriesId)
            || string.IsNullOrWhiteSpace(document.TemplateId)
            || document.ResolvedTemplate is null
            || document.Scope is null
            || document.Access is null
            || document.Snapshot is null
            || document.AuthoritativeSource is null
            || document.Readiness is null
            || document.Artifacts.IsDefaultOrEmpty
            || document.Artifacts.Any(static artifact =>
                string.IsNullOrWhiteSpace(artifact.ArtifactId)
                || string.IsNullOrWhiteSpace(artifact.FileName)
                || string.IsNullOrWhiteSpace(artifact.ContentType)
                || artifact.ByteLength is <= 0
                || artifact.ContentHashSha256 is not null && !Sha256Digest.IsWellFormed(artifact.ContentHashSha256))
            || document.Artifacts.Select(static artifact => artifact.ArtifactId)
                .Distinct(StringComparer.Ordinal).Count() != document.Artifacts.Length
            || document.CertifiedDatasetRowCount < 0
            || !Sha256Digest.IsWellFormed(document.CertifiedDatasetHashSha256)
            || !Sha256Digest.IsWellFormed(document.ParametersHash)
            || !string.Equals(
                Sha256Digest.Compute(Encoding.UTF8.GetBytes(document.ParametersCanonicalJson)),
                document.ParametersHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                "Retained reporting manifest is incomplete or failed canonical identity validation.");
        }

        return document;
    }

    private byte[] RenderArtifact(
        ReportingOutputManifest manifest,
        ReportingDeclaredArtifact declaration,
        LedgerClientReportDocumentPackage? canonicalLedgerDocuments) => declaration.Kind switch
        {
            ReportingDeclaredArtifactKind.PrimaryOutput => declaration.ContentType switch
            {
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" =>
                    canonicalLedgerDocuments is { } package
                    ? package.Workbook
                    : _primaryRenderer is { } workbookRenderer
                    ? workbookRenderer.RenderWorkbook(manifest)
                    : RenderXlsx(manifest),
                "text/csv" => RenderPrimaryCsv(manifest),
                "application/vnd.meridian.reporting-evidence+json" => RenderEvidenceVault(manifest),
                "application/pdf" => canonicalLedgerDocuments is { } package
                    ? package.Pdf
                    : _primaryRenderer is { } documentRenderer
                    ? documentRenderer.RenderPdf(manifest)
                    : RenderPdf(manifest),
                _ => throw new ReportingGovernanceException(
                    $"Primary artifact '{declaration.ArtifactId}' declares unsupported content type '{declaration.ContentType}'.")
            },
            ReportingDeclaredArtifactKind.Preview => RenderPreview(manifest),
            ReportingDeclaredArtifactKind.CertifiedSourceSchedule => RenderCertifiedRowsCsv(manifest),
            ReportingDeclaredArtifactKind.SupportingSchedules => RenderSupportingSchedules(manifest),
            ReportingDeclaredArtifactKind.EvidenceAppendix => RenderEvidenceAppendix(manifest),
            ReportingDeclaredArtifactKind.ReportWriterGrid => RenderGrid(manifest, declaration.GridId!),
            _ => throw new ReportingGovernanceException(
                $"Artifact kind '{declaration.Kind}' cannot be rendered outside the retained manifest pass.")
        };

    private async ValueTask<ReportingCertifiedLedgerPresentationInput?> ResolveCertifiedLedgerPresentationAsync(
        ReportingOutputManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!RequiresCertifiedLedgerPresentation(manifest))
        {
            return null;
        }

        if (_primaryRenderer is not IReportingPrimaryDocumentRendererWithLedgerReportPack)
        {
            throw new ReportingGovernanceException(
                $"Capital-account primary document '{manifest.RunId}' is blocked because the configured primary-document renderer cannot consume the exact checkpoint-bound ledger report pack.");
        }

        var input = _ledgerPresentationSource is null
            ? null
            : await _ledgerPresentationSource
                .ResolveExactAsync(manifest, cancellationToken)
                .ConfigureAwait(false);
        if (input is null)
        {
            throw new ReportingGovernanceException(
                $"Capital-account primary document '{manifest.RunId}' is blocked because no exact checkpoint-bound canonical ledger presentation is available. Partners' capital was not recalculated from incomplete certified display rows.");
        }
        ValidateCertifiedLedgerPresentation(manifest, input);
        return input;
    }

    private LedgerClientReportDocumentPackage? RenderCertifiedLedgerDocuments(
        ReportingOutputManifest manifest,
        ImmutableArray<ReportingDeclaredArtifact> declarations,
        ReportingCertifiedLedgerPresentationInput? certifiedLedgerPresentation)
    {
        if (certifiedLedgerPresentation is null)
        {
            return null;
        }

        var primaryDocuments = declarations
            .Where(static artifact => artifact.Kind == ReportingDeclaredArtifactKind.PrimaryOutput)
            .ToArray();
        var outputFormat = manifest.ResolvedParameters!.OutputFormat;
        var pdfCount = primaryDocuments.Count(static artifact =>
            string.Equals(artifact.ContentType, "application/pdf", StringComparison.Ordinal));
        var workbookCount = primaryDocuments.Count(static artifact =>
            string.Equals(
                artifact.ContentType,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                StringComparison.Ordinal));
        var declarationsMatch = outputFormat switch
        {
            ReportingOutputFormatDto.Pdf =>
                primaryDocuments.Length == 1 && pdfCount == 1 && workbookCount == 0,
            ReportingOutputFormatDto.Xlsx =>
                primaryDocuments.Length == 1 && pdfCount == 0 && workbookCount == 1,
            ReportingOutputFormatDto.ClientPackage =>
                primaryDocuments.Length == 2 && pdfCount == 1 && workbookCount == 1,
            _ => false
        };
        if (!declarationsMatch)
        {
            throw new ReportingGovernanceException(
                $"Capital-account primary document '{manifest.RunId}' has declarations that do not match output format '{outputFormat}'.");
        }

        var renderer = (IReportingPrimaryDocumentRendererWithLedgerReportPack)_primaryRenderer!;
        var package = renderer.RenderClientPackage(
            manifest,
            certifiedLedgerPresentation.ReportPack);
        if (package.Pdf.Length == 0 || package.Workbook.Length == 0)
        {
            throw new ReportingGovernanceException(
                $"Capital-account primary document '{manifest.RunId}' did not render the complete canonical PDF/XLSX pair.");
        }

        return package;
    }

    private static void ValidateCertifiedLedgerPresentation(
        ReportingOutputManifest manifest,
        ReportingCertifiedLedgerPresentationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var source = manifest.AuthoritativeSource!;
        var parameters = manifest.ResolvedParameters!;
        var reportPack = input.ReportPack
            ?? throw new ReportingGovernanceException(
                $"Capital-account primary document '{manifest.RunId}' has no canonical ledger report pack.");
        var expectedDatasetHash = ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows);

        if (!string.Equals(source.SourceKind, DurableLedgerSourceKind, StringComparison.Ordinal)
            || !string.Equals(input.SourceCheckpointId, source.CheckpointId, StringComparison.Ordinal)
            || !Sha256Digest.IsWellFormed(input.SourceCheckpointHash)
            || !string.Equals(input.SourceCheckpointHash, source.CheckpointHash, StringComparison.OrdinalIgnoreCase)
            || !Sha256Digest.IsWellFormed(input.CertifiedDatasetHashSha256)
            || !string.Equals(
                input.CertifiedDatasetHashSha256,
                expectedDatasetHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                $"Capital-account primary document '{manifest.RunId}' is blocked because its canonical ledger presentation is not bound to the exact certified source checkpoint and dataset hash.");
        }

        var request = reportPack.Request;
        if (!string.Equals(request.FundId, source.FundId, StringComparison.Ordinal)
            || !string.Equals(request.PeriodId, source.AccountingPeriodId, StringComparison.Ordinal)
            || DateOnly.FromDateTime(request.AsOf.UtcDateTime) != manifest.AsOfDate
            || !string.Equals(
                request.BaseCurrency,
                parameters.PresentationCurrency,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                $"Capital-account primary document '{manifest.RunId}' is blocked because its canonical ledger report pack does not match the certified fund, period, as-of date, and presentation currency.");
        }

        ValidateLedgerReportPackIntegrity(manifest.RunId, reportPack);
        var retainedPresentationEvidence =
            ReportingCertifiedLedgerPresentationBinding.GetSingleEvidenceId(source);
        var resolvedPresentationEvidence =
            ReportingCertifiedLedgerPresentationBinding.BuildEvidenceId(reportPack);
        if (retainedPresentationEvidence is null
            || !string.Equals(
                retainedPresentationEvidence,
                resolvedPresentationEvidence,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                $"Capital-account primary document '{manifest.RunId}' is blocked because its signed ledger presentation checksum is missing from or changed relative to the certified source evidence.");
        }

        var partnersCapital = reportPack.Statements.PartnersCapital
            ?? throw new ReportingGovernanceException(
                $"Capital-account primary document '{manifest.RunId}' is blocked because its canonical ledger report pack has no partners-capital statement.");
        if (partnersCapital.Accounts.Count == 0
            || partnersCapital.PeriodStart != request.PeriodStart
            || partnersCapital.AsOf != request.AsOf
            || !partnersCapital.IsReconciled
            || partnersCapital.AllocationComponentsVariance != 0m
            || partnersCapital.Accounts.Any(static account =>
                account.ReconciliationVariance != 0m
                || account.AllocationComponentsVariance != 0m)
            || partnersCapital.EndingCapital != reportPack.Statements.EndingEquity
            || !reportPack.IsBalanced)
        {
            throw new ReportingGovernanceException(
                $"Capital-account primary document '{manifest.RunId}' is blocked because its canonical partners-capital roll-forward is incomplete, unbalanced, or unreconciled.");
        }
    }

    private static void ValidateLedgerReportPackIntegrity(
        string runId,
        LedgerFinancialReportPack reportPack)
    {
        if (!string.Equals(reportPack.Signature.Algorithm, "SHA256", StringComparison.Ordinal)
            || !Sha256Digest.IsWellFormed(reportPack.Signature.PayloadChecksumSha256)
            || reportPack.Artifacts.Count == 0
            || reportPack.Artifacts.Any(static artifact =>
                artifact is null
                || string.IsNullOrWhiteSpace(artifact.Name)
                || string.IsNullOrWhiteSpace(artifact.ContentType)
                || !Sha256Digest.IsWellFormed(artifact.ChecksumSha256))
            || reportPack.Artifacts
                .Select(static artifact => artifact.Name)
                .Distinct(StringComparer.Ordinal)
                .Count() != reportPack.Artifacts.Count)
        {
            throw new ReportingGovernanceException(
                $"Capital-account primary document '{runId}' is blocked because its canonical ledger report pack signature or artifact declaration is incomplete.");
        }

        foreach (var artifact in reportPack.Artifacts)
        {
            if (!string.Equals(
                    Sha256Digest.Compute(artifact.GetBytes()),
                    artifact.ChecksumSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ReportingGovernanceException(
                    $"Capital-account primary document '{runId}' is blocked because canonical ledger report artifact '{artifact.Name}' failed checksum validation.");
            }
        }

        var signaturePayload = string.Join(
            "\n",
            reportPack.Artifacts
                .OrderBy(static artifact => artifact.Name, StringComparer.Ordinal)
                .Select(static artifact => $"{artifact.Name}:{artifact.ChecksumSha256}"));
        if (!string.Equals(
                Sha256Digest.Compute(Utf8(signaturePayload)),
                reportPack.Signature.PayloadChecksumSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                $"Capital-account primary document '{runId}' is blocked because its canonical ledger report pack signature does not match its exact artifacts.");
        }
    }

    private static bool RequiresCertifiedLedgerPresentation(ReportingOutputManifest manifest) =>
        ReportingCertifiedLedgerPresentationBinding.IsRequired(manifest);

    private static byte[] RenderPreview(ReportingOutputManifest manifest)
    {
        var rows = manifest.CertifiedDatasetRows
            .Take(25)
            .Select(static row => row
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal))
            .ToArray();
        var primaryOutputs = ReportingArtifactDeclaration.Build(manifest)
            .Where(static artifact => artifact.Kind == ReportingDeclaredArtifactKind.PrimaryOutput)
            .ToArray();
        var primary = primaryOutputs.FirstOrDefault(static artifact =>
                string.Equals(artifact.ContentType, "application/pdf", StringComparison.Ordinal))
            ?? primaryOutputs.FirstOrDefault()
            ?? throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' declares no primary output for its retained preview.");
        var document = new ReportingRetainedPreviewDocument(
            "meridian.reporting.retained-preview.v1",
            manifest.RunId,
            NormalizeOptional(manifest.RunSeriesId) ?? manifest.RunId,
            manifest.TemplateId,
            manifest.ResolvedTemplate!,
            manifest.AsOfDate,
            manifest.AuthoritativeSource!.CheckpointId,
            manifest.CertifiedSnapshot!.SnapshotId,
            manifest.CertifiedDatasetRows.Length,
            ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows),
            rows.Length,
            ResolveCertifiedColumns(manifest.CertifiedDatasetRows),
            rows,
            new ReportingRetainedPreviewPrimaryArtifact(
                primary.ArtifactId,
                primary.FileName,
                primary.ContentType));
        return JsonSerializer.SerializeToUtf8Bytes(
            document,
            ReportingCertifiedArtifactJsonContext.Default.ReportingRetainedPreviewDocument);
    }

    private static byte[] RenderPrimaryCsv(ReportingOutputManifest manifest)
        => RenderCertifiedRowsCsv(manifest);

    private static byte[] RenderCertifiedRowsCsv(ReportingOutputManifest manifest)
    {
        var builder = new StringBuilder();
        var columns = ResolveCertifiedColumns(manifest.CertifiedDatasetRows);
        if (columns.Length == 0)
        {
            AppendCsvRow(builder, "sourceCheckpointId", "ledgerLineCount", "datasetHashSha256");
            AppendCsvRow(
                builder,
                manifest.AuthoritativeSource!.CheckpointId,
                manifest.AuthoritativeSource.LedgerLineCount.ToString(CultureInfo.InvariantCulture),
                ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows));
            return Utf8(builder.ToString());
        }

        AppendCsvRow(builder, columns);
        foreach (var row in manifest.CertifiedDatasetRows)
        {
            AppendCsvRow(
                builder,
                columns.Select(column => row.TryGetValue(column, out var value) ? value : string.Empty).ToArray());
        }

        return Utf8(builder.ToString());
    }

    private static byte[] RenderSupportingSchedules(ReportingOutputManifest manifest)
    {
        // Guard the default/empty case before enumerating: a default ImmutableArray throws on
        // enumeration (the run-failure and legacy paths can leave this member uninitialized), and an
        // empty or absent grid set falls back to the certified-rows CSV — same result the trailing
        // check produced, but without first touching a default array.
        if (manifest.RenderedReportWriterGrids.IsDefaultOrEmpty)
        {
            return RenderCertifiedRowsCsv(manifest);
        }

        var builder = new StringBuilder("gridId,rowKey,column,value\r\n");
        foreach (var grid in manifest.RenderedReportWriterGrids.OrderBy(static grid => grid.GridId, StringComparer.Ordinal))
        {
            foreach (var row in grid.Rows.OrderBy(static row => row.RowKey, StringComparer.Ordinal))
            {
                foreach (var value in row.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    AppendCsvRow(builder, grid.GridId, row.RowKey, value.Key, value.Value);
                }
            }
        }

        return Utf8(builder.ToString());
    }

    private static byte[] RenderEvidenceVault(ReportingOutputManifest manifest)
    {
        var document = new ReportingEvidenceVaultDocument(
            "meridian.reporting.evidence-vault.v1",
            manifest.RunId,
            manifest.AsOfDate,
            manifest.AuthoritativeSource!,
            manifest.CertifiedSnapshot!,
            manifest.Readiness!,
            manifest.Sections
                .OrderBy(static section => section.SectionId, StringComparer.Ordinal)
                .ToArray(),
            ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows),
            manifest.CertifiedDatasetRows
                .Select(static row => row
                    .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal))
                .ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(
            document,
            ReportingCertifiedArtifactJsonContext.Default.ReportingEvidenceVaultDocument);
    }

    private static byte[] RenderEvidenceAppendix(ReportingOutputManifest manifest)
    {
        var document = new ReportingEvidenceAppendixDocument(
            "meridian.reporting.evidence-appendix.v1",
            manifest.RunId,
            manifest.AuthoritativeSource!.EvidenceIds
                .Concat(manifest.Readiness!.Checks.SelectMany(static check => check.EvidenceReferences))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static evidence => evidence, StringComparer.Ordinal)
                .ToArray(),
            manifest.Sections
                .OrderBy(static section => section.SectionId, StringComparer.Ordinal)
                .Select(static section => section.Lineage)
                .ToArray());
        return JsonSerializer.SerializeToUtf8Bytes(
            document,
            ReportingCertifiedArtifactJsonContext.Default.ReportingEvidenceAppendixDocument);
    }

    private static byte[] RenderGrid(ReportingOutputManifest manifest, string gridId)
    {
        var grid = (manifest.RenderedReportWriterGrids.IsDefaultOrEmpty
                ? null
                : manifest.RenderedReportWriterGrids.SingleOrDefault(item =>
                    string.Equals(item.GridId, gridId, StringComparison.OrdinalIgnoreCase)))
            ?? throw new ReportingGovernanceException(
                $"Declared report-writer grid '{gridId}' has no rendered grid payload.");
        var document = new ReportingRetainedGridDocument(
            "meridian.reporting.grid.v1",
            grid.GridId,
            grid.Title,
            grid.Kind.ToString(),
            grid.Columns
                .Select(static column => new ReportingRetainedGridColumn(
                    column.Key,
                    column.Label,
                    column.Role))
                .ToArray(),
            grid.Rows
                .OrderBy(static row => row.RowKey, StringComparer.Ordinal)
                .Select(static row => new ReportingRetainedGridRow(
                    row.RowKey,
                    row.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                        .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
                ))
                .ToArray(),
            grid.Warnings.OrderBy(static warning => warning, StringComparer.Ordinal).ToArray(),
            grid.Lineage,
            grid.DataDictionary,
            grid.ValidationChecks,
            grid.Chart);
        return JsonSerializer.SerializeToUtf8Bytes(
            document,
            ReportingCertifiedArtifactJsonContext.Default.ReportingRetainedGridDocument);
    }

    private static byte[] RenderPdf(ReportingOutputManifest manifest)
    {
        var textLines = new List<string>
        {
            "Meridian governed report",
            $"Run: {manifest.RunId}",
            $"Template: {manifest.ResolvedTemplate!.Name} v{manifest.ResolvedTemplate.Version.ToString(CultureInfo.InvariantCulture)}",
            $"As of: {manifest.AsOfDate:yyyy-MM-dd}",
            $"Source checkpoint: {manifest.AuthoritativeSource!.CheckpointId}",
            $"Snapshot: {manifest.CertifiedSnapshot!.SnapshotId}",
            $"Certified ledger rows: {manifest.CertifiedDatasetRows.Length.ToString(CultureInfo.InvariantCulture)}",
            $"Certified row hash: {ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows)}"
        };
        var canRenderAccountSummary = manifest.CertifiedDatasetRows.Length > 0
            && manifest.CertifiedDatasetRows.All(static row =>
                row.ContainsKey("account")
                && row.ContainsKey("debit")
                && row.ContainsKey("credit")
                && row.ContainsKey("netAmount"));
        var accountSummaries = canRenderAccountSummary
            ? manifest.CertifiedDatasetRows
                .Select((row, index) => new
                {
                    Account = row["account"],
                    Debit = ReadRequiredDecimal(row, "debit", index),
                    Credit = ReadRequiredDecimal(row, "credit", index),
                    Net = ReadRequiredDecimal(row, "netAmount", index)
                })
                .GroupBy(static row => row.Account, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(group => new
                {
                    Account = group.Key,
                    Debit = group.Sum(static row => row.Debit),
                    Credit = group.Sum(static row => row.Credit),
                    Net = group.Sum(static row => row.Net)
                })
                .ToArray()
            : [];
        if (accountSummaries.Length > 0)
        {
            textLines.Add("Account totals (debit / credit / net)");
            textLines.AddRange(accountSummaries.Select(summary =>
                $"{summary.Account}: {summary.Debit.ToString("G29", CultureInfo.InvariantCulture)} / {summary.Credit.ToString("G29", CultureInfo.InvariantCulture)} / {summary.Net.ToString("G29", CultureInfo.InvariantCulture)}"));
        }
        else
        {
            foreach (var row in manifest.CertifiedDatasetRows)
            {
                textLines.Add(string.Join(
                    "; ",
                    row.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                        .Select(static pair => $"{pair.Key}={pair.Value}")));
            }
        }

        // A substituted primary document is not a warning-only outcome: it would certify different
        // text than the source. Fail before byte production so the coordinator retains a Failed
        // execution with the recovery guidance below instead of a superficially successful PDF.
        EnsurePdfTextCanBeRenderedWithoutLoss(textLines);
        var wrappedLines = textLines.SelectMany(WrapPdfLine).ToArray();
        var pageStreams = wrappedLines
            .Chunk(PdfLinesPerPage)
            .Select(RenderPdfPage)
            .ToArray();
        if (pageStreams.Length == 0)
        {
            pageStreams = [RenderPdfPage([])];
        }

        const int catalogObjectId = 1;
        const int pagesObjectId = 2;
        const int fontObjectId = 3;
        var objects = new List<string>(3 + (pageStreams.Length * 2))
        {
            $"<< /Type /Catalog /Pages {pagesObjectId} 0 R >>",
            string.Empty,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var pageObjectIds = new List<int>(pageStreams.Length);
        foreach (var stream in pageStreams)
        {
            var pageObjectId = objects.Count + 1;
            var contentObjectId = pageObjectId + 1;
            pageObjectIds.Add(pageObjectId);
            objects.Add(
                $"<< /Type /Page /Parent {pagesObjectId} 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontObjectId} 0 R >> >> /Contents {contentObjectId} 0 R >>");
            objects.Add(
                $"<< /Length {Encoding.ASCII.GetByteCount(stream).ToString(CultureInfo.InvariantCulture)} >>\nstream\n{stream}endstream");
        }

        objects[pagesObjectId - 1] =
            $"<< /Type /Pages /Kids [{string.Join(" ", pageObjectIds.Select(static id => $"{id} 0 R"))}] /Count {pageStreams.Length.ToString(CultureInfo.InvariantCulture)} >>";
        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.4\n%Meridian\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xref = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(output, $"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }
        WriteAscii(output,
            $"trailer\n<< /Size {objects.Count + 1} /Root {catalogObjectId} 0 R >>\nstartxref\n{xref.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return output.ToArray();
    }

    private static void EnsurePdfTextCanBeRenderedWithoutLoss(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            foreach (var rune in line.EnumerateRunes())
            {
                if (rune.Value is >= 0x20 and <= 0x7E)
                {
                    continue;
                }

                throw new ReportingGovernanceException(
                    $"PDF artifact rendering is blocked because certified report text contains Unicode scalar U+{rune.Value:X4}, which is outside the producer's deterministic ASCII Helvetica font mapping. No lossy PDF bytes were emitted or retained. Choose XLSX, CSV, or Evidence Vault output to preserve the source text, or configure an embedded Unicode PDF font before retrying.");
            }
        }
    }

    private static string RenderPdfPage(IEnumerable<string> lines)
    {
        var content = new StringBuilder("BT /F1 10 Tf 48 744 Td 14 TL\n");
        foreach (var line in lines)
        {
            content.Append('(').Append(EscapePdf(line)).Append(") Tj T*\n");
        }

        return content.Append("ET\n").ToString();
    }

    private static IEnumerable<string> WrapPdfLine(string value)
    {
        var normalized = string.Concat(value.Select(static character =>
            char.IsControl(character) ? ' ' : character)).Trim();
        if (normalized.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        var continuation = false;
        while (normalized.Length > 0)
        {
            var prefix = continuation ? "  " : string.Empty;
            var capacity = PdfCharactersPerLine - prefix.Length;
            if (normalized.Length <= capacity)
            {
                yield return prefix + normalized;
                yield break;
            }

            var split = normalized.LastIndexOf(' ', capacity - 1, capacity);
            if (split <= 0)
            {
                split = capacity;
                if (char.IsHighSurrogate(normalized[split - 1])
                    && split < normalized.Length
                    && char.IsLowSurrogate(normalized[split]))
                {
                    split--;
                }
            }

            yield return prefix + normalized[..split].TrimEnd();
            normalized = normalized[split..].TrimStart();
            continuation = true;
        }
    }

    private static byte[] RenderXlsx(ReportingOutputManifest manifest)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            AddZipEntry(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            AddZipEntry(archive, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            AddZipEntry(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Report\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            AddZipEntry(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            var sheet = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            var columns = ResolveCertifiedColumns(manifest.CertifiedDatasetRows);
            if (columns.Length == 0)
            {
                var metadata = new[]
                {
                    ("Run", manifest.RunId),
                    ("Template", $"{manifest.ResolvedTemplate!.Name} v{manifest.ResolvedTemplate.Version.ToString(CultureInfo.InvariantCulture)}"),
                    ("As of", manifest.AsOfDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ("Source checkpoint", manifest.AuthoritativeSource!.CheckpointId),
                    ("Snapshot", manifest.CertifiedSnapshot!.SnapshotId)
                };
                for (var index = 0; index < metadata.Length; index++)
                {
                    AppendXlsxRow(sheet, index + 1, [metadata[index].Item1, metadata[index].Item2]);
                }
            }
            else
            {
                AppendXlsxRow(sheet, 1, columns);
                var rowNumber = 2;
                foreach (var row in manifest.CertifiedDatasetRows)
                {
                    AppendXlsxRow(
                        sheet,
                        rowNumber++,
                        columns.Select(column => row.TryGetValue(column, out var value) ? value : string.Empty).ToArray());
                }
            }
            sheet.Append("</sheetData></worksheet>");
            AddZipEntry(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return output.ToArray();
    }

    private static void AddZipEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var stream = entry.Open();
        var bytes = Utf8(content);
        stream.Write(bytes, 0, bytes.Length);
    }

    private static void AppendXlsxRow(
        StringBuilder sheet,
        int rowNumber,
        IReadOnlyList<string> values)
    {
        sheet.Append("<row r=\"").Append(rowNumber).Append("\">");
        for (var index = 0; index < values.Count; index++)
        {
            var cell = $"{ToExcelColumnName(index + 1)}{rowNumber.ToString(CultureInfo.InvariantCulture)}";
            sheet.Append("<c r=\"").Append(cell)
                .Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                .Append(EscapeXml(values[index]))
                .Append("</t></is></c>");
        }
        sheet.Append("</row>");
    }

    private static string ToExcelColumnName(int ordinal)
    {
        var builder = new StringBuilder();
        var value = ordinal;
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }
        return builder.ToString();
    }

    internal static string[] ResolveCertifiedColumns(
        ImmutableArray<IReadOnlyDictionary<string, string>> rows) =>
        rows.IsDefaultOrEmpty
            ? []
            : rows.SelectMany(static row => row.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static column => column, StringComparer.Ordinal)
                .ToArray();

    private static decimal ReadRequiredDecimal(
        IReadOnlyDictionary<string, string> row,
        string key,
        int zeroBasedRowIndex)
    {
        if (row.TryGetValue(key, out var value)
            && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        throw new ReportingGovernanceException(
            $"Certified ledger row {(zeroBasedRowIndex + 1).ToString(CultureInfo.InvariantCulture)} has an invalid '{key}' value. " +
            "PDF account totals were not produced because invalid certified numeric data cannot be coerced to zero. " +
            "Correct and recertify the authoritative dataset, then retry artifact production.");
    }

    internal static string ComputeCertifiedRowsHash(
        ImmutableArray<IReadOnlyDictionary<string, string>> rows)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var row in rows.IsDefault
                         ? ImmutableArray<IReadOnlyDictionary<string, string>>.Empty
                         : rows)
            {
                writer.WriteStartObject();
                foreach (var pair in row.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WriteString(pair.Key, pair.Value);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        return Sha256Digest.Compute(stream.ToArray());
    }

    private static void AppendCsvRow(StringBuilder builder, params string?[] values)
    {
        builder.AppendJoin(',', values.Select(EscapeCsv)).Append("\r\n");
    }

    private static string EscapeCsv(string? value)
        => Meridian.Storage.Export.SpreadsheetFormulaGuard.EscapeCsvCell(value);

    private static void ValidateManifest(ReportingOutputManifest manifest)
    {
        if (manifest.Status != ReportingRunStatus.Draft
            || manifest.ResolvedTemplate is null
            || manifest.ResolvedParameters is null
            || manifest.Readiness is null
            || manifest.OperationalScope is null
            || manifest.ImmutableAccessScope is null
            || manifest.CertifiedSnapshot is null
            || manifest.AuthoritativeSource is null
            || manifest.Artifacts.IsDefaultOrEmpty
            || manifest.CertifiedDatasetRows.IsDefault
            || manifest.CertifiedDatasetRows.Length != manifest.AuthoritativeSource.LedgerLineCount
            || manifest.CertifiedDatasetRows.Any(static row =>
                row is null
                || row.Keys.Any(string.IsNullOrWhiteSpace)
                || row.Values.Any(static value => value is null))
            || string.IsNullOrWhiteSpace(manifest.CertifiedSnapshot.ParametersCanonicalJson)
            || !Sha256Digest.IsWellFormed(manifest.CertifiedSnapshot.ParametersHash)
            || !string.Equals(
                manifest.CertifiedSnapshot.SourceCheckpointId,
                manifest.AuthoritativeSource.CheckpointId,
                StringComparison.Ordinal)
            || !string.Equals(
                manifest.CertifiedSnapshot.SourceCheckpointHash,
                manifest.AuthoritativeSource.CheckpointHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                $"Reporting manifest '{manifest.RunId}' is not a complete authoritative certified Draft.");
        }
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static string EscapePdf(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static string EscapeXml(string value)
    {
        var sanitized = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (IsXml10Character(rune.Value))
            {
                sanitized.Append(rune.ToString());
            }
        }

        return System.Security.SecurityElement.Escape(sanitized.ToString()) ?? string.Empty;
    }

    private static bool IsXml10Character(int scalar) =>
        scalar is 0x9 or 0xA or 0xD
        || scalar is >= 0x20 and <= 0xD7FF
        || scalar is >= 0xE000 and <= 0xFFFD
        || scalar is >= 0x10000 and <= 0x10FFFF;

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

}
