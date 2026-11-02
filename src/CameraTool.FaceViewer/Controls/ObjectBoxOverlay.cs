namespace CameraTool.FaceViewer.Controls;

using System.Windows.Threading;

using BunnyTail.XamlProperty;

using CameraTool.FaceViewer.Components;
using CameraTool.FaceViewer.Helpers;
using CameraTool.Vision;

// Draws object boxes in the same layout as VideoView, in the color of the class group.
// Labels are child visuals that are redrawn only when the text changes and moved by Offset.
// The confidence of an object changes on most updates, so each label text is drawn once into a frozen drawing
// and reused, because drawing the text itself allocates.
public sealed partial class ObjectBoxOverlay : FrameworkElement
{
    private const double LineThickness = 2;

    private const double LabelFontSize = 12;

    private const double LabelMargin = 2;

    private const int NoLabel = -1;

    // Indexed by ObjectGroup (Other, Person, Animal, Vehicle)
    private static readonly SolidColorBrush[] GroupBrushes =
    [
        CreateBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
        CreateBrush(Color.FromRgb(0x29, 0x79, 0xFF)),
        CreateBrush(Color.FromRgb(0xFF, 0xC4, 0x00)),
        CreateBrush(Color.FromRgb(0xE0, 0x40, 0xFB))
    ];

    private static readonly Pen[] GroupPens = GroupBrushes.Select(static x => CreatePen(x, LineThickness)).ToArray();

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private readonly ObjectBox[] boxes = new ObjectBox[ObjectDetectionService.MaxObjects];

    private readonly VisualCollection visuals;

    private readonly DrawingVisual boxVisual = new();

    private readonly DrawingVisual[] labelVisuals = new DrawingVisual[ObjectDetectionService.MaxObjects];

    // Class id and percent of the drawn label
    private readonly int[] labelKeys = new int[ObjectDetectionService.MaxObjects];

    private readonly Dictionary<int, Label> labels = [];

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
        for (var i = 0; i < labelVisuals.Length; i++)
        {
            labelVisuals[i] = new DrawingVisual();
            labelKeys[i] = NoLabel;
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
        labels.Clear();
        labelKeys.AsSpan().Fill(NoLabel);
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
        for (var i = 0; i < labelVisuals.Length; i++)
        {
            var visual = labelVisuals[i];
            if (i < count)
            {
                var key = (boxes[i].ClassId * 101) + (int)(boxes[i].Confidence * 100);
                var label = GetLabel(key, pixelsPerDip);
                if (labelKeys[i] != key)
                {
                    using var context = visual.RenderOpen();
                    context.DrawDrawing(label.Drawing);
                    labelKeys[i] = key;
                }

                var rect = ToRect(area, boxes[i]);
                visual.Offset = new Vector(rect.Left, Math.Max(area.Top, rect.Top - label.Height - LabelMargin));
            }
            else if (labelKeys[i] != NoLabel)
            {
                using (visual.RenderOpen())
                {
                    // Clear
                }

                labelKeys[i] = NoLabel;
            }
        }
    }

    private static Rect ToRect(Rect area, ObjectBox box) =>
        new(
            new Point(area.X + (box.Left * area.Width), area.Y + (box.Top * area.Height)),
            new Point(area.X + (box.Right * area.Width), area.Y + (box.Bottom * area.Height)));

    // key: class id * 101 + percent
    private Label GetLabel(int key, double pixelsPerDip)
    {
        if (!labels.TryGetValue(key, out var label))
        {
            var classId = key / 101;
            var text = new FormattedText(
                String.Create(CultureInfo.InvariantCulture, $"{ObjectLabels.Names[classId]} {key % 101}%"),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                LabelFontSize,
                GroupBrushes[(int)ObjectLabels.GetGroup(classId)],
                pixelsPerDip);
            var drawing = new DrawingGroup();
            using (var context = drawing.Open())
            {
                context.DrawText(text, default);
            }

            drawing.Freeze();
            label = new Label(drawing, text.Height);
            labels[key] = label;
        }

        return label;
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

    // Height: line height of the text
    private readonly record struct Label(Drawing Drawing, double Height);
}
