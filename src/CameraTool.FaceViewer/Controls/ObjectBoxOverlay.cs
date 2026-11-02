namespace CameraTool.FaceViewer.Controls;

using System.Windows.Threading;

using BunnyTail.XamlProperty;

using CameraTool.FaceViewer.Components;
using CameraTool.FaceViewer.Helpers;
using CameraTool.Vision;

// Draws object boxes in the same layout as VideoView, in the color of the class group,
// with the class and confidence labels of BoxLabels.
public sealed partial class ObjectBoxOverlay : FrameworkElement
{
    private const double LineThickness = 2;

    // Label key: class id * PercentCount + percent
    private const int PercentCount = 101;

    // Indexed by ObjectGroup (Other, Person, Animal, Vehicle)
    private static readonly SolidColorBrush[] GroupBrushes =
    [
        CreateBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
        CreateBrush(Color.FromRgb(0x29, 0x79, 0xFF)),
        CreateBrush(Color.FromRgb(0xFF, 0xC4, 0x00)),
        CreateBrush(Color.FromRgb(0xE0, 0x40, 0xFB))
    ];

    private static readonly Pen[] GroupPens = GroupBrushes.Select(static x => CreatePen(x, LineThickness)).ToArray();

    private readonly ObjectBox[] boxes = new ObjectBox[ObjectDetectionService.MaxObjects];

    private readonly VisualCollection visuals;

    private readonly DrawingVisual boxVisual = new();

    private readonly BoxLabels labels = new(
        ObjectDetectionService.MaxObjects,
        static key => String.Create(CultureInfo.InvariantCulture, $"{ObjectLabels.Names[key / PercentCount]} {key % PercentCount}%"),
        static key => GroupBrushes[(int)ObjectLabels.GetGroup(key / PercentCount)]);

    private readonly Action updateRequestedAction;

    private ObjectDetectionService? attachedSource;

    private ObjectDetectionInfo info;

    private int updatePending;

    [DependencyProperty(PropertyChanged = nameof(OnSourceChanged))]
    public partial ObjectDetectionService? Source { get; set; }

    public ObjectBoxOverlay()
    {
        updateRequestedAction = OnUpdateRequested;

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
                context.DrawRectangle(null, GroupPens[(int)ObjectLabels.GetGroup(boxes[i].ClassId)], ToRect(area, boxes[i]));
            }
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var i = 0; i < count; i++)
        {
            var key = (boxes[i].ClassId * PercentCount) + (int)(boxes[i].Confidence * 100);
            labels.Show(i, key, area, ToRect(area, boxes[i]), pixelsPerDip);
        }

        labels.HideFrom(count);
    }

    private static Rect ToRect(Rect area, ObjectBox box) =>
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
    private void OnDetected(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref updatePending, 1) == 0)
        {
            Dispatcher.PostWithoutContext(updateRequestedAction, DispatcherPriority.Render);
        }
    }

    private void OnUpdateRequested()
    {
        Volatile.Write(ref updatePending, 0);
        UpdateBoxes();
    }

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
