using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Fita.Controls;

/// <summary>Fundo da janela: a cor base e as luzes do tema (névoa, foco de montra, CRT…).</summary>
public sealed class Backdrop : Control
{
    public Backdrop()
    {
        ThemeManager.Changed += InvalidateVisual;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        var t = ThemeManager.Current;
        var rect = new Rect(Bounds.Size);
        context.FillRectangle(new SolidColorBrush(t.Base), rect);

        foreach (var glow in t.Glows)
        {
            var transparent = Color.FromArgb(0, glow.Color.R, glow.Color.G, glow.Color.B);
            var brush = new RadialGradientBrush
            {
                Center = new RelativePoint(glow.X, glow.Y, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(glow.X, glow.Y, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(glow.Radius, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(glow.Radius * 0.75, RelativeUnit.Relative),
                GradientStops = { new GradientStop(glow.Color, 0), new GradientStop(transparent, 1) },
            };
            context.FillRectangle(brush, rect);
        }

        if (t.BottomShade.A > 0)
        {
            var transparent = Color.FromArgb(0, t.BottomShade.R, t.BottomShade.G, t.BottomShade.B);
            context.FillRectangle(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(transparent, 0.38), new GradientStop(t.BottomShade, 1) },
            }, rect);
        }

        if (t.Scanlines.A > 0)
        {
            var brush = new SolidColorBrush(t.Scanlines);
            for (var y = 0.0; y < rect.Height; y += 3)
                context.FillRectangle(brush, new Rect(0, y, rect.Width, 1));
        }
    }
}
