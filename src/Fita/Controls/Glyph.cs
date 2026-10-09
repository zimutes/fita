using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Fita.Controls;

public enum GlyphKind { Play, Pause, Stop, Prev, Next, Eject, Menu, Compact, Minimize, Close, Logo, Grip }

/// <summary>Ícones desenhados (os dos botões de transporte e da barra de título), na cor do texto.</summary>
public sealed class Glyph : Control
{
    public static readonly StyledProperty<GlyphKind> KindProperty =
        AvaloniaProperty.Register<Glyph, GlyphKind>(nameof(Kind));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<Glyph>();

    static Glyph()
    {
        AffectsRender<Glyph>(KindProperty, ForegroundProperty);
    }

    public GlyphKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public override void Render(DrawingContext context)
    {
        var brush = Foreground ?? Brushes.White;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0)
            return;
        // Desenha-se numa grelha de 16×16, centrada.
        var s = size / 16.0;
        var ox = (Bounds.Width - size) / 2;
        var oy = (Bounds.Height - size) / 2;
        Point P(double x, double y) => new(ox + x * s, oy + y * s);
        Rect R(double x, double y, double w, double h) => new(ox + x * s, oy + y * s, w * s, h * s);
        var pen = new Pen(brush, Math.Max(1, 1.5 * s), lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

        void Poly(params Point[] points)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(points[0], true);
                for (var i = 1; i < points.Length; i++)
                    c.LineTo(points[i]);
                c.EndFigure(true);
            }
            context.DrawGeometry(brush, null, g);
        }

        switch (Kind)
        {
            case GlyphKind.Play:
                Poly(P(4, 2), P(14, 8), P(4, 14));
                break;
            case GlyphKind.Pause:
                context.FillRectangle(brush, R(3, 2.5, 3.6, 11));
                context.FillRectangle(brush, R(9.4, 2.5, 3.6, 11));
                break;
            case GlyphKind.Stop:
                context.FillRectangle(brush, R(3, 3, 10, 10));
                break;
            case GlyphKind.Prev:
                context.FillRectangle(brush, R(2, 2.5, 2.4, 11));
                Poly(P(14, 2.5), P(5, 8), P(14, 13.5));
                break;
            case GlyphKind.Next:
                Poly(P(2, 2.5), P(11, 8), P(2, 13.5));
                context.FillRectangle(brush, R(11.6, 2.5, 2.4, 11));
                break;
            case GlyphKind.Eject:
                Poly(P(2, 9.5), P(8, 3), P(14, 9.5));
                context.FillRectangle(brush, R(2, 11.5, 12, 2.5));
                break;
            case GlyphKind.Menu:
                context.DrawLine(pen, P(3, 4.5), P(13, 4.5));
                context.DrawLine(pen, P(3, 8), P(13, 8));
                context.DrawLine(pen, P(3, 11.5), P(13, 11.5));
                break;
            case GlyphKind.Compact:
                context.DrawRectangle(null, pen, R(2.5, 5.5, 11, 5));
                context.DrawLine(pen, P(2.5, 8), P(13.5, 8));
                break;
            case GlyphKind.Minimize:
                context.DrawLine(pen, P(3.5, 11.5), P(12.5, 11.5));
                break;
            case GlyphKind.Close:
                context.DrawLine(pen, P(4, 4), P(12, 12));
                context.DrawLine(pen, P(12, 4), P(4, 12));
                break;
            case GlyphKind.Logo:
            {
                // Uma cassete: corpo, as duas bobinas e a peça de baixo.
                var thin = new Pen(brush, Math.Max(1, 1.2 * s), lineJoin: PenLineJoin.Round);
                context.DrawRectangle(null, thin, new RoundedRect(R(1, 3, 14, 10.5), 1.6 * s));
                context.DrawEllipse(null, thin, P(5.4, 7.6), 1.6 * s, 1.6 * s);
                context.DrawEllipse(null, thin, P(10.6, 7.6), 1.6 * s, 1.6 * s);
                context.DrawLine(thin, P(5.4, 9.2), P(10.6, 9.2));
                var foot = new StreamGeometry();
                using (var c = foot.Open())
                {
                    c.BeginFigure(P(4, 13.5), false);
                    c.LineTo(P(5, 11.3));
                    c.LineTo(P(11, 11.3));
                    c.LineTo(P(12, 13.5));
                    c.EndFigure(false);
                }
                context.DrawGeometry(null, thin, foot);
                break;
            }
            case GlyphKind.Grip:
            {
                var dot = Math.Max(1, 1.2 * s);
                foreach (var (x, y) in new[] { (13.0, 13.0), (9.0, 13.0), (13.0, 9.0), (5.0, 13.0), (9.0, 9.0), (13.0, 5.0) })
                    context.DrawEllipse(brush, null, P(x, y), dot, dot);
                break;
            }
        }
    }
}
