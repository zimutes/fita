using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Fita.Controls;

/// <summary>
/// O balanço tonal de uma faixa em seis barrinhas (dos graves aos agudos), para cima ou para
/// baixo em relação à comparação (as referências, ou a média da lista). Até ±6 dB; a partir de
/// 3 dB na cor de alerta.
/// </summary>
public sealed class BandBars : Control
{
    public static readonly StyledProperty<double[]?> DeltasProperty =
        AvaloniaProperty.Register<BandBars, double[]?>(nameof(Deltas));

    static BandBars()
    {
        AffectsRender<BandBars>(DeltasProperty);
    }

    // Há uma por linha da lista: só ouve o tema enquanto está à vista, para não ficar presa.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.Changed += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeManager.Changed -= InvalidateVisual;
    }

    public double[]? Deltas { get => GetValue(DeltasProperty); set => SetValue(DeltasProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(6 * 4 - 1, 14);

    public override void Render(DrawingContext context)
    {
        var t = ThemeManager.Current;
        var h = Bounds.Height;
        var mid = Math.Round(h / 2);
        if (Deltas is not { Length: > 0 } deltas)
            return;
        context.FillRectangle(new SolidColorBrush(t.TextMuted, 0.5), new Rect(0, mid, Bounds.Width, 1));
        var normal = new SolidColorBrush(t.Accent);
        var alert = new SolidColorBrush(t.Brand);
        for (var i = 0; i < deltas.Length; i++)
        {
            var d = Math.Clamp(deltas[i], -6, 6);
            var size = Math.Max(1, Math.Abs(d) / 6 * (mid - 1));
            var r = d >= 0 ? new Rect(i * 4, mid - size, 3, size) : new Rect(i * 4, mid + 1, 3, size);
            context.FillRectangle(Math.Abs(deltas[i]) >= 3 ? alert : normal, r);
        }
    }
}
