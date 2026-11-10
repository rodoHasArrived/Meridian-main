using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Meridian.Tests.Integration.EndpointTests;

/// <summary>
/// Route-contract regression for the desktop clients: scans every <c>/api/...</c> string literal
/// in the WPF workstation (<c>src/Meridian.Wpf</c>) and its shared service layer
/// (<c>src/Meridian.Ui.Services</c>) and proves each one resolves to a route template the composed
/// application actually maps, taken from the live <see cref="EndpointDataSource"/> exactly as the
/// authorization coverage sweep does. A desktop screen that calls a route the server never mapped
/// only ever shows its empty state, and nothing else fails — so this test is the failure.
/// <para>
/// Calls built from <c>UiApiRoutes</c> constants carry no <c>/api/</c> literal and are out of scope
/// by construction: the constant is the same one the server maps. Prefer them over new literals.
/// </para>
/// </summary>
public sealed class DesktopClientRouteContractTests : EndpointIntegrationTestBase
{
    public DesktopClientRouteContractTests(EndpointTestFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>Stand-in for an interpolation hole (<c>{expr}</c>) inside a scanned literal.</summary>
    private const char Hole = '\u0001';

    private static readonly string[] ScannedSourceRoots =
    [
        "src/Meridian.Wpf",
        "src/Meridian.Ui.Services",
    ];

    private static readonly Regex StringLiteralPattern = new(
        @"(?<prefix>[$@]*)""(?<body>(?:[^""\\]|\\.)*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex InterpolationHolePattern = new(
        @"\{[^{}]*\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Desktop call sites that target a route the server does not map and that have no clearly
    /// equivalent mapped route today. Each one degrades gracefully on the resulting 404, so the
    /// feature is left in place rather than deleted or pointed at a guessed endpoint. Entries are
    /// keyed by source file and normalized route (query string dropped, interpolation holes shown
    /// as <c>{*}</c>). Do not add to this list to make a new screen pass: map the route on the
    /// server, or call an existing one through its <c>UiApiRoutes</c> constant. Remove an entry
    /// when the server gains the route or the call site is retired.
    /// </summary>
    internal static readonly HashSet<string> KnownUnmappedDesktopRoutes = new(StringComparer.Ordinal)
    {
        // Activity log page polls a server log feed that was never mapped. A non-success response
        // shows the "Offline Mode" notice once and the page keeps its locally captured log entries.
        "src/Meridian.Wpf/ViewModels/ActivityLogViewModel.cs -> /api/logs",
        // Optional watchlist backend sync. A 404 returns false without logging and the service
        // keeps the locally persisted watchlists, which remain the source of truth.
        "src/Meridian.Wpf/Services/WatchlistService.cs -> /api/watchlists",
        // Archive health probe. Any non-success response falls through to the locally calculated
        // health status, so the page still renders.
        "src/Meridian.Wpf/Services/ArchiveHealthService.cs -> /api/archive/health",
    };

    [Fact]
    public void DesktopClientApiLiterals_ResolveToMappedServerRoutes()
    {
        var templates = GetMappedRouteTemplates();
        var literals = ScanDesktopApiLiterals();

        templates.Should().NotBeEmpty();
        literals.Should().NotBeEmpty("the scan must find the desktop clients' /api/ literals; an empty scan means the source roots moved");

        var unmapped = literals
            .Where(literal => !ResolvesToMappedRoute(literal, templates))
            .Where(literal => !KnownUnmappedDesktopRoutes.Contains(literal.Key))
            .Select(literal => $"{literal.Key} (line {literal.Line})")
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        unmapped.Should().BeEmpty(
            "every /api/ literal in the desktop clients must resolve to a route the server maps. " +
            "Point the call at the real route through its UiApiRoutes constant (and check the response shape it returns)");
    }

    [Fact]
    public void KnownUnmappedDesktopRoutes_HasNoStaleEntries()
    {
        var templates = GetMappedRouteTemplates();
        var stillUnmapped = ScanDesktopApiLiterals()
            .Where(literal => !ResolvesToMappedRoute(literal, templates))
            .Select(literal => literal.Key)
            .ToHashSet(StringComparer.Ordinal);

        var stale = KnownUnmappedDesktopRoutes
            .Where(entry => !stillUnmapped.Contains(entry))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToList();

        stale.Should().BeEmpty(
            "an allowlisted desktop route that is now mapped, or whose call site is gone, must leave the allowlist so the ratchet stays tight");
    }

    /// <summary>
    /// Pins the scanner itself against the defect it was written for: the legacy live-data and
    /// workstation corporate-action routes the WPF app used to call must not resolve, and the
    /// routes the server really maps for the same screens must.
    /// </summary>
    [Theory]
    [InlineData("$", "{_baseUrl}/api/data/quotes/{Uri.EscapeDataString(symbol)}", true)]
    [InlineData("$", "{_baseUrl}/api/data/trades/{Uri.EscapeDataString(symbol)}?limit=15", true)]
    [InlineData("$", "{_baseUrl}/api/data/orderbook/{Uri.EscapeDataString(symbol)}?levels={levels}", true)]
    [InlineData("$", "/api/security-master/{securityId}/corporate-actions", true)]
    [InlineData("", "/api/data/quotes-snapshot", true)]
    [InlineData("$", "/api/loans/{loan.LoanId}/unapplied-cash/{(isRefund ? ", true)]
    [InlineData("$", "/api/no-such-area/{loan.LoanId}/unapplied-cash/{(isRefund ? ", false)]
    [InlineData("$", "/api/live/{Uri.EscapeDataString(symbol)}/quote", false)]
    [InlineData("$", "{_baseUrl}/api/live/{Uri.EscapeDataString(symbol)}/recent?limit=50", false)]
    [InlineData("$", "{_baseUrl}/api/live/{Uri.EscapeDataString(symbol)}/trades?limit=15", false)]
    [InlineData("$", "{_baseUrl}/api/orderbook/{Uri.EscapeDataString(symbol)}?levels={levels}", false)]
    [InlineData("$", "/api/workstation/security-master/securities/{securityId}/corporate-actions", false)]
    public void RouteResolution_DistinguishesMappedFromLegacyDesktopRoutes(string prefix, string body, bool expectedMapped)
    {
        var literal = TryNormalize("synthetic.cs", 1, prefix, body);

        literal.Should().NotBeNull();
        ResolvesToMappedRoute(literal!, GetMappedRouteTemplates()).Should().Be(expectedMapped);
    }

    private sealed record ApiLiteral(string File, int Line, string Route, string[] Segments, bool IsPrefix)
    {
        /// <summary>Allowlist key: repository-relative file plus the normalized route.</summary>
        public string Key => $"{File} -> {Route}";
    }

    /// <summary>
    /// Loopback-only lifecycle routes mapped by the host itself
    /// (<c>UiServer.MapLifecycleEndpoints</c> in <c>src/Meridian/UiServer.cs</c>), which the shared
    /// endpoint test fixture does not compose. They are real server routes, so they count as mapped.
    /// </summary>
    private static readonly string[] HostMappedLifecycleRoutes =
    [
        "/api/system/lifecycle",
        "/api/system/shutdown",
        "/api/system/shutdown/{operationId}",
        "/api/system/shutdown/receipts/latest",
    ];

    private IReadOnlyList<string[]> GetMappedRouteTemplates()
        => Fixture.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Concat(HostMappedLifecycleRoutes)
            .Where(rawText => !string.IsNullOrWhiteSpace(rawText))
            .Select(rawText => "/" + rawText!.Trim('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(SplitSegments)
            .ToList();

    private static List<ApiLiteral> ScanDesktopApiLiterals()
    {
        var repositoryRoot = FindRepositoryRoot();
        var literals = new List<ApiLiteral>();

        foreach (var sourceRoot in ScannedSourceRoots)
        {
            var directory = Path.Combine(repositoryRoot, sourceRoot.Replace('/', Path.DirectorySeparatorChar));
            Directory.Exists(directory).Should().BeTrue($"scanned source root '{sourceRoot}' must exist");

            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(repositoryRoot, file).Replace('\\', '/');
                if (relativePath.Contains("/bin/", StringComparison.OrdinalIgnoreCase) ||
                    relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (!line.Contains("/api/", StringComparison.Ordinal) ||
                        line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var match in StringLiteralPattern.Matches(line).Cast<System.Text.RegularExpressions.Match>())
                    {
                        var literal = TryNormalize(
                            relativePath,
                            lineNumber,
                            match.Groups["prefix"].Value,
                            match.Groups["body"].Value);
                        if (literal is not null)
                        {
                            literals.Add(literal);
                        }
                    }
                }
            }
        }

        return literals;
    }

    /// <summary>
    /// Reduces a string literal to the Meridian API route it addresses: interpolation holes become
    /// wildcards, anything before <c>/api/</c> (a base-URL hole) and the query string are dropped.
    /// Returns null for literals that are not Meridian routes — no <c>/api/</c>, or an absolute
    /// URL to an external host such as a market-data vendor.
    /// </summary>
    private static ApiLiteral? TryNormalize(string file, int line, string prefix, string body)
    {
        var text = body;
        if (prefix.Contains('$', StringComparison.Ordinal))
        {
            text = InterpolationHolePattern.Replace(body, Hole.ToString());

            // A hole whose expression contains its own string literal, such as
            // {(isRefund ? "refund" : "apply")}, ends the scanned literal at the inner quote and
            // leaves the hole open. Whatever it evaluates to, it is one more hole.
            var openHole = text.LastIndexOf('{');
            if (openHole >= 0)
            {
                text = string.Concat(text.AsSpan(0, openHole), Hole.ToString());
            }
        }

        var apiIndex = text.IndexOf("/api/", StringComparison.Ordinal);
        if (apiIndex < 0)
        {
            return null;
        }

        var schemeIndex = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeIndex >= 0 && schemeIndex < apiIndex)
        {
            return null;
        }

        var route = text[apiIndex..];
        var queryIndex = route.IndexOfAny(new[] { '?', '#' });
        if (queryIndex >= 0)
        {
            route = route[..queryIndex];
        }

        // A literal ending in '/' is a prefix that the caller completes by concatenation
        // (or compares with StartsWith); it only has to be the start of a mapped route.
        var isPrefix = route.EndsWith('/');
        route = route.TrimEnd('/');

        var display = route.Replace(Hole.ToString(), "{*}", StringComparison.Ordinal) + (isPrefix ? "/" : string.Empty);
        return new ApiLiteral(file, line, display, SplitSegments(route), isPrefix);
    }

    private static bool ResolvesToMappedRoute(ApiLiteral literal, IReadOnlyList<string[]> templates)
        => templates.Any(template => Matches(literal, template));

    private static bool Matches(ApiLiteral literal, string[] template)
    {
        var segments = literal.Segments;
        for (var i = 0; i < template.Length; i++)
        {
            var templateSegment = template[i];
            if (templateSegment.StartsWith("{*", StringComparison.Ordinal))
            {
                // Catch-all parameter: swallows whatever is left.
                return true;
            }

            if (i >= segments.Length)
            {
                // The literal ran out first: fine for a prefix literal, or when everything the
                // template still expects is optional.
                return literal.IsPrefix ||
                       template.Skip(i).All(remaining => remaining.EndsWith("?}", StringComparison.Ordinal));
            }

            if (!SegmentMatches(segments[i], templateSegment))
            {
                return false;
            }
        }

        return segments.Length == template.Length;
    }

    private static bool SegmentMatches(string literalSegment, string templateSegment)
    {
        if (templateSegment.Contains('{', StringComparison.Ordinal))
        {
            // Route parameter (possibly constrained or embedded): any concrete value satisfies it.
            return true;
        }

        if (!literalSegment.Contains(Hole, StringComparison.Ordinal))
        {
            return string.Equals(literalSegment, templateSegment, StringComparison.OrdinalIgnoreCase);
        }

        // The literal segment has an interpolation hole; the fixed text around it must still
        // line up with the template's literal segment (a trailing hole is often a query string).
        var pattern = "^" + string.Join(".*", literalSegment.Split(Hole).Select(Regex.Escape)) + "$";
        return Regex.IsMatch(templateSegment, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string[] SplitSegments(string route)
        => route.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Meridian.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (Meridian.sln).");
    }
}
