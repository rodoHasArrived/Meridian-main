namespace Meridian.Execution.Sdk;

/// <summary>Marks exceptions known to precede order dispatch while preserving their original type and cause.</summary>
public static class OrderSubmissionRefusal
{
    private static readonly object BeforeDispatchMarker = new();

    public static void MarkBeforeDispatch(Exception exception)
        => exception.Data[BeforeDispatchMarker] = true;

    public static bool IsBeforeDispatch(Exception exception)
        => exception.Data[BeforeDispatchMarker] is true;
}
