using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meridian.Contracts.Integrity;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Scripting;

namespace Meridian.QuantScript.Compilation;

/// <summary>
/// Compiles QuantScript (.csx) source files using the Roslyn scripting API.
/// Results are cached by SHA-256 of source text to avoid redundant recompilation; the cache
/// is bounded (see <see cref="QuantScriptOptions.MaxCachedCompilations"/>) so retained Roslyn
/// compilation graphs cannot accumulate without limit.
/// <para>
/// When unsafe scripts are disabled the compiler applies a best-effort advisory guard against
/// File/Network/Process/Reflection usage. That guard is a source-level denylist, not a security
/// sandbox — see <see cref="QuantScriptOptions.EnableUnsafeScripts"/>.
/// </para>
/// </summary>
public sealed class RoslynScriptCompiler : IQuantScriptCompiler
{
    private static readonly SourceReferenceResolver SafeSourceReferenceResolver = new RestrictedSourceReferenceResolver();

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

    private static readonly Regex ScriptReferenceDirectiveRegex = new(
        @"^\s*#\s*r\b",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ScriptLoadDirectiveRegex = new(
        @"^\s*#\s*load\b",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Advisory-only denylist. Substring matching is deliberately simple and can be bypassed by a
    // determined author (e.g. reflection or runtime type resolution); it exists to catch honest
    // mistakes, not to sandbox untrusted code. See QuantScriptOptions.EnableUnsafeScripts.
    private static readonly string[] UnsafeApiMarkers =
    [
        "System.IO.",
        "System.Net.",
        "System.Diagnostics.Process",
        "System.Environment",
        "System.Reflection",
        "Microsoft.Win32"
    ];

    private readonly ConcurrentDictionary<string, Script<object>> _cache = new();
    // Tracks insertion order so the cache can evict oldest-first once it exceeds the configured
    // bound; each cached Script<object> retains a Roslyn compilation graph.
    private readonly ConcurrentQueue<string> _cacheInsertionOrder = new();
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

                if (!_options.Value.EnableUnsafeScripts && TryCreateSafeModeDiagnostic(source) is { } diagnostic)
                {
                    return new ScriptCompilationResult(false, sw.Elapsed, [diagnostic]);
                }

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

                CacheScript(key, script);
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
    /// Caches a compiled script, evicting the oldest entries (FIFO) once the cache exceeds
    /// <see cref="QuantScriptOptions.MaxCachedCompilations"/>. A non-positive bound disables
    /// caching. Bounding is required because each <see cref="Script{TResult}"/> retains a Roslyn
    /// compilation graph, so an unbounded cache leaks memory on long-running hosts.
    /// </summary>
    private void CacheScript(string key, Script<object> script)
    {
        var max = _options.Value.MaxCachedCompilations;
        if (max <= 0)
            return;

        if (!_cache.TryAdd(key, script))
            return;

        _cacheInsertionOrder.Enqueue(key);

        while (_cache.Count > max && _cacheInsertionOrder.TryDequeue(out var oldestKey))
        {
            _cache.TryRemove(oldestKey, out _);
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

    private ScriptOptions BuildScriptOptions()
    {
        var scriptOptions = ScriptOptions.Default
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

        if (!_options.Value.EnableUnsafeScripts)
        {
            scriptOptions = scriptOptions
                .WithSourceResolver(SafeSourceReferenceResolver);
        }

        return scriptOptions;
    }

    private sealed class RestrictedSourceReferenceResolver : SourceReferenceResolver
    {
        public override bool Equals(object? other) => other is RestrictedSourceReferenceResolver;

        public override int GetHashCode() => typeof(RestrictedSourceReferenceResolver).GetHashCode();

        public override string? NormalizePath(string path, string? baseFilePath) => null;

        public override Stream OpenRead(string resolvedPath)
        {
            throw new InvalidOperationException("Script-level source loading is disabled in safe mode.");
        }

        public override string? ResolveReference(string path, string? baseFilePath) => null;
    }

    internal static ScriptDiagnostic? TryCreateSafeModeDiagnostic(string source)
    {
        return TryGetUnsafeMarker(source, out var marker)
            ? new ScriptDiagnostic(
                "Error",
                $"Safe mode blocks use of '{marker}' because it is disabled in safe mode. Set EnableUnsafeScripts=true to allow this script.",
                1,
                1)
            : null;
    }

    private static bool TryGetUnsafeMarker(string source, out string marker)
    {
        if (ScriptReferenceDirectiveRegex.IsMatch(source))
        {
            marker = "#r";
            return true;
        }

        if (ScriptLoadDirectiveRegex.IsMatch(source))
        {
            marker = "#load";
            return true;
        }

        foreach (var candidate in UnsafeApiMarkers)
        {
            if (source.Contains(candidate, StringComparison.Ordinal))
            {
                marker = candidate;
                return true;
            }
        }

        marker = string.Empty;
        return false;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ParameterDescriptor> ExtractParameters(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new List<ParameterDescriptor>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(kind: SourceCodeKind.Script));
        var root = syntaxTree.GetRoot();
        var externalDirective = root.DescendantTrivia(descendIntoTrivia: true)
            .FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.LoadDirectiveTrivia)
                || trivia.IsKind(SyntaxKind.ReferenceDirectiveTrivia));
        if (externalDirective.GetStructure() is { } directive)
            throw IncompleteParameter(directive, "External source and reference directives are not supported by static parameter discovery");

        AddDescriptors(result, seen, ExtractParamCallDescriptors(source, root));
        AddDescriptors(result, seen, ExtractScriptParamDescriptors(root));
        AddDescriptors(result, seen, ExtractScriptParamDescriptorsFallback(source));
        AddDescriptors(result, seen, ExtractLegacyCommentDescriptors(source));

        return result;
    }

    private static string ComputeHash(string source) => Sha256Digest.ComputeUtf8(source);

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

    private IEnumerable<ParameterDescriptor> ExtractParamCallDescriptors(string source, SyntaxNode root)
    {
        if (!root.DescendantNodes().OfType<SimpleNameSyntax>().Any(IsParamName))
            return [];

        // Resolve actual globals calls instead of guessing from argument positions or generic
        // syntax. This handles inferred type arguments, named arguments and optional defaults
        // without executing source. Unknown metadata must never authorize a partial sidebar.
        var compilation = BuildScript(source).GetCompilation();
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var descriptors = new Dictionary<string, ParameterDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>().Where(IsParamName))
        {
            var expression = name.Parent is MemberAccessExpressionSyntax or MemberBindingExpressionSyntax
                ? name.Parent
                : name;
            var invocation = expression.Parent as InvocationExpressionSyntax;
            var symbol = model.GetSymbolInfo(name).Symbol;
            if (symbol is not null && !IsGlobalsParam(symbol))
                continue;

            if (invocation is null || model.GetOperation(invocation) is not IInvocationOperation operation
                || !IsGlobalsParam(operation.TargetMethod))
            {
                throw IncompleteParameter(name, "Param must be a directly bound globals call with statically known metadata");
            }

            var parameterType = operation.TargetMethod.TypeArguments.Single();
            var typeName = parameterType.SpecialType switch
            {
                SpecialType.System_Int32 => "int",
                SpecialType.System_Int64 => "long",
                SpecialType.System_Single => "float",
                SpecialType.System_Double => "double",
                SpecialType.System_Decimal => "decimal",
                SpecialType.System_Boolean => "bool",
                SpecialType.System_String => "string",
                _ => throw IncompleteParameter(name, "Param type must be a supported scalar type")
            };
            var arguments = operation.Arguments.ToDictionary(argument => argument.Parameter!.Ordinal);
            if (arguments.Count != 5 || !arguments.Keys.Order().SequenceEqual(Enumerable.Range(0, 5)))
                throw IncompleteParameter(name, "Param arguments could not be resolved completely");

            object? ReadConstant(int ordinal)
            {
                var argument = arguments[ordinal];
                if (!argument.Value.ConstantValue.HasValue)
                    throw IncompleteParameter(name, $"Param argument '{argument.Parameter!.Name}' must be a compile-time constant");
                return argument.Value.ConstantValue.Value;
            }

            if (ReadConstant(0) is not string parameterName || string.IsNullOrWhiteSpace(parameterName)
                || parameterName != parameterName.Trim())
                throw IncompleteParameter(name, "Param name must be a nonempty constant string without surrounding whitespace");
            var defaultValue = arguments[1].ArgumentKind == ArgumentKind.DefaultValue
                ? typeName switch
                {
                    "int" => (object)0,
                    "long" => 0L,
                    "float" => 0f,
                    "double" => 0d,
                    "decimal" => 0m,
                    "bool" => false,
                    _ => null
                }
                : ReadConstant(1);
            if (defaultValue is double doubleValue && !double.IsFinite(doubleValue)
                || defaultValue is float floatValue && !float.IsFinite(floatValue))
                throw IncompleteParameter(name, "Param default must be finite");

            if (ReadConstant(2) is not double min || !double.IsFinite(min)
                || ReadConstant(3) is not double max || !double.IsFinite(max) || min > max)
                throw IncompleteParameter(name, "Param bounds must be finite constants with min <= max");
            var description = ReadConstant(4) as string;
            var descriptor = new ParameterDescriptor(parameterName, typeName, parameterName, defaultValue,
                min, max, string.IsNullOrWhiteSpace(description) ? null : description.Trim());
            if (descriptors.TryGetValue(parameterName, out var existing) && existing != descriptor)
                throw IncompleteParameter(name, $"Param '{parameterName}' has conflicting declarations");
            descriptors[parameterName] = descriptor;
        }

        return descriptors.Values;
    }

    private static bool IsParamName(SimpleNameSyntax name) =>
        string.Equals(name.Identifier.ValueText, "Param", StringComparison.Ordinal);

    private static bool IsGlobalsParam(ISymbol symbol) =>
        symbol is IMethodSymbol { Name: "Param" } method
        && method.ContainingType.ToDisplayString() == typeof(QuantScriptGlobals).FullName;

    private static ParameterExtractionException IncompleteParameter(SyntaxNode node, string reason)
    {
        var location = node.GetLocation().GetLineSpan().StartLinePosition;
        return new ParameterExtractionException(
            $"Parameter extraction is incomplete at line {location.Line + 1}, column {location.Character + 1}: {reason}.");
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
