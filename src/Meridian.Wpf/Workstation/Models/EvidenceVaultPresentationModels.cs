using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Workstation.Models;

public sealed record EvidenceVaultDocumentQueuePresentationModel(
    string SummaryText,
    string ReadinessText,
    string ManifestSnapshotText,
    WorkstationReadinessTone ReadinessTone,
    string Tone,
    IReadOnlyList<EvidenceVaultDocumentRowModel> Documents,
    EvidenceVaultManifestSnapshotModel? ManifestSnapshot)
{
    public bool HasFrozenManifestSnapshot => ManifestSnapshot is not null;
}

public sealed record EvidenceVaultDocumentRowModel(
    string DocumentId,
    string FileName,
    string ClassificationText,
    string ExtractionText,
    string ReviewText,
    string SourceHashText,
    string SourceText,
    string ActorText,
    string TenantScopeText,
    string ObjectLinksText,
    string OpenRequestCountText,
    string ManifestRoute,
    WorkstationReadinessTone ReadinessTone,
    string Tone,
    IReadOnlyList<WorkstationEvidenceLinkModel> EvidenceLinks);

public sealed record EvidenceVaultManifestSnapshotModel(
    string ManifestId,
    string PackageText,
    string FrozenText,
    string ContentHashText,
    string DocumentCountText,
    string RequestCountText,
    string ObjectLinkCountText,
    WorkstationReadinessTone ReadinessTone,
    string Tone);

public static class EvidenceVaultPresentationMapper
{
    public static EvidenceVaultDocumentQueuePresentationModel BuildDocumentQueue(
        IReadOnlyList<EvidenceVaultDocumentEntryDto> entries,
        EvidenceManifestDto? manifestSnapshot)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var rows = entries.Select(ToDocumentRow).ToArray();
        var needsReviewCount = rows.Count(row => row.ReadinessTone is WorkstationReadinessTone.SignoffRequired or WorkstationReadinessTone.Blocked);
        var openRequestCount = entries.Sum(entry => entry.OpenRequestCount);
        var readinessTone = ResolveQueueTone(rows, manifestSnapshot);
        var tone = ToWorkspaceTone(readinessTone);
        var manifestModel = manifestSnapshot is null ? null : ToManifestSnapshot(manifestSnapshot);

        return new EvidenceVaultDocumentQueuePresentationModel(
            Pluralize(rows.Length, "document") + " retained",
            BuildReadinessText(rows.Length, needsReviewCount, openRequestCount),
            manifestModel is null ? "No frozen manifest snapshot" : $"{manifestModel.ManifestId} frozen for {manifestModel.PackageText}",
            readinessTone,
            tone,
            rows,
            manifestModel);
    }

    public static EvidenceVaultManifestSnapshotModel ToManifestSnapshot(EvidenceManifestDto manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var openRequestCount = manifest.Requests.Count(IsOpenRequest);
        var readinessTone = openRequestCount > 0
            ? WorkstationReadinessTone.SignoffRequired
            : WorkstationReadinessTone.EvidenceLinked;

        return new EvidenceVaultManifestSnapshotModel(
            manifest.ManifestId,
            $"{manifest.PackageKind} {manifest.PackageId}",
            $"Frozen {manifest.FrozenAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC",
            ShortHash(manifest.ContentHashSha256),
            Pluralize(manifest.Documents.Count, "document"),
            openRequestCount == 0 ? "No open requests" : Pluralize(openRequestCount, "open request"),
            Pluralize(manifest.ObjectLinks.Count, "object link"),
            readinessTone,
            ToWorkspaceTone(readinessTone));
    }

    private static EvidenceVaultDocumentRowModel ToDocumentRow(EvidenceVaultDocumentEntryDto entry)
    {
        var document = entry.Document;
        var readinessTone = ResolveDocumentTone(entry);
        var manifestRoute = string.IsNullOrWhiteSpace(document.ManifestRoute)
            ? entry.ManifestRoute
            : document.ManifestRoute;

        return new EvidenceVaultDocumentRowModel(
            document.DocumentId,
            document.FileName,
            FormatEnum(document.Classification),
            FormatEnum(document.ExtractionStatus),
            FormatReview(document.ReviewerState),
            ShortHash(document.SourceHashSha256),
            FormatSource(document),
            string.IsNullOrWhiteSpace(document.Actor) ? "Unknown actor" : document.Actor,
            FormatTenantScope(document.TenantId, document.Scope),
            FormatObjectLinks(document.ObjectLinks),
            entry.OpenRequestCount == 0 ? "No open requests" : Pluralize(entry.OpenRequestCount, "open request"),
            manifestRoute,
            readinessTone,
            ToWorkspaceTone(readinessTone),
            BuildEvidenceLinks(document, manifestRoute));
    }

    private static WorkstationReadinessTone ResolveQueueTone(
        IReadOnlyList<EvidenceVaultDocumentRowModel> rows,
        EvidenceManifestDto? manifestSnapshot)
    {
        if (rows.Count == 0)
        {
            return WorkstationReadinessTone.Neutral;
        }

        if (rows.Any(row => row.ReadinessTone == WorkstationReadinessTone.Blocked))
        {
            return WorkstationReadinessTone.Blocked;
        }

        if (rows.Any(row => row.ReadinessTone == WorkstationReadinessTone.SignoffRequired) ||
            manifestSnapshot?.Requests.Any(IsOpenRequest) == true)
        {
            return WorkstationReadinessTone.SignoffRequired;
        }

        return WorkstationReadinessTone.EvidenceLinked;
    }

    private static WorkstationReadinessTone ResolveDocumentTone(EvidenceVaultDocumentEntryDto entry)
    {
        var document = entry.Document;
        if (document.ExtractionStatus == EvidenceExtractionStatusDto.Rejected ||
            document.ReviewerState.Status == EvidenceDocumentReviewStatusDto.Rejected ||
            entry.SupportRequests.Any(request => string.Equals(request.BlockedOutput, "close", StringComparison.OrdinalIgnoreCase)))
        {
            return WorkstationReadinessTone.Blocked;
        }

        if (document.ExtractionStatus == EvidenceExtractionStatusDto.NeedsReview ||
            document.ReviewerState.Status == EvidenceDocumentReviewStatusDto.NeedsReview ||
            entry.OpenRequestCount > 0)
        {
            return WorkstationReadinessTone.SignoffRequired;
        }

        if (document.ExtractionStatus == EvidenceExtractionStatusDto.Accepted ||
            document.ReviewerState.Status == EvidenceDocumentReviewStatusDto.Accepted)
        {
            return WorkstationReadinessTone.EvidenceLinked;
        }

        return WorkstationReadinessTone.Neutral;
    }

    private static IReadOnlyList<WorkstationEvidenceLinkModel> BuildEvidenceLinks(
        EvidenceDocumentDto document,
        string manifestRoute)
    {
        var links = document.ObjectLinks
            .Select(link => new WorkstationEvidenceLinkModel(
                string.IsNullOrWhiteSpace(link.Label) ? FormatEnum(link.LinkKind) : link.Label,
                string.IsNullOrWhiteSpace(link.Route) ? link.ObjectId : link.Route,
                FormatEnum(link.LinkKind),
                link.Relationship ?? string.Empty))
            .ToList();

        if (!string.IsNullOrWhiteSpace(manifestRoute))
        {
            links.Add(new WorkstationEvidenceLinkModel("Frozen manifest", manifestRoute, "evidence-vault"));
        }

        return links;
    }

    private static string BuildReadinessText(int documentCount, int needsReviewCount, int openRequestCount)
    {
        if (documentCount == 0)
        {
            return "No vault documents retained";
        }

        if (needsReviewCount > 0 || openRequestCount > 0)
        {
            var reviewText = needsReviewCount == 1
                ? "1 document needs review"
                : $"{needsReviewCount} documents need review";
            return $"{reviewText}; {Pluralize(openRequestCount, "open request")}";
        }

        return "Vault documents are linked to frozen evidence";
    }

    private static string FormatReview(EvidenceDocumentReviewStateDto reviewState)
    {
        var status = FormatEnum(reviewState.Status);
        if (!string.IsNullOrWhiteSpace(reviewState.Reviewer))
        {
            status += $" by {reviewState.Reviewer}";
        }

        return status;
    }

    private static string FormatSource(EvidenceDocumentDto document)
    {
        var parts = new[]
        {
            document.SourceChannel,
            document.SourceSystem,
            document.SourceReference
        }.Where(part => !string.IsNullOrWhiteSpace(part));

        return string.Join(" / ", parts);
    }

    private static string FormatTenantScope(string? tenantId, string? scope)
    {
        if (string.IsNullOrWhiteSpace(tenantId) && string.IsNullOrWhiteSpace(scope))
        {
            return "No tenant scope";
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            return tenantId!;
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return scope;
        }

        return $"{tenantId} / {scope}";
    }

    private static string FormatObjectLinks(IReadOnlyList<EvidenceDocumentLinkDto> links)
    {
        if (links.Count == 0)
        {
            return "No object links";
        }

        return string.Join(", ", links.Select(link =>
            $"{(string.IsNullOrWhiteSpace(link.Label) ? FormatEnum(link.LinkKind) : link.Label)} ({link.ObjectId})"));
    }

    private static bool IsOpenRequest(EvidenceRequestDto request)
        => !string.Equals(request.Status, "Accepted", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(request.Status, "Closed", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(request.Status, "Resolved", StringComparison.OrdinalIgnoreCase);

    private static string FormatEnum<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var text = value.ToString();
        var chars = new List<char>(text.Length + 8);
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (index > 0 && char.IsUpper(current) && !char.IsUpper(text[index - 1]))
            {
                chars.Add(' ');
            }

            chars.Add(current);
        }

        return string.Concat(chars);
    }

    private static string ShortHash(string hash)
        => string.IsNullOrWhiteSpace(hash)
            ? "No hash"
            : hash.Length <= 12 ? hash : hash[..12];

    private static string Pluralize(int count, string singular)
        => count == 1 ? $"1 {singular}" : $"{count} {singular}s";

    private static string ToWorkspaceTone(WorkstationReadinessTone readinessTone)
        => readinessTone switch
        {
            WorkstationReadinessTone.Blocked => WorkspaceTone.Danger,
            WorkstationReadinessTone.SignoffRequired => WorkspaceTone.Warning,
            WorkstationReadinessTone.EvidenceLinked or WorkstationReadinessTone.Ready => WorkspaceTone.Success,
            WorkstationReadinessTone.Recovery or WorkstationReadinessTone.Stale => WorkspaceTone.Warning,
            _ => WorkspaceTone.Neutral
        };
}
