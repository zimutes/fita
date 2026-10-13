using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Fita.Controls;

/// <summary>
/// Controlo de deslizar desenhado à mão, com o aspeto do tema (cápsula, relevo, blocos, fio,
/// losango). Serve para a posição, o volume, o balanço e as bandas do equalizador.
/// Clicar salta para o ponto; roda do rato em passos; duplo clique volta ao valor por omissão.
/// </summary>
public sealed class LevelSlider : Control
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<LevelSlider, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LevelSlider, double>(nameof(Maximum), 1);
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<LevelSlider, double>(nameof(Value));
    /// <summary>De onde parte o enchimento (o 0 dB de uma banda, o centro do balanço).</summary>
    public static readonly StyledProperty<double?> OriginProperty =
        AvaloniaProperty.Register<LevelSlider, double?>(nameof(Origin));
    public static readonly StyledProperty<double?> DefaultValueProperty =
        AvaloniaProperty.Register<LevelSlider, double?>(nameof(DefaultValue));
    public static readonly StyledProperty<double> WheelStepProperty =
        AvaloniaProperty.Register<LevelSlider, double>(nameof(WheelStep), 0.05);
    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<LevelSlider, Orientation>(nameof(Orientation));
    /// <summary>Barra de posição: mais fina e sem cursor enquanto não há faixa.</summary>
    public static readonly StyledProperty<bool> IsSeekBarProperty =
        AvaloniaProperty.Register<LevelSlider, bool>(nameof(IsSeekBar));
    /// <summary>Na barra de posição: a forma de onda da faixa (0 a 1), desenhada no lugar da calha.</summary>
    public static readonly StyledProperty<float[]?> WaveformProperty =
        AvaloniaProperty.Register<LevelSlider, float[]?>(nameof(Waveform));

    static LevelSlider()
    {
        AffectsRender<LevelSlider>(MinimumProperty, MaximumProperty, ValueProperty, OriginProperty,
            OrientationProperty, IsEnabledProperty, IsSeekBarProperty, WaveformProperty);
        FocusableProperty.OverrideDefaultValue<LevelSlider>(false);
    }

    private bool _dragging;
    private bool _hover;

    public LevelSlider()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ThemeManager.Changed += InvalidateVisual;
    }

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double? Origin { get => GetValue(OriginProperty); set => SetValue(OriginProperty, value); }
    public double? DefaultValue { get => GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }
    public double WheelStep { get => GetValue(WheelStepProperty); set => SetValue(WheelStepProperty, value); }
    public Orientation Orientation { get => GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public bool IsSeekBar { get => GetValue(IsSeekBarProperty); set => SetValue(IsSeekBarProperty, value); }
    public float[]? Waveform { get => GetValue(WaveformProperty); set => SetValue(WaveformProperty, value); }

    public bool IsDragging => _dragging;

    /// <summary>O valor mudou pela mão do utilizador (arrastar, roda, duplo clique).</summary>
    public event EventHandler<double>? UserChanged;
    /// <summary>O utilizador largou o controlo (a barra de posição só salta aqui).</summary>
    public event EventHandler<double>? Committed;

    private bool Vertical => Orientation == Orientation.Vertical;

    private double Fraction(double v)
    {
        var range = Maximum - Minimum;
        return range <= 0 ? 0 : Math.Clamp((v - Minimum) / range, 0, 1);
    }

    private const double Pad = 7;

    private double ValueAt(Point p)
    {
        var length = (Vertical ? Bounds.Height : Bounds.Width) - 2 * Pad;
        var pos = Vertical ? Bounds.Height - Pad - p.Y : p.X - Pad;
        var f = length <= 0 ? 0 : Math.Clamp(pos / length, 0, 1);
        return Minimum + f * (Maximum - Minimum);
    }

    private void SetByUser(double v)
    {
        v = Math.Clamp(v, Minimum, Maximum);
        if (Math.Abs(v - Value) < 1e-9)
            return;
        Value = v;
        UserChanged?.Invoke(this, v);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed || !IsEnabled)
            return;
        if (e.ClickCount == 2 && DefaultValue is { } d)
        {
            SetByUser(d);
            Committed?.Invoke(this, Value);
            e.Handled = true;
            return;
        }
        _dragging = true;
        e.Pointer.Capture(this);
        SetByUser(ValueAt(point.Position));
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
            SetByUser(ValueAt(e.GetPosition(this)));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging)
            return;
        _dragging = false;
        e.Pointer.Capture(null);
        Committed?.Invoke(this, Value);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_dragging)
        {
            _dragging = false;
            Committed?.Invoke(this, Value);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _hover = true;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = false;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsEnabled)
            return;
        var delta = e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        SetByUser(Value + Math.Sign(delta) * WheelStep);
        Committed?.Invoke(this, Value);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var t = ThemeManager.Current;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;
        var dim = IsEnabled ? 1.0 : 0.45;
        using var opacity = context.PushOpacity(dim);

        var length = (Vertical ? h : w) - 2 * Pad;
        var cross = Vertical ? w / 2 : h / 2;
        var f = Fraction(Value);
        var o = Fraction(Origin ?? Minimum);
        var showThumb = !IsSeekBar || IsEnabled;

        // Converte (posição ao longo, desvio de lado) para coordenadas.
        Point At(double along, double side) =>
            Vertical ? new Point(cross + side, h - Pad - along) : new Point(Pad + along, cross + side);
        Rect Span(double from, double to, double thickness)
        {
            var a = Math.Min(from, to) * length;
            var b = Math.Max(from, to) * length;
            return Vertical
                ? new Rect(cross - thickness / 2, h - Pad - b, thickness, b - a)
                : new Rect(Pad + a, cross - thickness / 2, b - a, thickness);
        }

        var track = new SolidColorBrush(t.SliderTrack);
        var fill = new SolidColorBrush(t.SliderFill);
        var thumbPos = At(f * length, 0);

        if (IsSeekBar && !Vertical && IsEnabled && Waveform is { Length: > 1 } wave)
        {
            DrawWaveform(context, t, wave, f, length, h);
            return;
        }

        switch (t.Slider)
        {
            case SliderLook.Pill:
            {
                var th = IsSeekBar ? 5 : 4;
                if (_hover || _dragging) th += 1;
                context.DrawRectangle(track, null, new RoundedRect(Span(0, 1, th), th / 2));
                if (Math.Abs(f - o) > 0.0001)
                    context.DrawRectangle(fill, null, new RoundedRect(Span(o, f, th), th / 2));
                if (showThumb)
                {
                    var r = _dragging ? 7 : 6;
                    var glow = t.SliderThumbGlow.A > 0
                        ? new BoxShadows(new BoxShadow { Blur = _hover || _dragging ? 14 : 9, Color = t.SliderThumbGlow })
                        : default;
                    context.DrawRectangle(new SolidColorBrush(t.SliderThumb), null,
                        new RoundedRect(new Rect(thumbPos.X - r, thumbPos.Y - r, 2 * r, 2 * r), r), glow);
                }
                break;
            }
            case SliderLook.Bevel:
            {
                // Calha afundada de 4 px: sombra em cima/esquerda, luz em baixo/direita.
                var g = Span(0, 1, 4);
                context.FillRectangle(new SolidColorBrush(Color.Parse("#808080")), g);
                context.FillRectangle(Brushes.White, Vertical ? new Rect(g.Right - 1, g.Y, 1, g.Height) : new Rect(g.X, g.Bottom - 1, g.Width, 1));
                context.FillRectangle(Brushes.Black, Vertical ? new Rect(g.X + 1, g.Y + 1, 1, g.Height - 2) : new Rect(g.X + 1, g.Y + 1, g.Width - 2, 1));
                if (Math.Abs(f - o) > 0.0001)
                    context.FillRectangle(fill, Span(o, f, 2));
                if (showThumb)
                {
                    var tw = Vertical ? 18.0 : 10.0;
                    var tl = Vertical ? 10.0 : 18.0;
                    if (IsSeekBar && !Vertical) tl = Math.Min(16, h);
                    var rect = new Rect(thumbPos.X - tw / 2, thumbPos.Y - tl / 2, tw, tl);
                    context.FillRectangle(new SolidColorBrush(t.SliderThumb), rect);
                    Bevel(context, rect, raised: !_dragging);
                }
                break;
            }
            case SliderLook.Block:
            {
                var th = IsSeekBar ? 10.0 : 9.0;
                var outline = Span(0, 1, th);
                context.DrawRectangle(null, new Pen(track, 1), outline.Deflate(0.5));
                var inner = outline.Deflate(2.5);
                var cell = 5.0;
                var gap = 2.0;
                var innerLength = Vertical ? inner.Height : inner.Width;
                var from = Math.Min(o, f) * innerLength;
                var to = Math.Max(o, f) * innerLength;
                for (var x = 0.0; x + cell <= innerLength + 0.5; x += cell + gap)
                {
                    var mid = x + cell / 2;
                    if (mid < from || mid > to)
                        continue;
                    var r = Vertical
                        ? new Rect(inner.X, inner.Bottom - x - cell, inner.Width, cell)
                        : new Rect(inner.X + x, inner.Y, cell, inner.Height);
                    context.FillRectangle(fill, r);
                }
                if (showThumb)
                {
                    // Cursor de terminal: um bloco que atravessa a calha, aceso ao passar por cima.
                    var c = Vertical
                        ? new Rect(outline.X - 2, thumbPos.Y - 1.5, outline.Width + 4, 3)
                        : new Rect(thumbPos.X - 1.5, outline.Y - 2, 3, outline.Height + 4);
                    context.FillRectangle(new SolidColorBrush(_hover || _dragging ? t.Text : t.SliderThumb), c);
                }
                break;
            }
            case SliderLook.Hairline:
            {
                context.FillRectangle(track, Span(0, 1, 2));
                if (Math.Abs(f - o) > 0.0001)
                    context.FillRectangle(fill, Span(o, f, 2));
                if (showThumb)
                {
                    var r = _hover || _dragging ? 6.0 : 4.5;
                    context.DrawEllipse(new SolidColorBrush(t.SliderThumb), null, thumbPos, r, r);
                }
                break;
            }
            case SliderLook.Jewel:
            {
                context.FillRectangle(track, Span(0, 1, 1));
                if (Math.Abs(f - o) > 0.0001)
                    context.FillRectangle(fill, Span(o, f, 1.5));
                if (showThumb)
                {
                    var r = _hover || _dragging ? 6.5 : 5.0;
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(thumbPos.X, thumbPos.Y - r), true);
                        c.LineTo(new Point(thumbPos.X + r, thumbPos.Y));
                        c.LineTo(new Point(thumbPos.X, thumbPos.Y + r));
                        c.LineTo(new Point(thumbPos.X - r, thumbPos.Y));
                        c.EndFigure(true);
                    }
                    context.DrawGeometry(new SolidColorBrush(t.SliderThumb), new Pen(new SolidColorBrush(t.Base), 1), g);
                }
                break;
            }
        }
    }

    /// <summary>
    /// A forma de onda no lugar da calha, na mesma altura: o que já tocou na cor do enchimento,
    /// o resto na da calha, e uma linha fina na posição. No tema de terminal as barras vão aos
    /// degraus, como blocos.
    /// </summary>
    private void DrawWaveform(DrawingContext context, ThemeDef t, float[] wave, double f, double length, double h)
    {
        var blocky = t.Slider == SliderLook.Block;
        var bar = blocky ? 3.0 : 2.0;
        var gap = blocky ? 1.0 : 1.0;
        var max = h - 4;
        var mid = h / 2;
        var played = new SolidColorBrush(t.SliderFill);
        var rest = new SolidColorBrush(t.SliderTrack);
        var head = Pad + f * length;
        for (var x = 0.0; x + bar <= length + 0.01; x += bar + gap)
        {
            var i = (int)(x / length * wave.Length);
            var to = Math.Max(i + 1, (int)((x + bar) / length * wave.Length));
            var v = 0f;
            for (var j = i; j < Math.Min(to, wave.Length); j++)
                v = Math.Max(v, wave[j]);
            var height = Math.Max(blocky ? 2 : 1.5, v * max);
            if (blocky)
                height = Math.Max(2, Math.Round(height / 2) * 2);
            var r = new Rect(Pad + x, mid - height / 2, bar, height);
            context.FillRectangle(Pad + x + bar / 2 <= head ? played : rest, r);
        }
        var thick = _hover || _dragging ? 3.0 : 2.0;
        context.FillRectangle(new SolidColorBrush(_hover || _dragging ? t.Text : t.SliderThumb),
            new Rect(head - thick / 2, 1, thick, h - 2));
    }

    /// <summary>Moldura de Windows 95: luz em cima/esquerda e sombra em baixo/direita (ou o contrário).</summary>
    internal static void Bevel(DrawingContext context, Rect r, bool raised)
    {
        var light = raised ? Brushes.White : Brushes.Black;
        var dark = raised ? Brushes.Black : Brushes.White;
        var light2 = new SolidColorBrush(Color.Parse(raised ? "#DFDFDF" : "#808080"));
        var dark2 = new SolidColorBrush(Color.Parse(raised ? "#808080" : "#DFDFDF"));
        context.FillRectangle(light, new Rect(r.X, r.Y, r.Width, 1));
        context.FillRectangle(light, new Rect(r.X, r.Y, 1, r.Height));
        context.FillRectangle(dark, new Rect(r.X, r.Bottom - 1, r.Width, 1));
        context.FillRectangle(dark, new Rect(r.Right - 1, r.Y, 1, r.Height));
        context.FillRectangle(light2, new Rect(r.X + 1, r.Y + 1, r.Width - 2, 1));
        context.FillRectangle(light2, new Rect(r.X + 1, r.Y + 1, 1, r.Height - 2));
        context.FillRectangle(dark2, new Rect(r.X + 1, r.Bottom - 2, r.Width - 2, 1));
        context.FillRectangle(dark2, new Rect(r.Right - 2, r.Y + 1, 1, r.Height - 2));
    }
}
