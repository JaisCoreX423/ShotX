using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;

namespace ShotX.Models;

public enum ToolType
{
    Select,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Pen,
    Highlighter,
    Text,
    StepNumber,
    Blur,
    Crop,
    Image
}

public enum TextHAlign { Left, Center, Right }
public enum TextVAlign { Top, Center, Bottom }

public class Annotation
{
    public ToolType Type { get; set; }
    public Rect Bounds { get; set; }
    public List<Point> Points { get; set; } = new();
    public Color Color { get; set; } = Colors.Red;
    public double Thickness { get; set; } = 3;
    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; } = 20;
    public int StepIndex { get; set; }
    public bool Locked { get; set; }

    /// <summary>Where <see cref="Text"/> sits within <see cref="Bounds"/> — for a standalone Text
    /// layer, and for the optional label inside a Rectangle/Ellipse.</summary>
    public TextHAlign TextHAlign { get; set; } = TextHAlign.Center;
    public TextVAlign TextVAlign { get; set; } = TextVAlign.Center;

    /// <summary>Fill behind the text (Text annotations) or fill of the shape (Rectangle/Ellipse).
    /// Alpha 0 (the default) means none — fully transparent / outline only.</summary>
    public Color BackgroundColor { get; set; } = Colors.Transparent;

    /// <summary>Pre-rendered pixelated tile, only used by Blur annotations. Rebuilt when Bounds changes.</summary>
    public WriteableBitmap? BlurTile { get; set; }

    /// <summary>Imported picture, only used by Image annotations.</summary>
    public BitmapSource? Image { get; set; }

    public Annotation Clone()
    {
        return new Annotation
        {
            Type = Type,
            Bounds = Bounds,
            Points = new List<Point>(Points),
            Color = Color,
            Thickness = Thickness,
            Text = Text,
            FontSize = FontSize,
            StepIndex = StepIndex,
            Locked = Locked,
            TextHAlign = TextHAlign,
            TextVAlign = TextVAlign,
            BackgroundColor = BackgroundColor,
            BlurTile = BlurTile,
            Image = Image
        };
    }
}
