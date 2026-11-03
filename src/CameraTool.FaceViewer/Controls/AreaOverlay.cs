namespace CameraTool.FaceViewer.Controls;

using System.Windows.Input;
using System.Windows.Media.Imaging;

using BunnyTail.XamlProperty;

using CameraTool.FaceViewer.Services;
using CameraTool.Vision;

// Darkens the cells outside the detection area in the same layout as VideoView, and edits the area while IsEditing.
// A drag sets the cells in the rectangle from the pressed cell to the current cell:
// they are removed when the pressed cell is in the area, otherwise they are added.
// Drawn only when the area, the size or the mode changes (not on each frame).
public sealed partial class AreaOverlay : FrameworkElement
{
    private const double Dpi = 96;

    // Black with alpha 0x60 / 0x99 (Pbgra32)
    private const uint DimColor = 0x60000000;

    private const uint EditingDimColor = 0x99000000;

    private static readonly Pen GridPen = CreatePen(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF), 1);

    private static readonly Pen SelectionPen = CreatePen(Colors.White, 2);

    private readonly WriteableBitmap bitmap = new(DetectionArea.Columns, DetectionArea.Rows, Dpi, Dpi, PixelFormats.Pbgra32, null);

    private AreaService? attachedSource;

    private DetectionArea area = DetectionArea.Full;

    // Area when the drag started (null while not dragging)
    private DetectionArea? dragBase;

    private bool dragIncluded;

    private int dragColumn;

    private int dragRow;

    private int currentColumn;

    private int currentRow;

    [DependencyProperty(PropertyChanged = nameof(OnSourceChanged))]
    public partial AreaService? Source { get; set; }

    [DependencyProperty(PropertyChanged = nameof(OnIsEditingChanged))]
    public partial bool IsEditing { get; set; }

    [DependencyProperty(PropertyChanged = nameof(OnFrameSizeChanged))]
    public partial int FrameWidth { get; set; }

    [DependencyProperty(PropertyChanged = nameof(OnFrameSizeChanged))]
    public partial int FrameHeight { get; set; }

    public AreaOverlay()
    {
        // The mouse is received only while editing
        IsHitTestVisible = false;
        Cursor = Cursors.Cross;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    //--------------------------------------------------------------------------------
    // Visual
    //--------------------------------------------------------------------------------

    protected override void OnRender(DrawingContext drawingContext)
    {
        var video = GetVideoRect();
        if (video.IsEmpty)
        {
            return;
        }

        if (IsEditing)
        {
            // Target of the hit test
            drawingContext.DrawRectangle(Brushes.Transparent, null, video);
        }

        if (IsEditing || !area.IsFull)
        {
            drawingContext.DrawImage(bitmap, video);
        }

        if (IsEditing)
        {
            DrawGrid(drawingContext, video);
            if (dragBase is not null)
            {
                drawingContext.DrawRectangle(null, SelectionPen, ToRect(video, dragColumn, dragRow, currentColumn, currentRow));
            }
        }
    }

    private Rect GetVideoRect() => VideoView.CalculateUniformRect(RenderSize, FrameWidth, FrameHeight);

    private static void DrawGrid(DrawingContext drawingContext, Rect video)
    {
        for (var column = 1; column < DetectionArea.Columns; column++)
        {
            var x = video.X + ((video.Width * column) / DetectionArea.Columns);
            drawingContext.DrawLine(GridPen, new Point(x, video.Top), new Point(x, video.Bottom));
        }

        for (var row = 1; row < DetectionArea.Rows; row++)
        {
            var y = video.Y + ((video.Height * row) / DetectionArea.Rows);
            drawingContext.DrawLine(GridPen, new Point(video.Left, y), new Point(video.Right, y));
        }
    }

    private static Rect ToRect(Rect video, int column0, int row0, int column1, int row1)
    {
        var cellWidth = video.Width / DetectionArea.Columns;
        var cellHeight = video.Height / DetectionArea.Rows;
        return new Rect(
            new Point(video.X + (Math.Min(column0, column1) * cellWidth), video.Y + (Math.Min(row0, row1) * cellHeight)),
            new Point(video.X + ((Math.Max(column0, column1) + 1) * cellWidth), video.Y + ((Math.Max(row0, row1) + 1) * cellHeight)));
    }

    private unsafe void WriteBitmap()
    {
        var color = IsEditing ? EditingDimColor : DimColor;
        bitmap.Lock();
        try
        {
            var pixels = (uint*)bitmap.BackBuffer;
            var stride = bitmap.BackBufferStride / sizeof(uint);
            for (var row = 0; row < DetectionArea.Rows; row++)
            {
                var line = pixels + (row * stride);
                for (var column = 0; column < DetectionArea.Columns; column++)
                {
                    line[column] = area.Contains(column, row) ? 0 : color;
                }
            }

            bitmap.AddDirtyRect(new Int32Rect(0, 0, DetectionArea.Columns, DetectionArea.Rows));
        }
        finally
        {
            bitmap.Unlock();
        }
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
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

    private void OnIsEditingChanged()
    {
        IsHitTestVisible = IsEditing;
        if (!IsEditing)
        {
            EndDrag();
        }

        WriteBitmap();
        InvalidateVisual();
    }

    private void OnFrameSizeChanged() => InvalidateVisual();

    private void OnLoaded(object sender, RoutedEventArgs e) => Attach();

    private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();

    private void Attach()
    {
        if (Source is { } source)
        {
            source.Changed += OnAreaChanged;
            attachedSource = source;
        }

        UpdateArea();
    }

    private void Detach()
    {
        if (attachedSource is not null)
        {
            attachedSource.Changed -= OnAreaChanged;
            attachedSource = null;
        }
    }

    // UI thread
    private void OnAreaChanged(object? sender, EventArgs e) => UpdateArea();

    private void UpdateArea()
    {
        area = attachedSource?.Current ?? DetectionArea.Full;
        WriteBitmap();
        InvalidateVisual();
    }

    //--------------------------------------------------------------------------------
    // Edit
    //--------------------------------------------------------------------------------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEditing || (attachedSource is not { } source) || !TryGetCell(e.GetPosition(this), false, out var column, out var row))
        {
            return;
        }

        dragBase = source.Current;
        dragIncluded = !dragBase.Contains(column, row);
        dragColumn = column;
        dragRow = row;
        currentColumn = column;
        currentRow = row;
        CaptureMouse();
        source.Update(dragBase.WithCells(column, row, column, row, dragIncluded));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if ((dragBase is null) ||
            (attachedSource is not { } source) ||
            !TryGetCell(e.GetPosition(this), true, out var column, out var row) ||
            ((column == currentColumn) && (row == currentRow)))
        {
            return;
        }

        currentColumn = column;
        currentRow = row;
        source.Update(dragBase.WithCells(dragColumn, dragRow, column, row, dragIncluded));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (dragBase is not null)
        {
            EndDrag();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        EndDrag();
    }

    private void EndDrag()
    {
        if (dragBase is null)
        {
            return;
        }

        // Cleared first, because releasing the capture calls this again
        dragBase = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        attachedSource?.Save();
        InvalidateVisual();
    }

    // Cell at the position in the video (clamped to the edge cells when clamp is true)
    private bool TryGetCell(Point point, bool clamp, out int column, out int row)
    {
        column = 0;
        row = 0;
        var video = GetVideoRect();
        if (video.IsEmpty)
        {
            return false;
        }

        var x = (point.X - video.X) / video.Width;
        var y = (point.Y - video.Y) / video.Height;
        if (!clamp && ((x < 0) || (x >= 1) || (y < 0) || (y >= 1)))
        {
            return false;
        }

        column = Math.Clamp((int)Math.Floor(x * DetectionArea.Columns), 0, DetectionArea.Columns - 1);
        row = Math.Clamp((int)Math.Floor(y * DetectionArea.Rows), 0, DetectionArea.Rows - 1);
        return true;
    }
}
