namespace CameraTool.FaceViewer.Controls;

using System.Windows.Media.Imaging;

using BunnyTail.XamlProperty;

using CameraTool.FaceViewer.Helpers;
using CameraTool.FaceViewer.Services;
using CameraTool.Vision;

// Draws the moving cells in the same layout as VideoView.
// The mask is written to a bitmap with one pixel per cell, which is stretched without interpolation.
public sealed partial class MotionOverlay : FrameworkElement
{
    private const double Dpi = 96;

    // Orange with alpha 0x50
    private static readonly uint CellColor = ToPremultiplied(0x50, 0xFF, 0x91, 0x00);

    private readonly WriteableBitmap bitmap = new(MotionDetector.Columns, MotionDetector.Rows, Dpi, Dpi, PixelFormats.Pbgra32, null);

    private readonly byte[] mask = new byte[MotionDetector.MaskLength];

    private readonly byte[] drawnMask = new byte[MotionDetector.MaskLength];

    private readonly UpdateRequest updateRequest;

    private MotionService? attachedSource;

    private MotionInfo info;

    [DependencyProperty(PropertyChanged = nameof(OnSourceChanged))]
    public partial MotionService? Source { get; set; }

    public MotionOverlay()
    {
        updateRequest = new UpdateRequest(UpdateCells);
        IsHitTestVisible = false;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    //--------------------------------------------------------------------------------
    // Visual
    //--------------------------------------------------------------------------------

    protected override void OnRender(DrawingContext drawingContext)
    {
        var area = VideoView.CalculateUniformRect(RenderSize, info.FrameWidth, info.FrameHeight);
        if (!area.IsEmpty)
        {
            drawingContext.DrawImage(bitmap, area);
        }
    }

    private unsafe void WriteBitmap()
    {
        bitmap.Lock();
        try
        {
            var pixels = (uint*)bitmap.BackBuffer;
            var stride = bitmap.BackBufferStride / sizeof(uint);
            for (var row = 0; row < MotionDetector.Rows; row++)
            {
                var line = pixels + (row * stride);
                for (var column = 0; column < MotionDetector.Columns; column++)
                {
                    var index = (row * MotionDetector.Columns) + column;
                    line[column] = (mask[index >> 3] & (1 << (index & 7))) != 0 ? CellColor : 0;
                }
            }

            bitmap.AddDirtyRect(new Int32Rect(0, 0, MotionDetector.Columns, MotionDetector.Rows));
        }
        finally
        {
            bitmap.Unlock();
        }
    }

    // Pbgra32
    private static uint ToPremultiplied(byte alpha, byte red, byte green, byte blue) =>
        ((uint)alpha << 24) | ((uint)(red * alpha / 255) << 16) | ((uint)(green * alpha / 255) << 8) | (uint)(blue * alpha / 255);

    //--------------------------------------------------------------------------------
    // Update
    //--------------------------------------------------------------------------------

    private void OnSourceChanged()
    {
        if (IsLoaded)
        {
            Detach();
            Attach();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Attach();

    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

    private void Attach()
    {
        if (Source is { } source)
        {
            source.Updated += OnMotionUpdated;
            attachedSource = source;
            UpdateCells();
        }
    }

    private void Detach()
    {
        if (attachedSource is not null)
        {
            attachedSource.Updated -= OnMotionUpdated;
            attachedSource = null;
        }
    }

    // Capture thread
    private void OnMotionUpdated(object? sender, EventArgs e) => updateRequest.Request();

    private void UpdateCells()
    {
        var previous = info;
        info = attachedSource?.CopyResults(mask) ?? default;

        // The bitmap is written only when the cells change
        if (!mask.AsSpan().SequenceEqual(drawnMask))
        {
            WriteBitmap();
            mask.CopyTo(drawnMask, 0);
        }

        if ((info.FrameWidth != previous.FrameWidth) || (info.FrameHeight != previous.FrameHeight))
        {
            InvalidateVisual();
        }
    }
}
