using System.IO.Compression;
using FluentAssertions;
using Meridian.Storage.Export;

namespace Meridian.Tests.Storage;

public sealed class XlsxWorkbookWriterTests
{
    [Fact]
    public void CreateWorkbook_SameInput_ProducesIdenticalBytesWithFixedEntryTimestamps()
    {
        var worksheets = new[]
        {
            new XlsxWorksheet(
                "Integrity",
                ["Field", "Value"],
                [
                    (IReadOnlyList<object?>)["asOfDate", new DateOnly(2026, 6, 30)],
                    (IReadOnlyList<object?>)["nav", 1234.56m],
                    (IReadOnlyList<object?>)["approved", true]
                ])
        };

        var first = XlsxWorkbookWriter.CreateWorkbook(worksheets);
        var second = XlsxWorkbookWriter.CreateWorkbook(worksheets);

        first.Should().Equal(second);

        using var stream = new MemoryStream(first);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        archive.Entries.Should().NotBeEmpty();
        foreach (var entry in archive.Entries)
        {
            entry.LastWriteTime.DateTime.Should().Be(
                new DateTime(2000, 1, 1, 0, 0, 0),
                $"{entry.FullName} must use the deterministic workbook timestamp");
        }
    }
}
