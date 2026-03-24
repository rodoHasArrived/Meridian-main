using System.CommandLine;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DocGenerator;

/// <summary>
/// Generates documentation from code annotations and XML documentation.
/// </summary>
public static partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Generate documentation from code annotations");

        var generateContextCmd = new Command("context", "Generate project-context.md from code");
        var srcOption = new Option<DirectoryInfo>("--src", "Source directory") { IsRequired = true };
        var outputOption = new Option<FileInfo>("--output", "Output file path") { IsRequired = true };
        var xmlDocsOption = new Option<FileInfo[]>("--xml-docs", "XML documentation files") { AllowMultipleArgumentsPerToken = true };

        generateContextCmd.AddOption(srcOption);
        generateContextCmd.AddOption(outputOption);
        generateContextCmd.AddOption(xmlDocsOption);

        generateContextCmd.SetHandler(async (src, output, xmlDocs) =>
        {
            await GenerateProjectContext(src, output, xmlDocs ?? Array.Empty<FileInfo>());
        }, srcOption, outputOption, xmlDocsOption);

        var verifyAdrsCmd = new Command("verify-adrs", "Verify ADR implementation links");
        var adrDirOption = new Option<DirectoryInfo>("--adr-dir", "ADR directory") { IsRequired = true };
        var srcDirOption = new Option<DirectoryInfo>("--src-dir", "Source directory") { IsRequired = true };

        verifyAdrsCmd.AddOption(adrDirOption);
        verifyAdrsCmd.AddOption(srcDirOption);

        verifyAdrsCmd.SetHandler(async (adrDir, srcDir) =>
        {
            var success = await VerifyAdrLinks(adrDir, srcDir);
            Environment.ExitCode = success ? 0 : 1;
        }, adrDirOption, srcDirOption);

        var extractInterfacesCmd = new Command("interfaces", "Extract interface documentation");
        var interfaceSrcOption = new Option<DirectoryInfo>("--src", "Source directory") { IsRequired = true };
        var interfaceOutputOption = new Option<FileInfo>("--output", "Output file") { IsRequired = true };

        extractInterfacesCmd.AddOption(interfaceSrcOption);
        extractInterfacesCmd.AddOption(interfaceOutputOption);

        extractInterfacesCmd.SetHandler(async (src, output) =>
        {
            await ExtractInterfaces(src, output);
        }, interfaceSrcOption, interfaceOutputOption);

        rootCommand.AddCommand(generateContextCmd);
        rootCommand.AddCommand(verifyAdrsCmd);
        rootCommand.AddCommand(extractInterfacesCmd);

        return await rootCommand.InvokeAsync(args);
    }

    private static async Task GenerateProjectContext(DirectoryInfo src, FileInfo output, FileInfo[] xmlDocs)
    {
        Console.WriteLine($"Generating project context from {src.FullName}");

        var sb = new StringBuilder();
        sb.AppendLine("# Meridian Project Context");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("**Source:** Auto-generated from code annotations");
        sb.AppendLine();

        // Load XML documentation
        var xmlDocMap = new Dictionary<string, XElement>();
        foreach (var xmlFile in xmlDocs)
        {
            if (xmlFile.Exists)
            {
                try
                {
                    var doc = XDocument.Load(xmlFile.FullName);
                    var members = doc.Descendants("member");
                    foreach (var member in members)
                    {
                        var name = member.Attribute("name")?.Value;
                        if (name != null)
                        {
                            xmlDocMap[name] = member;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not load {xmlFile.Name}: {ex.Message}");
                }
            }
        }

        // Find interfaces
        var interfaces = await FindInterfacesAsync(src);
        sb.AppendLine("## Key Interfaces");
        sb.AppendLine();

        foreach (var iface in interfaces.OrderBy(i => i.Name))
        {
            sb.AppendLine($"### {iface.Name}");
            sb.AppendLine();
            sb.AppendLine($"**Location:** `{iface.RelativePath}`");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(iface.Summary))
            {
                sb.AppendLine(iface.Summary);
                sb.AppendLine();
            }

            if (iface.Methods.Count > 0)
            {
                sb.AppendLine("| Method | Description |");
                sb.AppendLine("|--------|-------------|");
                foreach (var method in iface.Methods)
                {
                    var desc = method.Summary?.Replace("\n", " ").Trim() ?? "-";
                    sb.AppendLine($"| `{method.Signature}` | {desc} |");
                }
                sb.AppendLine();
            }
        }

        // Find DataSource implementations
        var dataSources = await FindDataSourcesAsync(src);
        if (dataSources.Count > 0)
        {
            sb.AppendLine("## Data Sources");
            sb.AppendLine();
            sb.AppendLine("| Name | Type | Category | Location |");
            sb.AppendLine("|------|------|----------|----------|");

            foreach (var ds in dataSources.OrderBy(d => d.DisplayName))
            {
                sb.AppendLine($"| {ds.DisplayName} | {ds.Type} | {ds.Category} | `{ds.RelativePath}` |");
            }
            sb.AppendLine();
        }

        // Find ADR implementations
        var adrImpls = await FindAdrImplementationsAsync(src);
        if (adrImpls.Count > 0)
        {
            sb.AppendLine("## ADR Implementations");
            sb.AppendLine();

            foreach (var group in adrImpls.GroupBy(a => a.AdrId).OrderBy(g => g.Key))
            {
                sb.AppendLine($"### {group.Key}");
                sb.AppendLine();
                sb.AppendLine("| Type | Location | Description |");
                sb.AppendLine("|------|----------|-------------|");

                foreach (var impl in group.OrderBy(i => i.TypeName))
                {
                    var desc = impl.Description ?? "-";
                    sb.AppendLine($"| `{impl.TypeName}` | `{impl.RelativePath}` | {desc} |");
                }
                sb.AppendLine();
            }
        }

        // Write output
        output.Directory?.Create();
        await File.WriteAllTextAsync(output.FullName, sb.ToString());
        Console.WriteLine($"Generated {output.FullName}");
    }

    private static async Task<bool> VerifyAdrLinks(DirectoryInfo adrDir, DirectoryInfo srcDir)
    {
        Console.WriteLine($"Verifying ADR links in {adrDir.FullName}");

        var adrFiles = adrDir.GetFiles("*.md", SearchOption.TopDirectoryOnly)
            .Where(f => !f.Name.StartsWith("_") && f.Name != "README.md");

        var errors = new List<string>();
        var linkPattern = LinkPatternRegex();

        foreach (var adrFile in adrFiles)
        {
            Console.WriteLine($"Checking {adrFile.Name}...");
            var content = await File.ReadAllTextAsync(adrFile.FullName);

            // Find implementation links table
            var inLinksSection = false;
            var lines = content.Split('\n');

            foreach (var line in lines)
            {
                if (line.Contains("## Implementation Links"))
                {
                    inLinksSection = true;
                    continue;
                }

                if (inLinksSection && line.StartsWith("## "))
                {
                    inLinksSection = false;
                    continue;
                }

                if (inLinksSection && line.StartsWith("|") && !line.Contains("---"))
                {
                    // Parse table row
                    var matches = linkPattern.Matches(line);
                    foreach (Match match in matches)
                    {
                        var path = match.Groups[1].Value;
                        if (path.Contains("/") && !path.StartsWith("http"))
                        {
                            // Extract file path (remove line number if present)
                            var filePath = path.Split(':')[0];
                            var fullPath = Path.Combine(srcDir.Parent?.FullName ?? srcDir.FullName, filePath);

                            if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
                            {
                                errors.Add($"{adrFile.Name}: Missing implementation link: {path}");
                            }
                        }
                    }
                }
            }
        }

        if (errors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("VERIFICATION FAILED:");
            foreach (var error in errors)
            {
                Console.WriteLine($"  - {error}");
            }
            return false;
        }

        Console.WriteLine("All ADR links verified successfully!");
        return true;
    }

    private static async Task ExtractInterfaces(DirectoryInfo src, FileInfo output)
    {
        var interfaces = await FindInterfacesAsync(src);

        var sb = new StringBuilder();
        sb.AppendLine("# Interface Reference");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();

        foreach (var iface in interfaces.OrderBy(i => i.Name))
        {
            sb.AppendLine($"## {iface.Name}");
            sb.AppendLine();
            sb.AppendLine($"**File:** `{iface.RelativePath}`");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(iface.Summary))
            {
                sb.AppendLine(iface.Summary);
                sb.AppendLine();
            }

            sb.AppendLine("```csharp");
            sb.AppendLine(iface.Code);
            sb.AppendLine("```");
            sb.AppendLine();
        }

        output.Directory?.Create();
        await File.WriteAllTextAsync(output.FullName, sb.ToString());
        Console.WriteLine($"Generated {output.FullName}");
    }

    private static async Task<List<InterfaceInfo>> FindInterfacesAsync(DirectoryInfo src)
    {
        var interfaces = new List<InterfaceInfo>();
        var files = src.GetFiles("I*.cs", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains("obj") && !f.FullName.Contains("bin"));

        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file.FullName);

            // Simple regex to find interfaces
            var interfaceMatch = InterfaceRegex().Match(content);
            if (interfaceMatch.Success)
            {
                var name = interfaceMatch.Groups[1].Value;
                var summary = ExtractSummary(content, interfaceMatch.Index);
                var methods = ExtractMethods(content);
                var relativePath = Path.GetRelativePath(src.Parent?.FullName ?? src.FullName, file.FullName);

                // Extract interface code block
                var codeStart = interfaceMatch.Index;
                var braceCount = 0;
                var codeEnd = codeStart;
                var foundFirst = false;

                for (var i = codeStart; i < content.Length; i++)
                {
                    if (content[i] == '{')
                    {
                        braceCount++;
                        foundFirst = true;
                    }
                    else if (content[i] == '}')
                    {
                        braceCount--;
                        if (foundFirst && braceCount == 0)
                        {
                            codeEnd = i + 1;
                            break;
                        }
                    }
                }

                var code = content.Substring(codeStart, codeEnd - codeStart);

                interfaces.Add(new InterfaceInfo(name, relativePath, summary, code, methods));
            }
        }

        return interfaces;
    }

    private static async Task<List<DataSourceInfo>> FindDataSourcesAsync(DirectoryInfo src)
    {
        var dataSources = new List<DataSourceInfo>();
        var files = src.GetFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains("obj") && !f.FullName.Contains("bin"));

        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file.FullName);

            var match = DataSourceRegex().Match(content);
            if (match.Success)
            {
                var id = match.Groups[1].Value;
                var displayName = match.Groups[2].Value;
                var type = match.Groups[3].Value;
                var category = match.Groups[4].Value;
                var relativePath = Path.GetRelativePath(src.Parent?.FullName ?? src.FullName, file.FullName);

                dataSources.Add(new DataSourceInfo(id, displayName, type, category, relativePath));
            }
        }

        return dataSources;
    }

    private static async Task<List<AdrImplInfo>> FindAdrImplementationsAsync(DirectoryInfo src)
    {
        var impls = new List<AdrImplInfo>();
        var files = src.GetFiles("*.cs", SearchOption.AllDirectories)
            .Where(f => !f.FullName.Contains("obj") && !f.FullName.Contains("bin"));

        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file.FullName);

            var matches = AdrImplRegex().Matches(content);
            foreach (Match match in matches)
            {
                var adrId = match.Groups[1].Value;
                var description = match.Groups.Count > 2 ? match.Groups[2].Value : null;

                // Find the class name following the attribute
                var classMatch = ClassNameRegex().Match(content, match.Index);
                if (classMatch.Success)
                {
                    var typeName = classMatch.Groups[1].Value;
                    var relativePath = Path.GetRelativePath(src.Parent?.FullName ?? src.FullName, file.FullName);

                    impls.Add(new AdrImplInfo(adrId, typeName, description?.Trim('"'), relativePath));
                }
            }
        }

        return impls;
    }

    private static string? ExtractSummary(string content, int beforeIndex)
    {
        var lines = content.Substring(0, beforeIndex).Split('\n');
        var summaryLines = new List<string>();
        var inSummary = false;

        for (var i = lines.Length - 1; i >= 0 && i > lines.Length - 20; i--)
        {
            var line = lines[i].Trim();

            if (line.Contains("</summary>"))
            {
                inSummary = true;
                continue;
            }

            if (line.Contains("<summary>"))
            {
                inSummary = false;
                break;
            }

            if (inSummary)
            {
                var text = line.TrimStart('/').Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    summaryLines.Insert(0, text);
                }
            }
        }

        return summaryLines.Count > 0 ? string.Join(" ", summaryLines) : null;
    }

    private static List<MethodInfo> ExtractMethods(string content)
    {
        var methods = new List<MethodInfo>();
        var matches = MethodRegex().Matches(content);

        foreach (Match match in matches)
        {
            var signature = match.Value.Trim().TrimEnd(';');
            var summary = ExtractSummary(content, match.Index);
            methods.Add(new MethodInfo(signature, summary));
        }

        return methods;
    }

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex LinkPatternRegex();

    [GeneratedRegex(@"public\s+interface\s+(\w+)")]
    private static partial Regex InterfaceRegex();

    [GeneratedRegex(@"\[DataSource\s*\(\s*""([^""]+)""\s*,\s*""([^""]+)""\s*,\s*DataSourceType\.(\w+)\s*,\s*DataSourceCategory\.(\w+)")]
    private static partial Regex DataSourceRegex();

    [GeneratedRegex(@"\[ImplementsAdr\s*\(\s*""([^""]+)""(?:\s*,\s*""([^""]+)"")?\s*\)\]")]
    private static partial Regex AdrImplRegex();

    [GeneratedRegex(@"(?:public\s+)?(?:sealed\s+)?(?:abstract\s+)?class\s+(\w+)")]
    private static partial Regex ClassNameRegex();

    [GeneratedRegex(@"(?:Task|IAsyncEnumerable|void|int|bool|string)[<\w,\s>]*\s+\w+\s*\([^)]*\)\s*;")]
    private static partial Regex MethodRegex();
}

internal sealed record InterfaceInfo(
    string Name,
    string RelativePath,
    string? Summary,
    string Code,
    List<MethodInfo> Methods);

internal sealed record MethodInfo(string Signature, string? Summary);

internal sealed record DataSourceInfo(
    string Id,
    string DisplayName,
    string Type,
    string Category,
    string RelativePath);

internal sealed record AdrImplInfo(
    string AdrId,
    string TypeName,
    string? Description,
    string RelativePath);
