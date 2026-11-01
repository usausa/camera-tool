namespace CameraTool.FaceViewer.Controls;

public enum VideoUpdateMode
{
    // Update as soon as a frame is written (Dispatcher, Render priority)
    Immediate,
    // Update on the WPF rendering tick (CompositionTarget.Rendering)
    Rendering
}
