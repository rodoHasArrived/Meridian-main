using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using QuestPDF.Infrastructure;

namespace Meridian.Documents;

/// <summary>
/// Shared determinism utilities for client-grade document rendering. QuestPDF and ClosedXML both
/// introduce non-deterministic bytes by default (wall-clock metadata, a GUID-named OPC
/// core-properties part, random relationship ids, and reordered zip entries). These helpers pin
/// them so re-rendering identical input reproduces identical bytes, which the governed reporting
/// pipeline relies on for audit hash verification.
/// </summary>
internal static class DeterministicDocumentPackaging
{
    internal static readonly DateTime FixedTimestamp = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ClosedXML names the OPC core-properties part with a fresh random GUID each save (and references
    // it by that GUID in _rels/.rels) and reorders zip entries; re-zip deterministically with a fixed
    // core-properties name, fixed timestamps, and sorted entries so re-rendering yields stable bytes.
    private const string CanonicalCorePropertiesPart = "package/services/metadata/core-properties/core.psmdcp";

    /// <summary>
    /// Applies the process-wide QuestPDF configuration required for deterministic, license-clean
    /// rendering. Idempotent; safe to call from every renderer's static constructor.
    /// </summary>
    internal static void ConfigureQuestPdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.EnableDebugging = false;
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
    }

    /// <summary>
    /// Re-zips a ClosedXML workbook deterministically: canonicalizes the volatile core-properties
    /// part name, pins its timestamps, normalizes package relationship ids, and writes entries in a
    /// stable order with fixed entry timestamps.
    /// </summary>
    internal static byte[] Canonicalize(byte[] workbookBytes)
    {
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string? volatileCorePropertiesPart = null;
        using (var source = new ZipArchive(new MemoryStream(workbookBytes), ZipArchiveMode.Read))
        {
            foreach (var entry in source.Entries)
            {
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);
                var name = entry.FullName;
                if (name.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
                    && name.EndsWith(".psmdcp", StringComparison.Ordinal))
                {
                    volatileCorePropertiesPart = name;
                    name = CanonicalCorePropertiesPart;
                }

                parts[name] = buffer.ToArray();
            }
        }

        // Repoint every reference to the GUID-named core-properties part (_rels/.rels targets it and
        // [Content_Types].xml overrides it by full part name), and pin the wall-clock created/modified
        // timestamps ClosedXML stamps into the core-properties part regardless of Properties.
        if (volatileCorePropertiesPart is not null)
        {
            var oldFileName = volatileCorePropertiesPart[(volatileCorePropertiesPart.LastIndexOf('/') + 1)..];
            foreach (var referencingPart in new[] { "_rels/.rels", "[Content_Types].xml" })
            {
                if (parts.TryGetValue(referencingPart, out var bytes))
                {
                    var rewritten = Encoding.UTF8.GetString(bytes).Replace(oldFileName, "core.psmdcp", StringComparison.Ordinal);
                    if (referencingPart == "_rels/.rels")
                        rewritten = NormalizeRelationshipIds(rewritten);
                    parts[referencingPart] = Encoding.UTF8.GetBytes(rewritten);
                }
            }

            if (parts.TryGetValue(CanonicalCorePropertiesPart, out var coreBytes))
                parts[CanonicalCorePropertiesPart] = Encoding.UTF8.GetBytes(PinCoreTimestamps(Encoding.UTF8.GetString(coreBytes)));
        }

        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            foreach (var part in parts.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                var targetEntry = target.CreateEntry(part.Key, CompressionLevel.NoCompression);
                targetEntry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var targetStream = targetEntry.Open();
                targetStream.Write(part.Value, 0, part.Value.Length);
            }
        }

        return output.ToArray();
    }

    // Package-level relationships carry random 16-hex ids that no other part references by id;
    // replace them with sequential canonical ids so re-rendering is byte-stable.
    private static string NormalizeRelationshipIds(string rels)
    {
        var counter = 0;
        return Regex.Replace(
            rels,
            "Id=\"R[0-9a-fA-F]{16}\"",
            _ => $"Id=\"Rc{++counter}\"");
    }

    private static string PinCoreTimestamps(string coreProperties)
        => Regex.Replace(
            coreProperties,
            @"(<dcterms:(?:created|modified)[^>]*>)[^<]*(</dcterms:(?:created|modified)>)",
            "${1}2000-01-01T00:00:00Z${2}");
}
