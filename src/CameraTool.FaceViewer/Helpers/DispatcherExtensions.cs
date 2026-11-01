namespace CameraTool.FaceViewer.Helpers;

using System.Windows.Threading;

public static class DispatcherExtensions
{
    // Posts without capturing ExecutionContext to avoid allocation on each call
    public static void PostWithoutContext(this Dispatcher dispatcher, Action action, DispatcherPriority priority)
    {
        if (ExecutionContext.IsFlowSuppressed())
        {
            dispatcher.InvokeAsync(action, priority);
            return;
        }

        using var flow = ExecutionContext.SuppressFlow();
        dispatcher.InvokeAsync(action, priority);
    }
}
