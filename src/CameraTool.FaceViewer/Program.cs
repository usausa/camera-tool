namespace CameraTool.FaceViewer;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        var app = new App();
        return app.Run();
    }
}
