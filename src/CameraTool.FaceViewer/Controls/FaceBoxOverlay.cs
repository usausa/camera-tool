namespace CameraTool.FaceViewer.Controls;

using BunnyTail.XamlProperty;

using CameraTool.FaceViewer.Components;
using CameraTool.FaceViewer.Helpers;
using CameraTool.Vision;

// Draws face boxes in the same layout as VideoView, with the confidence labels of BoxLabels.
public sealed partial class FaceBoxOverlay : FrameworkElement
{
    private const double LineThickness = 2;

    private static readonly SolidColorBrush BoxBrush = CreateBrush(Color.FromRgb(0x00, 0xE6, 0x76));

    private static readonly Pen BoxPen = CreatePen(BoxBrush, LineThickness);

    private readonly FaceBox[] boxes = new FaceBox[FaceDetectionService.MaxFaces];

    private readonly VisualCollection visuals;

    private readonly DrawingVisual boxVisual = new();

    // key: percent
    private readonly BoxLabels labels = new(
        FaceDetectionService.MaxFaces,
        static key => String.Create(CultureInfo.InvariantCulture, $"{key}%"),
        static _ => BoxBrush);

    private readonly UpdateRequest updateRequest;

    private FaceDetectionService? attachedSource;

    private DetectionInfo info;

    [DependencyProperty(PropertyChanged = nameof(OnSourceChanged))]
    public partial FaceDetectionService? Source { get; set; }

    public FaceBoxOverlay()
    {
        updateRequest = new UpdateRequest(UpdateBoxes);

        // Create children before setting properties that enumerate them
        visuals = new VisualCollection(this) { boxVisual };
        labels.AddTo(visuals);

        IsHitTestVisible = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    //--------------------------------------------------------------------------------
    // Visual
    //--------------------------------------------------------------------------------

    protected override int VisualChildrenCount => visuals.Count;

    protected override Visual GetVisualChild(int index) => visuals[index];

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Redraw();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        labels.Reset();
        base.OnDpiChanged(oldDpi, newDpi);
        Redraw();
    }

    private void Redraw()
    {
        var area = VideoView.CalculateUniformRect(RenderSize, info.FrameWidth, info.FrameHeight);
        var count = area.IsEmpty ? 0 : info.Count;

        using (var context = boxVisual.RenderOpen())
        {
            for (var i = 0; i < count; i++)
            {
                context.DrawRectangle(null, BoxPen, ToRect(area, boxes[i]));
            }
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var i = 0; i < count; i++)
        {
            labels.Show(i, (int)(boxes[i].Confidence * 100), area, ToRect(area, boxes[i]), pixelsPerDip);
        }

        labels.HideFrom(count);
    }

    private static Rect ToRect(Rect area, FaceBox box) =>
        new(
            new Point(area.X + (box.Left * area.Width), area.Y + (box.Top * area.Height)),
            new Point(area.X + (box.Right * area.Width), area.Y + (box.Bottom * area.Height)));

    private static SolidColorBrush CreateBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

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
            source.Detected += OnDetected;
            attachedSource = source;
            UpdateBoxes();
        }
    }

    private void Detach()
    {
        if (attachedSource is not null)
        {
            attachedSource.Detected -= OnDetected;
            attachedSource = null;
        }
    }

    // Detection thread
    private void OnDetected(object? sender, EventArgs e) => updateRequest.Request();

    private void UpdateBoxes()
    {
        var previousCount = info.Count;
        info = attachedSource?.CopyResults(boxes) ?? default;
        if ((info.Count > 0) || (previousCount > 0))
        {
            Redraw();
        }
    }
}
