using Meridian.Wpf.Services;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Archive health used to probe <c>/api/archive/health</c>, a route the host never mapped, before
/// falling back to the local calculation, and it dropped the caller's cancellation token on the way.
/// Health is now calculated locally only, and cancellation reaches the calculation.
/// </summary>
public sealed class ArchiveHealthServiceTests
{
    [Fact]
    public async Task GetHealthStatusAsync_WhenRefreshIsCancelled_ThrowsInsteadOfCalculating()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => ArchiveHealthService.Instance.GetHealthStatusAsync(forceRefresh: true, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ServiceSource_DoesNotProbeAServerArchiveHealthRoute()
    {
        var source = File.ReadAllText(GetRepositoryFilePath("src/Meridian.Wpf/Services/ArchiveHealthService.cs"));

        source.Should().NotContain("/api/archive/health", "the host maps no archive health route");
        source.Should().NotContain("HttpClient");
    }

    private static string GetRepositoryFilePath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
