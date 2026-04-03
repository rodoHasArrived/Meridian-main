using Meridian.QuantScript.Documents;

namespace Meridian.QuantScript.Tests;

public sealed class QuantScriptNotebookStoreTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "meridian-quantscript-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAndLoadNotebook_RoundTripsCells()
    {
        Directory.CreateDirectory(_tempDirectory);
        var store = BuildStore();
        var path = Path.Combine(_tempDirectory, "roundtrip.mqnb");
        var document = new QuantScriptNotebookDocument
        {
            Title = "Round Trip",
            Cells =
            [
                new QuantScriptNotebookCellDocument("cell-1", "var x = 1;"),
                new QuantScriptNotebookCellDocument("cell-2", "Print(x);")
            ]
        };

        await store.SaveNotebookAsync(path, document);
        var loaded = await store.LoadNotebookAsync(path);

        loaded.Title.Should().Be("Round Trip");
        loaded.Cells.Should().HaveCount(2);
        loaded.Cells[1].Source.Should().Contain("Print(x)");
    }

    [Fact]
    public async Task ImportLegacyScript_CreatesSingleCellNotebook()
    {
        Directory.CreateDirectory(_tempDirectory);
        var store = BuildStore();
        var legacyPath = Path.Combine(_tempDirectory, "legacy.csx");
        await File.WriteAllTextAsync(legacyPath, "Print(\"legacy\");");

        var notebook = await store.ImportLegacyScriptAsync(legacyPath);

        notebook.Title.Should().Be("legacy");
        notebook.Cells.Should().ContainSingle();
        notebook.Cells[0].Source.Should().Contain("legacy");
    }

    [Fact]
    public void ListDocuments_ReturnsNotebookAndLegacyEntries()
    {
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(Path.Combine(_tempDirectory, "alpha.mqnb"), "{}");
        File.WriteAllText(Path.Combine(_tempDirectory, "beta.csx"), "Print(\"beta\");");
        var store = BuildStore();

        var documents = store.ListDocuments();

        documents.Should().Contain(doc => doc.Kind == QuantScriptDocumentKind.Notebook && doc.Name == "alpha.mqnb");
        documents.Should().Contain(doc => doc.Kind == QuantScriptDocumentKind.LegacyScript && doc.Name == "beta.csx");
    }

    [Fact]
    public async Task LoadNotebook_WithUnsupportedVersion_ThrowsInvalidDataException()
    {
        Directory.CreateDirectory(_tempDirectory);
        var path = Path.Combine(_tempDirectory, "unsupported.mqnb");
        await File.WriteAllTextAsync(path, """
            {
              "title": "Unsupported",
              "version": 99,
              "cells": [
                { "id": "cell-1", "source": "Print(\"hi\");", "collapsed": false }
              ]
            }
            """);
        var store = BuildStore();

        var act = () => store.LoadNotebookAsync(path);

        await act.Should().ThrowAsync<InvalidDataException>();
    }

    private QuantScriptNotebookStore BuildStore()
        => new(new QuantScriptOptions
        {
            ScriptsDirectory = _tempDirectory,
            NotebookExtension = ".mqnb"
        });

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, true);
    }
}
