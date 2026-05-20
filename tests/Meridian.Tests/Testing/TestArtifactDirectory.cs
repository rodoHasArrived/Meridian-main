namespace Meridian.Testing;

internal sealed class TestArtifactDirectory : IDisposable
{
    private TestArtifactDirectory(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public static TestArtifactDirectory Create(string scenarioName)
    {
        var sanitizedName = new string(scenarioName
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        var rootPath = Path.Combine(
            AppContext.BaseDirectory,
            "test-artifacts",
            sanitizedName,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);
        return new TestArtifactDirectory(rootPath);
    }

    public void Dispose()
    {
        if (!Directory.Exists(RootPath))
        {
            return;
        }

        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for test artifacts.
        }
    }
}
