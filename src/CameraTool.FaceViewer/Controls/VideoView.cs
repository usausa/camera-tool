namespace CameraTool.FaceViewer.Controls;

using System.Windows.Media.Imaging;

using BunnyTail.XamlProperty;

using CameraTool.FaceViewer.Helpers;
using CameraTool.FaceViewer.Services;

public sealed partial class VideoView : FrameworkElement
{
    private const double Dpi = 96;

    private readonly UpdateRequest updateRequest;

    private WriteableBitmap? bitmap;

    private FrameStore? attachedSource;

    private bool renderingAttached;

    [DependencyProperty(PropertyChanged = nameof(OnSourceChanged))]
    public partial FrameStore? Source { get; set; }

    [DependencyProperty(PropertyChanged = nameof(OnUpdateModeChanged))]
    public partial VideoUpdateMode UpdateMode { get; set; }

    public VideoView()
    {
        updateRequest = new UpdateRequest(UpdateFrame);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static Rect CalculateUniformRect(Size size, double width, double height)
    {
        if ((width <= 0) || (height <= 0) || (size.Width <= 0) || (size.Height <= 0))
        {
            return Rect.Empty;
        }

        var scale = Math.Min(size.Width / width, size.Height / height);
        var renderWidth = width * scale;
        var renderHeight = height * scale;
        return new Rect((size.Width - renderWidth) / 2, (size.Height - renderHeight) / 2, renderWidth, renderHeight);
    }

    //--------------------------------------------------------------------------------
    // Layout
    //--------------------------------------------------------------------------------

    protected override Size MeasureOverride(Size availableSize)
    {
        if (bitmap is null)
        {
            return default;
        }

        var width = (double)bitmap.PixelWidth;
        var height = (double)bitmap.PixelHeight;
        var scaleX = Double.IsInfinity(availableSize.Width) ? Double.PositiveInfinity : availableSize.Width / width;
        var scaleY = Double.IsInfinity(availableSize.Height) ? Double.PositiveInfinity : availableSize.Height / height;
        var scale = Math.Min(scaleX, scaleY);
        return Double.IsInfinity(scale) ? new Size(width, height) : new Size(width * scale, height * scale);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (bitmap is not null)
        {
            drawingContext.DrawImage(bitmap, CalculateUniformRect(RenderSize, bitmap.PixelWidth, bitmap.PixelHeight));
        }
    }

    //--------------------------------------------------------------------------------
    // Update
    //--------------------------------------------------------------------------------

    private void OnSourceChanged() => Reattach();

    private void OnUpdateModeChanged() => Reattach();

    private void OnLoaded(object sender, RoutedEventArgs e) => Attach();

    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

    private void Reattach()
    {
        if (IsLoaded)
        {
            Detach();
            Attach();
        }
    }

    private void Attach()
    {
        if (UpdateMode == VideoUpdateMode.Rendering)
        {
            CompositionTarget.Rendering += OnRendering;
            renderingAttached = true;
        }
        else if (Source is { } source)
        {
            source.FrameWritten += OnFrameWritten;
            attachedSource = source;
        }
    }

    private void Detach()
    {
        if (renderingAttached)
        {
            CompositionTarget.Rendering -= OnRendering;
            renderingAttached = false;
        }

        if (attachedSource is not null)
        {
            attachedSource.FrameWritten -= OnFrameWritten;
            attachedSource = null;
        }
    }

    // Producer thread
    private void OnFrameWritten(object? sender, EventArgs e) => updateRequest.Request();

    private void OnRendering(object? sender, EventArgs e) => UpdateFrame();

    private void UpdateFrame()
    {
        if ((Source is not { } source) || !source.TryAcquire(out var slot))
        {
            return;
        }

        if ((bitmap is null) || (bitmap.PixelWidth != slot.Width) || (bitmap.PixelHeight != slot.Height))
        {
            bitmap = new WriteableBitmap(slot.Width, slot.Height, Dpi, Dpi, PixelFormats.Bgr32, null);
            InvalidateMeasure();
            InvalidateVisual();
        }

        CopyToBitmap(slot, bitmap);
        source.ReportDisplayed(slot);
    }

    private static unsafe void CopyToBitmap(FrameSlot slot, WriteableBitmap target)
    {
        target.Lock();
        try
        {
            fixed (byte* source = slot.Data)
            {
                var destination = (byte*)target.BackBuffer;
                if (target.BackBufferStride == slot.Stride)
                {
                    var size = (long)slot.Stride * slot.Height;
                    Buffer.MemoryCopy(source, destination, size, size);
                }
                else
                {
                    for (var y = 0; y < slot.Height; y++)
                    {
                        Buffer.MemoryCopy(source + (y * slot.Stride), destination + (y * target.BackBufferStride), target.BackBufferStride, slot.Stride);
                    }
                }
            }

            target.AddDirtyRect(new Int32Rect(0, 0, slot.Width, slot.Height));
        }
        finally
        {
            target.Unlock();
        }
    }
}
