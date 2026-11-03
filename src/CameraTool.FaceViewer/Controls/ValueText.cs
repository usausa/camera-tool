namespace CameraTool.FaceViewer.Controls;

using System.Windows.Automation.Peers;
using System.Windows.Documents;

using BunnyTail.XamlProperty;

// One line of text drawn as one glyph run, for the statistics values that change every second.
// TextBlock lays out the whole line again on each change (about 10 KB), this takes the glyphs of the printable
// ASCII characters from a table made once per font, so a change allocates little more than the glyph run (about 1 KB).
// Same size and baseline as TextBlock; no font fallback, shaping or wrapping.
public sealed partial class ValueText : FrameworkElement
{
    private const char FirstTableChar = ' ';

    private const char LastTableChar = '~';

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(ValueText));

    public static readonly DependencyProperty FontStyleProperty = TextElement.FontStyleProperty.AddOwner(typeof(ValueText));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(ValueText));

    public static readonly DependencyProperty FontStretchProperty = TextElement.FontStretchProperty.AddOwner(typeof(ValueText));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(ValueText));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(ValueText));

    // Glyph index and advance width (em) of each table character
    private readonly ushort[] tableGlyphs = new ushort[LastTableChar - FirstTableChar + 1];

    private readonly double[] tableAdvances = new double[LastTableChar - FirstTableChar + 1];

    private bool fontResolved;

    // Null when the font has no single glyph typeface (e.g. it is not installed), then FormattedText draws the text
    private GlyphTypeface? glyphTypeface;

    private double lineHeight;

    private double baseline;

    // Snaps the baseline to the device pixels like TextBlock
    private GuidelineSet? baselineGuideline;

    [DependencyProperty(Options = FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender)]
    public partial string? Text { get; set; }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStretch FontStretch
    {
        get => (FontStretch)GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    //--------------------------------------------------------------------------------
    // Layout
    //--------------------------------------------------------------------------------

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if ((e.Property == FontFamilyProperty) || (e.Property == FontStyleProperty) || (e.Property == FontWeightProperty) ||
            (e.Property == FontStretchProperty) || (e.Property == FontSizeProperty))
        {
            fontResolved = false;
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text ?? String.Empty;
        if (ResolveFont() is not { } face)
        {
            var formatted = CreateFormattedText(text);
            return new Size(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
        }

        var width = 0d;
        foreach (var c in text)
        {
            width += GetGlyph(face, c).Advance;
        }

        return new Size(width * FontSize, lineHeight);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var text = Text;
        if (String.IsNullOrEmpty(text))
        {
            return;
        }

        if (ResolveFont() is not { } face)
        {
            drawingContext.DrawText(CreateFormattedText(text), default);
            return;
        }

        var fontSize = FontSize;
        var glyphs = new ushort[text.Length];
        var advances = new double[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var (glyph, advance) = GetGlyph(face, text[i]);
            glyphs[i] = glyph;
            advances[i] = advance * fontSize;
        }

        var pixelsPerDip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var run = new GlyphRun(face, 0, false, fontSize, pixelsPerDip, glyphs, new Point(0, baseline), advances, null, null, null, null, null, null);
        drawingContext.PushGuidelineSet(baselineGuideline);
        drawingContext.DrawGlyphRun(Foreground, run);
        drawingContext.Pop();
    }

    //--------------------------------------------------------------------------------
    // Font
    //--------------------------------------------------------------------------------

    private GlyphTypeface? ResolveFont()
    {
        if (!fontResolved)
        {
            var typeface = CreateTypeface();
            glyphTypeface = typeface.TryGetGlyphTypeface(out var face) ? face : null;
            if (face is not null)
            {
                // The lookups of GlyphTypeface allocate on each access
                var map = face.CharacterToGlyphMap;
                var widths = face.AdvanceWidths;
                for (var i = 0; i < tableGlyphs.Length; i++)
                {
                    map.TryGetValue(FirstTableChar + i, out var glyph);
                    tableGlyphs[i] = glyph;
                    tableAdvances[i] = widths[glyph];
                }

                // Line height and baseline of TextBlock
                var sample = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                lineHeight = sample.Height;
                baseline = sample.Baseline;
                baselineGuideline = new GuidelineSet(null, [baseline]);
                baselineGuideline.Freeze();
            }

            fontResolved = true;
        }

        return glyphTypeface;
    }

    private (ushort Glyph, double Advance) GetGlyph(GlyphTypeface face, char c)
    {
        if ((c >= FirstTableChar) && (c <= LastTableChar))
        {
            return (tableGlyphs[c - FirstTableChar], tableAdvances[c - FirstTableChar]);
        }

        face.CharacterToGlyphMap.TryGetValue(c, out var glyph);
        return (glyph, face.AdvanceWidths[glyph]);
    }

    private Typeface CreateTypeface() => new(FontFamily, FontStyle, FontWeight, FontStretch);

    private FormattedText CreateFormattedText(string text) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, CreateTypeface(), FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    //--------------------------------------------------------------------------------
    // Automation
    //--------------------------------------------------------------------------------

    protected override AutomationPeer OnCreateAutomationPeer() => new ValueTextAutomationPeer(this);

    // A text element named by the text, like TextBlock
    private sealed class ValueTextAutomationPeer : FrameworkElementAutomationPeer
    {
        public ValueTextAutomationPeer(ValueText owner)
            : base(owner)
        {
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(ValueText);

        protected override string GetNameCore()
        {
            var name = base.GetNameCore();
            return String.IsNullOrEmpty(name) ? ((ValueText)Owner).Text ?? String.Empty : name;
        }
    }
}
