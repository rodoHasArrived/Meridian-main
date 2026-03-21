using System;
using System.IO;

namespace Meridian.Tests.Infrastructure;

/// <summary>
/// Base class for tests that require temporary directory creation and cleanup.
/// Implements IDisposable to ensure proper cleanup of temporary resources.
/// </summary>
/// <remarks>
/// <para>
/// Use this class as a base for any test that needs to create temporary files or directories.
/// The directories are created in the system temp folder with unique GUIDs to prevent collisions.
/// </para>
/// <para>
/// Example usage:
/// <code>
/// public class MyStorageTests : TempDirectoryTestBase
/// {
///     [Fact]
///     public void Test_WriteToTempDirectory()
///     {
///         var filePath = Path.Combine(TestDataRoot, "test.json");
///         File.WriteAllText(filePath, "{}");
///         Assert.True(File.Exists(filePath));
///     }
/// }
/// </code>
/// </para>
/// </remarks>
public abstract class TempDirectoryTestBase : IDisposable
{
    /// <summary>
    /// Root directory for test data input.
    /// </summary>
    protected string TestDataRoot { get; }

    /// <summary>
    /// Directory for test output files.
    /// </summary>
    protected string TestOutputDir { get; }

    /// <summary>
    /// Unique identifier for this test run.
    /// </summary>
    protected string TestRunId { get; }

    /// <summary>
    /// Initializes a new instance of the test base, creating temporary directories.
    /// </summary>
    protected TempDirectoryTestBase()
    {
        TestRunId = Guid.NewGuid().ToString("N")[..8];
        TestDataRoot = Path.Combine(Path.GetTempPath(), $"mdc_test_data_{TestRunId}");
        TestOutputDir = Path.Combine(Path.GetTempPath(), $"mdc_test_output_{TestRunId}");

        Directory.CreateDirectory(TestDataRoot);
        Directory.CreateDirectory(TestOutputDir);
    }

    /// <summary>
    /// Creates a subdirectory within the test data root.
    /// </summary>
    /// <param name="relativePath">Relative path for the subdirectory.</param>
    /// <returns>The full path to the created directory.</returns>
    protected string CreateTestSubdirectory(string relativePath)
    {
        var fullPath = Path.Combine(TestDataRoot, relativePath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    /// <summary>
    /// Creates a test file with the specified content.
    /// </summary>
    /// <param name="relativePath">Relative path within TestDataRoot.</param>
    /// <param name="content">Content to write to the file.</param>
    /// <returns>The full path to the created file.</returns>
    protected string CreateTestFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(TestDataRoot, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    /// <summary>
    /// Disposes of temporary directories and their contents.
    /// </summary>
    public virtual void Dispose()
    {
        CleanupDirectory(TestDataRoot);
        CleanupDirectory(TestOutputDir);
        GC.SuppressFinalize(this);
    }

    private static void CleanupDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Directory may be in use or locked - log but don't fail the test
            Console.WriteLine($"[WARN] Test cleanup: Could not delete directory {path}");
        }
        catch (UnauthorizedAccessException)
        {
            // Permission issue - log but don't fail the test
            Console.WriteLine($"[WARN] Test cleanup: Access denied for directory {path}");
        }
    }
}

/// <summary>
/// Async version of the temp directory fixture implementing IAsyncLifetime.
/// Use this when your tests require async setup/teardown.
/// </summary>
public abstract class TempDirectoryAsyncTestBase : TempDirectoryTestBase, Xunit.IAsyncLifetime
{
    /// <summary>
    /// Called when the test class is initialized.
    /// Override to add async initialization logic.
    /// </summary>
    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Called when the test class is being disposed.
    /// Override to add async cleanup logic.
    /// </summary>
    public virtual Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }
}
