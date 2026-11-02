namespace CameraTool.FaceViewer;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        // Otherwise each dispatcher operation and window message allocates a new synchronization context
        // (set before the dispatcher is used)
        BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance = true;

        var app = new App();
        return app.Run();
    }
}
