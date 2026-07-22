namespace Meridian.Tests.Application.Commands;

internal static class CommandTestConsole
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<T> CaptureErrorAsync<T>(Func<Task<T>> action)
    {
        await Gate.WaitAsync();
        var originalError = Console.Error;
        try
        {
            using var writer = new StringWriter();
            Console.SetError(writer);
            return await action();
        }
        finally
        {
            Console.SetError(originalError);
            Gate.Release();
        }
    }

    public static T CaptureError<T>(Func<T> action)
    {
        Gate.Wait();
        var originalError = Console.Error;
        try
        {
            using var writer = new StringWriter();
            Console.SetError(writer);
            return action();
        }
        finally
        {
            Console.SetError(originalError);
            Gate.Release();
        }
    }
}
