using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Fita.Controls;

/// <summary>
/// O nome "FITA" desenhado à mão, à maneira do logótipo da FIFA: letras muito pesadas, os braços
/// do F e a barra do T cortados na mesma oblíqua das pernas do A, e o A em bico. A largura sai
/// da altura; a cor é o Foreground.
/// </summary>
public sealed class Wordmark : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Wordmark>();

    static Wordmark()
    {
        AffectsRender<Wordmark>(ForegroundProperty);
    }

    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    // Desenho numa grelha de 937 × 315 (a altura das letras).
    private const double GridWidth = 937;
    private const double GridHeight = 315;

    private static readonly Point[][] Outlines =
    [
        // F
        [new(0, 0), new(242, 0), new(218, 68), new(100, 68), new(100, 132), new(194, 132),
         new(170, 201), new(100, 201), new(100, 315), new(0, 315)],
        // I
        [new(272, 0), new(372, 0), new(372, 315), new(272, 315)],
        // T: a barra é um paralelogramo, com as pontas na oblíqua do F
        [new(426, 0), new(652, 0), new(628, 68), new(577, 68), new(577, 315), new(477, 315),
         new(477, 68), new(402, 68)],
        // A, por fora
        [new(722, 0), new(822, 0), new(937, 315), new(839, 315), new(827, 273), new(714, 273),
         new(702, 315), new(607, 315)],
        // A, o furo
        [new(772, 83), new(809, 211), new(734, 211)],
    ];

    /// <summary>Os contornos, na grelha de 937 × 315 (o relatório desenha-os em SVG).</summary>
    internal static IReadOnlyList<Point[]> Shapes => Outlines;

    protected override Size MeasureOverride(Size availableSize)
    {
        var h = double.IsNaN(Height) ? Math.Min(availableSize.Height, 13) : Height;
        if (double.IsInfinity(h))
            h = 13;
        return new Size(h * GridWidth / GridHeight, h);
    }

    public override void Render(DrawingContext context)
    {
        var h = Bounds.Height;
        if (h <= 0)
            return;
        var s = Math.Min(h / GridHeight, Bounds.Width / GridWidth);
        var oy = (h - GridHeight * s) / 2;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.SetFillRule(FillRule.EvenOdd);
            foreach (var outline in Outlines)
            {
                c.BeginFigure(new Point(outline[0].X * s, oy + outline[0].Y * s), true);
                for (var i = 1; i < outline.Length; i++)
                    c.LineTo(new Point(outline[i].X * s, oy + outline[i].Y * s));
                c.EndFigure(true);
            }
        }
        context.DrawGeometry(Foreground ?? Brushes.White, null, g);
    }
}
