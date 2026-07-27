using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

/// <summary>
/// One persistence-boundary validator for certified reporting manifests. File and PostgreSQL run
/// stores must invoke this same validator so changing the storage adapter cannot weaken the
/// immutable scope, source, readiness, dataset, or snapshot-hash guarantees.
/// </summary>
public static class ReportingCertifiedManifestValidation
{
    public static void Validate(ReportingOutputManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.RunId)
            || string.IsNullOrWhiteSpace(manifest.TemplateId)
            || manifest.CertifiedDatasetRows.IsDefault)
        {
            throw new InvalidDataException("A retained reporting manifest is incomplete.");
        }

        var hasCertifiedState = manifest.OperationalScope is not null
            || manifest.ImmutableAccessScope is not null
            || manifest.CertifiedSnapshot is not null
            || manifest.AuthoritativeSource is not null;
        if (!hasCertifiedState)
        {
            return;
        }

        if (manifest.OperationalScope is not { } scope
            || manifest.ImmutableAccessScope is not { } access
            || manifest.CertifiedSnapshot is not { } snapshot
            || manifest.AuthoritativeSource is not { } source
            || manifest.ResolvedTemplate is not { } template
            || manifest.ResolvedParameters is not { } parameters
            || manifest.Readiness is not { } readiness)
        {
            throw new InvalidDataException(
                "A retained certified reporting manifest has incomplete or mismatched source bindings.");
        }

        if (!IsCanonicalNonEmptyGuid(scope.PeriodId)
            || !string.Equals(scope.PeriodId, source.AccountingPeriodId, StringComparison.Ordinal)
            || !string.Equals(scope.PeriodId, parameters.PeriodId, StringComparison.Ordinal)
            || !string.Equals(
                scope.PeriodId,
                readiness.ResolvedParameters.PeriodId,
                StringComparison.Ordinal)
            || !string.Equals(scope.PeriodId, snapshot.PeriodId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "A retained certified reporting manifest must bind its scope, authoritative source, resolved parameters, readiness parameters, and snapshot to the same canonical non-empty GUID accounting-period identity.");
        }

        if (!IsSha256(readiness.EvidenceHash)
            || !IsUtc(readiness.EvaluatedAtUtc)
            || readiness.Status != ReportingRunReadinessStatusDto.Ready
            || !readiness.CanGenerateDraft
            || parameters.Finality == ReportingFinalityDto.Final
                && !readiness.CanGenerateFinal
            || readiness.Checks is null
            || readiness.Checks.Count == 0
            || readiness.Checks.Any(check =>
                check is null
                || check.EvidenceReferences is null
                || check.EvidenceReferences.Count == 0
                || (parameters.Finality == ReportingFinalityDto.Final
                        ? check.BlocksFinal
                        : check.BlocksDraft)
                    && check.Status != ReportingRunReadinessStatusDto.Ready)
            || !string.Equals(template.Name, manifest.TemplateId, StringComparison.Ordinal)
            || template.Version <= 0
            || !Equals(readiness.ResolvedTemplate, template)
            || !string.Equals(
                snapshot.ParametersCanonicalJson,
                ReportingCanonicalParameterSerializer.Serialize(
                    parameters,
                    snapshot.RequiresCertifiedLedgerPresentation),
                StringComparison.Ordinal)
            || !string.Equals(
                snapshot.ParametersCanonicalJson,
                ReportingCanonicalParameterSerializer.Serialize(
                    readiness.ResolvedParameters,
                    snapshot.RequiresCertifiedLedgerPresentation),
                StringComparison.Ordinal)
            || manifest.CertifiedDatasetRows.Length != source.LedgerLineCount
            || manifest.CertifiedDatasetRows.Any(static row =>
                row is null
                || row.Keys.Any(string.IsNullOrWhiteSpace)
                || row.Values.Any(static value => value is null))
            || source.LedgerLineCount < 0
            || source.JournalEntryCount < 0
            || source.HighestGlobalSequence < 0
            || !IsUtc(source.CutoffUtc)
            || !IsUtc(source.CapturedAtUtc)
            || source.CapturedAtUtc > snapshot.CapturedAtUtc
            || readiness.EvaluatedAtUtc > snapshot.CapturedAtUtc
            || source.AsOfDate != manifest.AsOfDate
            || parameters.AsOfDate != manifest.AsOfDate
            || !string.Equals(
                source.AccountingBasis,
                ExpectedAccountingBasis(parameters.AccountingBasis),
                StringComparison.Ordinal)
            || !string.Equals(source.TenantId, scope.TenantId, StringComparison.Ordinal)
            || !string.Equals(source.OrganizationId, scope.OrganizationId, StringComparison.Ordinal)
            || !string.Equals(source.CompanyId, scope.CompanyId, StringComparison.Ordinal)
            || !string.Equals(source.FundId, scope.FundId, StringComparison.Ordinal)
            || !string.Equals(source.LedgerBookId, scope.BookId, StringComparison.Ordinal)
            || !string.Equals(source.AccountingPeriodId, scope.PeriodId, StringComparison.Ordinal)
            || !string.Equals(snapshot.SourceCheckpointId, source.CheckpointId, StringComparison.Ordinal)
            || !string.Equals(
                snapshot.SourceCheckpointHash,
                source.CheckpointHash,
                StringComparison.OrdinalIgnoreCase)
            || !IsSha256(source.CheckpointHash)
            || source.EvidenceIds.IsDefaultOrEmpty
            || source.EvidenceIds.Any(string.IsNullOrWhiteSpace)
            || source.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != source.EvidenceIds.Length
            || !source.EvidenceIds.Contains(
                $"reporting-source-checkpoint:{source.CheckpointId}:{source.CheckpointHash}",
                StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "A retained certified reporting manifest has incomplete or mismatched source bindings.");
        }

        try
        {
            ReportingGovernanceCanonicalValidation.ValidateScope(scope);
            ReportingGovernanceCanonicalValidation.ValidateAccess(access);
            ReportingGovernanceCanonicalValidation.ValidateSnapshot(snapshot, scope);
        }
        catch (ReportingGovernanceException exception)
        {
            throw new InvalidDataException(
                $"A retained certified reporting manifest failed canonical governance validation: {exception.Message}",
                exception);
        }

        var expectedSnapshotHash = ComputeSnapshotHash(manifest);
        if (!FixedHashEquals(snapshot.SnapshotHash, expectedSnapshotHash))
        {
            throw new InvalidDataException(
                "The retained certified snapshot hash does not match its template, scope, access, parameters, source, reconciliation, readiness, and certified-dataset binding.");
        }
    }

    public static string ComputeCertifiedRowsHash(
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

        return ComputeSha256(stream.ToArray());
    }

    public static string ComputeSnapshotHash(ReportingOutputManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.CertifiedSnapshot is null
            || manifest.ResolvedTemplate is null
            || manifest.OperationalScope is null
            || manifest.ImmutableAccessScope is null
            || manifest.AuthoritativeSource is null
            || manifest.Readiness is null)
        {
            throw new InvalidDataException(
                "A certified reporting manifest is incomplete and cannot produce a snapshot hash.");
        }

        var payload = manifest.CertifiedSnapshot.RequiresCertifiedLedgerPresentation
            ? JsonSerializer.Serialize(new
            {
                template = new
                {
                    manifest.ResolvedTemplate.Name,
                    manifest.ResolvedTemplate.Version
                },
                scope = manifest.OperationalScope,
                access = manifest.ImmutableAccessScope,
                parametersHash = manifest.CertifiedSnapshot.ParametersHash,
                sourceCheckpointId = manifest.AuthoritativeSource.CheckpointId,
                sourceCheckpointHash = manifest.AuthoritativeSource.CheckpointHash,
                reconciliationId = manifest.CertifiedSnapshot.ReconciliationCheckpointId,
                reconciliationHash = manifest.CertifiedSnapshot.ReconciliationCheckpointHash,
                readinessHash = manifest.Readiness.EvidenceHash,
                certifiedDatasetHash = ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows),
                requiresCertifiedLedgerPresentation = true
            })
            : JsonSerializer.Serialize(new
            {
                template = new
                {
                    manifest.ResolvedTemplate.Name,
                    manifest.ResolvedTemplate.Version
                },
                scope = manifest.OperationalScope,
                access = manifest.ImmutableAccessScope,
                parametersHash = manifest.CertifiedSnapshot.ParametersHash,
                sourceCheckpointId = manifest.AuthoritativeSource.CheckpointId,
                sourceCheckpointHash = manifest.AuthoritativeSource.CheckpointHash,
                reconciliationId = manifest.CertifiedSnapshot.ReconciliationCheckpointId,
                reconciliationHash = manifest.CertifiedSnapshot.ReconciliationCheckpointHash,
                readinessHash = manifest.Readiness.EvidenceHash,
                certifiedDatasetHash = ComputeCertifiedRowsHash(manifest.CertifiedDatasetRows)
            });
        return ComputeSha256(Encoding.UTF8.GetBytes(payload));
    }

    private static bool IsUtc(DateTimeOffset value) =>
        value != default && value.Offset == TimeSpan.Zero;

    private static bool IsCanonicalNonEmptyGuid(string? value) =>
        Guid.TryParseExact(value, "D", out var periodId)
        && periodId != Guid.Empty
        && string.Equals(value, periodId.ToString("D"), StringComparison.Ordinal);

    private static string ExpectedAccountingBasis(ReportingAccountingBasisDto basis) =>
        basis switch
        {
            ReportingAccountingBasisDto.Gaap => "Gaap",
            ReportingAccountingBasisDto.Tax => "Tax",
            ReportingAccountingBasisDto.Cash => "Cash",
            ReportingAccountingBasisDto.Statutory => "Statutory",
            _ => "Primary"
        };

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static bool FixedHashEquals(string left, string right) =>
        IsSha256(left)
        && IsSha256(right)
        && CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left),
            Convert.FromHexString(right));

    private static string ComputeSha256(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
