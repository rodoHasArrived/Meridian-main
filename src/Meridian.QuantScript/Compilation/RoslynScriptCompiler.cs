using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Compiles QuantScript (.csx) source files using the Roslyn scripting API.
/// Results are cached by SHA-256 of source text to avoid redundant recompilation.
/// </summary>
public sealed class RoslynScriptCompiler : IQuantScriptCompiler
{
    // Parameter comment convention: // @param Name:Label:Default:Min:Max:Description
    private static readonly Regex ParamRegex = new(
        @"//\s*@param\s+(\w+):([^:]*):([^:]*):([^:]*):([^:]*):?(.*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TypeRegex = new(
        @"^\s*(var|int|double|decimal|string|bool|float|long)\s+(\w+)\s*=",
        RegexOptions.Compiled);

    private static readonly Regex ScriptParamFallbackRegex = new(
        @"\[ScriptParam(?:Attribute)?\((?<args>.*?)\)\]\s*(?<type>var|int|double|decimal|string|bool|float|long)\s+(?<name>\w+)\s*=\s*(?<value>[^;]+);",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ScriptParamNamedArgumentRegex = new(
        @"(?<name>Default|Min|Max|Description)\s*=\s*(?<value>(""[^""]*""|[^,]+))",
        RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, Script<object>> _cache = new();
    private readonly IOptions<QuantScriptOptions> _options;
    private readonly ILogger<RoslynScriptCompiler> _logger;

    public RoslynScriptCompiler(ILogger<RoslynScriptCompiler> logger)
        : this(Microsoft.Extensions.Options.Options.Create(new QuantScriptOptions()), logger) { }

    public RoslynScriptCompiler(IOptions<QuantScriptOptions> options, ILogger<RoslynScriptCompiler> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<ScriptCompilationResult> CompileAsync(
        string source, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var key = ComputeHash(source);

        if (_cache.ContainsKey(key))
        {
            _logger.LogDebug("Script cache hit for hash {Hash}", key[..8]);
            return new ScriptCompilationResult(true, TimeSpan.Zero, Array.Empty<ScriptDiagnostic>());
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.Value.CompilationTimeoutSeconds));

        try
        {
            return await Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var script = BuildScript(source);
                var compilation = script.GetCompilation();
                var diagnostics = compilation.GetDiagnostics(cts.Token);
                sw.Stop();

                var errors = diagnostics
                    .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
                    .Select(d =>
                    {
                        var loc = d.Location.GetLineSpan();
                        return new ScriptDiagnostic(
                            "Error",
                            d.GetMessage(),
                            loc.StartLinePosition.Line + 1,
                            loc.StartLinePosition.Character + 1);
                    })
                    .ToList();

                if (errors.Count > 0)
                {
                    _logger.LogWarning("Script compilation failed with {Count} error(s)", errors.Count);
                    return new ScriptCompilationResult(false, sw.Elapsed, errors);
                }

                _cache.TryAdd(key, script);
                _logger.LogDebug("Script compiled in {ElapsedMs}ms", sw.ElapsedMilliseconds);
                return new ScriptCompilationResult(true, sw.Elapsed, Array.Empty<ScriptDiagnostic>());
            }, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ct.ThrowIfCancellationRequested();
            return new ScriptCompilationResult(false, TimeSpan.Zero,
                [new ScriptDiagnostic("Error", "Compilation timed out", 0, 0)]);
        }
    }

    /// <summary>
    /// Returns the cached compiled <see cref="Script{TResult}"/> for the given source,
    /// or null if not yet compiled. Call <see cref="CompileAsync"/> first.
    /// </summary>
    internal Script<object>? GetCachedScript(string source)
    {
        var key = ComputeHash(source);
        return _cache.TryGetValue(key, out var script) ? script : null;
    }

    /// <summary>Builds (but does not cache) a Roslyn Script from source text.</summary>
    internal Script<object> BuildScript(string source) =>
        CSharpScript.Create<object>(
            source,
            BuildScriptOptions(),
            globalsType: typeof(QuantScriptGlobals));

    private static ScriptOptions BuildScriptOptions() =>
        ScriptOptions.Default
            .AddReferences(
                typeof(QuantScriptGlobals).Assembly,
                typeof(Backtesting.Engine.BacktestEngine).Assembly,
                typeof(Backtesting.Sdk.IBacktestStrategy).Assembly,
                typeof(Contracts.Domain.Models.HistoricalBar).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "System.Threading",
                "System.Threading.Tasks",
                "Meridian.QuantScript.Api",
                "Meridian.Backtesting.Sdk",
                "Meridian.Contracts.Domain.Models");

    /// <inheritdoc/>
    public IReadOnlyList<ParameterDescriptor> ExtractParameters(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new List<ParameterDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(kind: SourceCodeKind.Script));
        var root = syntaxTree.GetRoot();

        AddDescriptors(result, seen, ExtractParamCallDescriptors(root));
        AddDescriptors(result, seen, ExtractScriptParamDescriptors(root));
        AddDescriptors(result, seen, ExtractScriptParamDescriptorsFallback(source));
        AddDescriptors(result, seen, ExtractLegacyCommentDescriptors(source));

        return result;
    }

    private static string ComputeHash(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static void AddDescriptors(
        ICollection<ParameterDescriptor> destination,
        ISet<string> seen,
        IEnumerable<ParameterDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            if (string.IsNullOrWhiteSpace(descriptor.Name) || !seen.Add(descriptor.Name))
                continue;

            destination.Add(descriptor);
        }
    }

    private static IEnumerable<ParameterDescriptor> ExtractParamCallDescriptors(SyntaxNode root)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!TryGetParamInvocation(invocation, out var genericName))
                continue;

            if (genericName.TypeArgumentList.Arguments.Count != 1)
                continue;

            var arguments = invocation.ArgumentList.Arguments;
            if (arguments.Count == 0 || !TryReadStringLiteral(arguments[0].Expression, out var name))
                continue;

            var typeName = NormalizeTypeName(genericName.TypeArgumentList.Arguments[0]);
            var defaultValue = arguments.Count > 1
                ? ConvertLiteralValue(arguments[1].Expression, typeName)
                : null;
            var min = arguments.Count > 2 && TryReadDouble(arguments[2].Expression, out var minValue)
                ? minValue
                : double.MinValue;
            var max = arguments.Count > 3 && TryReadDouble(arguments[3].Expression, out var maxValue)
                ? maxValue
                : double.MaxValue;
            var description = arguments.Count > 4 && TryReadStringLiteral(arguments[4].Expression, out var descriptionText)
                ? descriptionText
                : null;

            yield return new ParameterDescriptor(
                Name: name!,
                TypeName: typeName,
                Label: name!,
                DefaultValue: defaultValue,
                Min: min,
                Max: max,
                Description: string.IsNullOrWhiteSpace(description) ? null : description);
        }
    }

    private static IEnumerable<ParameterDescriptor> ExtractScriptParamDescriptors(SyntaxNode root)
    {
        foreach (var declaration in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (declaration.AttributeLists.Count == 0)
                continue;

            var scriptParamAttribute = declaration.AttributeLists
                .SelectMany(static list => list.Attributes)
                .FirstOrDefault(IsScriptParamAttribute);

            if (scriptParamAttribute is null)
                continue;

            var attributeValues = ReadScriptParamAttributeValues(scriptParamAttribute);
            var typeName = NormalizeTypeName(declaration.Declaration.Type);

            foreach (var variable in declaration.Declaration.Variables)
            {
                var name = variable.Identifier.ValueText;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var defaultValue = attributeValues.DefaultValue
                    ?? ConvertLiteralValue(variable.Initializer?.Value, typeName);

                yield return new ParameterDescriptor(
                    Name: name,
                    TypeName: typeName,
                    Label: attributeValues.Label ?? name,
                    DefaultValue: defaultValue,
                    Min: attributeValues.Min ?? double.MinValue,
                    Max: attributeValues.Max ?? double.MaxValue,
                    Description: attributeValues.Description);
            }
        }
    }

    private static IEnumerable<ParameterDescriptor> ExtractScriptParamDescriptorsFallback(string source)
    {
        foreach (Match match in ScriptParamFallbackRegex.Matches(source))
        {
            if (!match.Success)
                continue;

            var name = match.Groups["name"].Value.Trim();
            var typeName = match.Groups["type"].Value.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(typeName))
                continue;

            var attributeArguments = match.Groups["args"].Value;
            var defaultValue = ConvertLegacyDefaultValue(match.Groups["value"].Value.Trim(), typeName);
            double? min = null;
            double? max = null;
            string? description = null;
            string? label = null;

            var constructorMatch = Regex.Match(attributeArguments, @"^\s*""(?<label>[^""]*)""");
            if (constructorMatch.Success)
                label = constructorMatch.Groups["label"].Value.Trim();

            foreach (Match argumentMatch in ScriptParamNamedArgumentRegex.Matches(attributeArguments))
            {
                var argumentName = argumentMatch.Groups["name"].Value.Trim();
                var argumentValue = argumentMatch.Groups["value"].Value.Trim();
                switch (argumentName)
                {
                    case "Default":
                        defaultValue = ConvertLegacyDefaultValue(argumentValue.Trim('"'), typeName);
                        break;
                    case "Min" when double.TryParse(argumentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var minValue):
                        min = minValue;
                        break;
                    case "Max" when double.TryParse(argumentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxValue):
                        max = maxValue;
                        break;
                    case "Description":
                        description = argumentValue.Trim().Trim('"');
                        break;
                }
            }

            yield return new ParameterDescriptor(
                Name: name,
                TypeName: typeName,
                Label: string.IsNullOrWhiteSpace(label) ? name : label,
                DefaultValue: defaultValue,
                Min: min ?? double.MinValue,
                Max: max ?? double.MaxValue,
                Description: string.IsNullOrWhiteSpace(description) ? null : description);
        }
    }

    private static IEnumerable<ParameterDescriptor> ExtractLegacyCommentDescriptors(string source)
    {
        var result = new List<ParameterDescriptor>();
        var lines = source.Split('\n');

        foreach (var line in lines)
        {
            var match = ParamRegex.Match(line);
            if (!match.Success)
                continue;

            var name = match.Groups[1].Value.Trim();
            var label = match.Groups[2].Value.Trim();
            var defaultStr = match.Groups[3].Value.Trim();
            _ = double.TryParse(match.Groups[4].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var min);
            _ = double.TryParse(match.Groups[5].Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var max);
            var description = match.Groups[6].Value.Trim();

            var typeName = "string";
            for (var i = 0; i < lines.Length; i++)
            {
                var typeMatch = TypeRegex.Match(lines[i]);
                if (typeMatch.Success && typeMatch.Groups[2].Value == name)
                {
                    typeName = typeMatch.Groups[1].Value;
                    break;
                }
            }

            result.Add(new ParameterDescriptor(
                Name: name,
                TypeName: typeName,
                Label: label.Length > 0 ? label : name,
                DefaultValue: ConvertLegacyDefaultValue(defaultStr, typeName),
                Min: min,
                Max: max,
                Description: description.Length > 0 ? description : null));
        }

        return result;
    }

    private static bool TryGetParamInvocation(
        InvocationExpressionSyntax invocation,
        out GenericNameSyntax genericName)
    {
        genericName = invocation.Expression switch
        {
            GenericNameSyntax directGeneric => directGeneric,
            IdentifierNameSyntax => null!,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax memberGeneric } => memberGeneric,
            _ => null!
        };

        if (genericName is null || !string.Equals(genericName.Identifier.ValueText, "Param", StringComparison.Ordinal))
            return false;

        return true;
    }

    private static bool IsScriptParamAttribute(AttributeSyntax attribute)
    {
        var name = attribute.Name switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
            _ => attribute.Name.ToString()
        };

        return string.Equals(name, "ScriptParam", StringComparison.Ordinal)
            || string.Equals(name, "ScriptParamAttribute", StringComparison.Ordinal);
    }

    private static (string? Label, object? DefaultValue, double? Min, double? Max, string? Description)
        ReadScriptParamAttributeValues(AttributeSyntax attribute)
    {
        string? label = null;
        object? defaultValue = null;
        double? min = null;
        double? max = null;
        string? description = null;

        if (attribute.ArgumentList is null)
            return (label, defaultValue, min, max, description);

        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            if (argument.NameEquals is null)
            {
                if (label is null && TryReadStringLiteral(argument.Expression, out var constructorLabel))
                    label = constructorLabel;

                continue;
            }

            var argumentName = argument.NameEquals.Name.Identifier.ValueText;
            switch (argumentName)
            {
                case "Default":
                    defaultValue = ConvertRawLiteralValue(argument.Expression);
                    break;
                case "Min" when TryReadDouble(argument.Expression, out var minValue):
                    min = minValue;
                    break;
                case "Max" when TryReadDouble(argument.Expression, out var maxValue):
                    max = maxValue;
                    break;
                case "Description" when TryReadStringLiteral(argument.Expression, out var descriptionValue):
                    description = descriptionValue;
                    break;
            }
        }

        return (label, defaultValue, min, max, description);
    }

    private static object? ConvertLegacyDefaultValue(string defaultValue, string typeName)
        => typeName switch
        {
            "int" => int.TryParse(defaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue) ? intValue : null,
            "double" or "float" => double.TryParse(defaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) ? doubleValue : null,
            "decimal" => decimal.TryParse(defaultValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue) ? decimalValue : null,
            "bool" => bool.TryParse(defaultValue, out var boolValue) ? boolValue : null,
            _ => defaultValue.Length > 0 ? defaultValue : null
        };

    private static object? ConvertLiteralValue(ExpressionSyntax? expression, string typeName)
    {
        var rawValue = ConvertRawLiteralValue(expression);
        if (rawValue is null)
            return null;

        try
        {
            return typeName switch
            {
                "int" => Convert.ToInt32(rawValue, CultureInfo.InvariantCulture),
                "double" => Convert.ToDouble(rawValue, CultureInfo.InvariantCulture),
                "float" => Convert.ToSingle(rawValue, CultureInfo.InvariantCulture),
                "decimal" => Convert.ToDecimal(rawValue, CultureInfo.InvariantCulture),
                "bool" => Convert.ToBoolean(rawValue, CultureInfo.InvariantCulture),
                "long" => Convert.ToInt64(rawValue, CultureInfo.InvariantCulture),
                "string" => Convert.ToString(rawValue, CultureInfo.InvariantCulture),
                _ => rawValue
            };
        }
        catch
        {
            return rawValue;
        }
    }

    private static object? ConvertRawLiteralValue(ExpressionSyntax? expression)
        => expression switch
        {
            null => null,
            LiteralExpressionSyntax literal => literal.Token.Value,
            PrefixUnaryExpressionSyntax
            {
                OperatorToken.RawKind: (int)SyntaxKind.MinusToken,
                Operand: LiteralExpressionSyntax literalOperand
            } when literalOperand.Token.Value is int intValue => -intValue,
            PrefixUnaryExpressionSyntax
            {
                OperatorToken.RawKind: (int)SyntaxKind.MinusToken,
                Operand: LiteralExpressionSyntax literalOperand
            } when literalOperand.Token.Value is long longValue => -longValue,
            PrefixUnaryExpressionSyntax
            {
                OperatorToken.RawKind: (int)SyntaxKind.MinusToken,
                Operand: LiteralExpressionSyntax literalOperand
            } when literalOperand.Token.Value is float floatValue => -floatValue,
            PrefixUnaryExpressionSyntax
            {
                OperatorToken.RawKind: (int)SyntaxKind.MinusToken,
                Operand: LiteralExpressionSyntax literalOperand
            } when literalOperand.Token.Value is double doubleValue => -doubleValue,
            PrefixUnaryExpressionSyntax
            {
                OperatorToken.RawKind: (int)SyntaxKind.MinusToken,
                Operand: LiteralExpressionSyntax literalOperand
            } when literalOperand.Token.Value is decimal decimalValue => -decimalValue,
            _ => null
        };

    private static bool TryReadStringLiteral(ExpressionSyntax expression, out string? value)
    {
        value = ConvertRawLiteralValue(expression) as string;
        return value is not null;
    }

    private static bool TryReadDouble(ExpressionSyntax expression, out double value)
    {
        var raw = ConvertRawLiteralValue(expression);
        switch (raw)
        {
            case double doubleValue:
                value = doubleValue;
                return true;
            case float floatValue:
                value = floatValue;
                return true;
            case decimal decimalValue:
                value = (double)decimalValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                value = parsed;
                return true;
            default:
                value = default;
                return false;
        }
    }

    private static string NormalizeTypeName(TypeSyntax typeSyntax)
    {
        var raw = typeSyntax.ToString().Trim();
        return raw switch
        {
            "int" or "Int32" or "System.Int32" => "int",
            "double" or "Double" or "System.Double" => "double",
            "float" or "Single" or "System.Single" => "float",
            "decimal" or "Decimal" or "System.Decimal" => "decimal",
            "bool" or "Boolean" or "System.Boolean" => "bool",
            "long" or "Int64" or "System.Int64" => "long",
            "string" or "String" or "System.String" => "string",
            _ => raw
        };
    }
}
