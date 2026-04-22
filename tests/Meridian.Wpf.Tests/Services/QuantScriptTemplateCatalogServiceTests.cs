#if WINDOWS
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Meridian.QuantScript.Documents;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

public sealed class QuantScriptTemplateCatalogServiceTests
{
    [Fact]
    public async Task LoadTemplateAsync_ReturnsTemplateSourceFromCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "quant-script-template-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(root, "catalog.json"),
                JsonSerializer.Serialize(
                    new QuantScriptTemplateCatalogManifest(
                    [
                        new QuantScriptTemplateDefinition(
                            "hello-spy",
                            "Hello SPY",
                            "Loads SPY",
                            QuantScriptDocumentKind.LegacyScript,
                            "hello-spy.csx",
                            "Getting Started")
                    ]),
                    QuantScriptStorageJsonContext.Default.QuantScriptTemplateCatalogManifest));
            await File.WriteAllTextAsync(Path.Combine(root, "hello-spy.csx"), "Print(\"hello\");");

            var service = new QuantScriptTemplateCatalogService(
                NullLogger<QuantScriptTemplateCatalogService>.Instance,
                root);

            var templates = service.ListTemplates();
            var document = await service.LoadTemplateAsync("hello-spy");

            templates.Should().ContainSingle();
            document.Definition.Title.Should().Be("Hello SPY");
            document.Source.Should().Be("Print(\"hello\");");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
#endif
