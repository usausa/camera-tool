namespace CameraTool.FaceViewer.Helpers;

using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;

// Runs an action on the UI thread when it is requested from another thread.
// The requests until the action runs are merged into one. A request is a window message to a message-only window,
// so unlike Dispatcher.InvokeAsync it allocates nothing, which matters for the updates on each frame.
public sealed class UpdateRequest
{
    private readonly MessageWindow window;

    private readonly int id;

    private readonly Action action;

    private int pending;

    // Created on the UI thread
    public UpdateRequest(Action action)
    {
        this.action = action;
        window = MessageWindow.Current;
        id = window.Register(this);
    }

    // Any thread
    public void Request()
    {
        if (Interlocked.Exchange(ref pending, 1) == 0)
        {
            window.Post(id);
        }
    }

    // UI thread, a request while the action runs posts again
    internal void Run()
    {
        Volatile.Write(ref pending, 0);
        action();
    }
}

// Message-only window of the UI thread for the update requests (the dispatcher pumps its messages)
internal sealed partial class MessageWindow : IDisposable
{
    // WM_APP + 1, wParam: the id of the request
    private const int RunMessage = 0x8001;

    // HWND_MESSAGE
    private static readonly IntPtr MessageOnlyParent = new(-3);

    private readonly HwndSource source;

    private readonly IntPtr handle;

    private readonly HwndSourceHook hook;

    // UI thread only
    private readonly List<UpdateRequest> requests = [];

    // Created by the first request of the UI thread
    [field: ThreadStatic]
    public static MessageWindow Current => field ??= new MessageWindow();

    private MessageWindow()
    {
        source = new HwndSource(new HwndSourceParameters("CameraTool.FaceViewer.Updates") { ParentWindow = MessageOnlyParent, WindowStyle = 0 });
        handle = source.Handle;
        hook = OnMessage;
        source.AddHook(hook);
        Dispatcher.CurrentDispatcher.ShutdownStarted += OnShutdownStarted;
    }

    public void Dispose()
    {
        source.RemoveHook(hook);
        source.Dispose();
    }

    public int Register(UpdateRequest request)
    {
        requests.Add(request);
        return requests.Count - 1;
    }

    // Fails only after the window is destroyed at the shutdown
    public void Post(int id) => PostMessage(handle, RunMessage, id, IntPtr.Zero);

    private IntPtr OnMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == RunMessage)
        {
            requests[wParam.ToInt32()].Run();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void OnShutdownStarted(object? sender, EventArgs e) => Dispose();

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hWnd, int msg, nint wParam, IntPtr lParam);
}
