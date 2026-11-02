namespace CameraTool.FaceViewer.Controls;

// Label visuals of a box overlay, one per box.
// Labels are child visuals that are redrawn only when the text changes and moved by Offset.
// The confidence changes on most updates, so each label text is drawn once into a frozen drawing
// and reused, because drawing the text itself allocates.
public sealed class BoxLabels
{
    private const double FontSize = 12;

    private const double Margin = 2;

    private const int NoLabel = -1;

    private static readonly Typeface LabelTypeface = new("Segoe UI");

    private readonly DrawingVisual[] visuals;

    // Key of the drawn label
    private readonly int[] keys;

    private readonly Dictionary<int, Label> labels = [];

    private readonly Func<int, string> createText;

    private readonly Func<int, Brush> getBrush;

    // key: non-negative value that determines the text and the brush of a label
    public BoxLabels(int capacity, Func<int, string> createText, Func<int, Brush> getBrush)
    {
        this.createText = createText;
        this.getBrush = getBrush;
        visuals = new DrawingVisual[capacity];
        keys = new int[capacity];
        for (var i = 0; i < capacity; i++)
        {
            visuals[i] = new DrawingVisual();
            keys[i] = NoLabel;
        }
    }

    public void AddTo(VisualCollection collection)
    {
        foreach (var visual in visuals)
        {
            collection.Add(visual);
        }
    }

    // The drawings depend on the DPI
    public void Reset()
    {
        labels.Clear();
        keys.AsSpan().Fill(NoLabel);
    }

    // Places the label above the box, inside the area
    public void Show(int index, int key, Rect area, Rect box, double pixelsPerDip)
    {
        var label = GetLabel(key, pixelsPerDip);
        var visual = visuals[index];
        if (keys[index] != key)
        {
            using var context = visual.RenderOpen();
            context.DrawDrawing(label.Drawing);
            keys[index] = key;
        }

        visual.Offset = new Vector(box.Left, Math.Max(area.Top, box.Top - label.Height - Margin));
    }

    // Clears the labels from the index
    public void HideFrom(int index)
    {
        for (var i = index; i < visuals.Length; i++)
        {
            if (keys[i] != NoLabel)
            {
                using (visuals[i].RenderOpen())
                {
                    // Clear
                }

                keys[i] = NoLabel;
            }
        }
    }

    private Label GetLabel(int key, double pixelsPerDip)
    {
        if (!labels.TryGetValue(key, out var label))
        {
            var text = new FormattedText(
                createText(key),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                FontSize,
                getBrush(key),
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

    // Height: line height of the text
    private readonly record struct Label(Drawing Drawing, double Height);
}
