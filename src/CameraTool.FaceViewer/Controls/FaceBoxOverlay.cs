namespace CameraTool.FaceViewer.Controls;

using System.Windows.Threading;

using BunnyTail.XamlProperty;

using CameraTool.FaceViewer.Components;
using CameraTool.FaceViewer.Helpers;
using CameraTool.Vision;

// Draws face boxes in the same layout as VideoView.
// Labels are child visuals that are redrawn only when the text changes and moved by Offset,
// because formatting text on each update allocates.
public sealed partial class FaceBoxOverlay : FrameworkElement
{
    private const double LineThickness = 2;

    private const double LabelFontSize = 12;

    private const double LabelMargin = 2;

    private const int NoLabel = -1;

    private static readonly SolidColorBrush BoxBrush = CreateBrush(Color.FromRgb(0x00, 0xE6, 0x76));

    private static readonly Pen BoxPen = CreatePen(BoxBrush, LineThickness);

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private readonly FaceBox[] boxes = new FaceBox[FaceDetectionService.MaxFaces];

    private readonly VisualCollection visuals;

    private readonly DrawingVisual boxVisual = new();

    private readonly DrawingVisual[] labelVisuals = new DrawingVisual[FaceDetectionService.MaxFaces];

    private readonly int[] labelPercents = new int[FaceDetectionService.MaxFaces];

    private readonly Dictionary<int, FormattedText> labelTexts = [];

    private readonly Action updateRequestedAction;

    private FaceDetectionService? attachedSource;

    private FaceDetectionInfo info;

    private int updatePending;

    [DependencyProperty(PropertyChanged = nameof(OnSourceChanged))]
    public partial FaceDetectionService? Source { get; set; }

    public FaceBoxOverlay()
    {
        updateRequestedAction = OnUpdateRequested;

        // Create children before setting properties that enumerate them
        visuals = new VisualCollection(this) { boxVisual };
        for (var i = 0; i < labelVisuals.Length; i++)
        {
            labelVisuals[i] = new DrawingVisual();
            labelPercents[i] = NoLabel;
            visuals.Add(labelVisuals[i]);
        }

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
        labelTexts.Clear();
        labelPercents.AsSpan().Fill(NoLabel);
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
        for (var i = 0; i < labelVisuals.Length; i++)
        {
            var visual = labelVisuals[i];
            if (i < count)
            {
                var percent = (int)(boxes[i].Confidence * 100);
                var text = GetLabelText(percent, pixelsPerDip);
                if (labelPercents[i] != percent)
                {
                    using var context = visual.RenderOpen();
                    context.DrawText(text, default);
                    labelPercents[i] = percent;
                }

                var rect = ToRect(area, boxes[i]);
                visual.Offset = new Vector(rect.Left, Math.Max(area.Top, rect.Top - text.Height - LabelMargin));
            }
            else if (labelPercents[i] != NoLabel)
            {
                using (visual.RenderOpen())
                {
                    // Clear
                }

                labelPercents[i] = NoLabel;
            }
        }
    }

    private static Rect ToRect(Rect area, FaceBox box) =>
        new(
            new Point(area.X + (box.Left * area.Width), area.Y + (box.Top * area.Height)),
            new Point(area.X + (box.Right * area.Width), area.Y + (box.Bottom * area.Height)));

    private FormattedText GetLabelText(int percent, double pixelsPerDip)
    {
        if (!labelTexts.TryGetValue(percent, out var text))
        {
            text = new FormattedText(
                String.Create(CultureInfo.InvariantCulture, $"{percent}%"),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                LabelFontSize,
                BoxBrush,
                pixelsPerDip);
            labelTexts[percent] = text;
        }

        return text;
    }

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
