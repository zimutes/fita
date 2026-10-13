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
/// Apanha o rato em toda a sua área (não só onde há desenho), e a barra de posição ainda numa
/// margem invisível em cima e em baixo (<see cref="HitPadding"/>), para não ser preciso acertar
/// na calha.
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
    /// <summary>Na barra de posição: as marcas do ciclo A-B (frações de 0 a 1).</summary>
    public static readonly StyledProperty<double?> MarkAProperty =
        AvaloniaProperty.Register<LevelSlider, double?>(nameof(MarkA));
    public static readonly StyledProperty<double?> MarkBProperty =
        AvaloniaProperty.Register<LevelSlider, double?>(nameof(MarkB));
    /// <summary>Na barra de posição: a forma de onda da faixa (0 a 1), desenhada no lugar da calha.</summary>
    public static readonly StyledProperty<float[]?> WaveformProperty =
        AvaloniaProperty.Register<LevelSlider, float[]?>(nameof(Waveform));
    /// <summary>Na barra de posição, no modo producer: onde satura (frações de 0 a 1), marcado em cima.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> ClipMarksProperty =
        AvaloniaProperty.Register<LevelSlider, IReadOnlyList<double>?>(nameof(ClipMarks));
    /// <summary>Na barra de posição, no modo producer: onde há notas, marcado em baixo.</summary>
    public static readonly StyledProperty<IReadOnlyList<double>?> NoteMarksProperty =
        AvaloniaProperty.Register<LevelSlider, IReadOnlyList<double>?>(nameof(NoteMarks));
    /// <summary>Na horizontal: margem em cima e em baixo que apanha o rato mas não se desenha.</summary>
    public static readonly StyledProperty<double> HitPaddingProperty =
        AvaloniaProperty.Register<LevelSlider, double>(nameof(HitPadding));

    static LevelSlider()
    {
        AffectsRender<LevelSlider>(MinimumProperty, MaximumProperty, ValueProperty, OriginProperty,
            OrientationProperty, IsEnabledProperty, IsSeekBarProperty, WaveformProperty, MarkAProperty, MarkBProperty,
            HitPaddingProperty, ClipMarksProperty, NoteMarksProperty);
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
    public double HitPadding { get => GetValue(HitPaddingProperty); set => SetValue(HitPaddingProperty, value); }
    public IReadOnlyList<double>? ClipMarks { get => GetValue(ClipMarksProperty); set => SetValue(ClipMarksProperty, value); }
    public IReadOnlyList<double>? NoteMarks { get => GetValue(NoteMarksProperty); set => SetValue(NoteMarksProperty, value); }
    public double? MarkA { get => GetValue(MarkAProperty); set => SetValue(MarkAProperty, value); }
    public double? MarkB { get => GetValue(MarkBProperty); set => SetValue(MarkBProperty, value); }

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
        Stir(point.Position.X - Pad, strong: true);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_dragging)
            SetByUser(ValueAt(p));
        if (_hover || _dragging)
            Stir(p.X - Pad, strong: false);
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
        _hoverAt = null;
        Animate();
        InvalidateVisual();
    }

    // ── A onda ao passar o rato (só na forma de onda da barra de posição) ─────────────────────
    // Como no leitor do estufa.org: as barras debaixo do cursor sobem (a lupa) e cada movimento
    // solta uma onda que corre para os dois lados e se vai apagando. O jeito da onda (rapidez,
    // largura, degraus, cor da crista) é do tema: ThemeDef.Ripple.

    private readonly record struct Ripple(double At, long Born, double Strength);

    private readonly List<Ripple> _ripples = [];
    private double? _hoverAt;
    private double _lens;
    private double _lastRippleAt = double.NaN;
    private long _lastRippleTime;
    private long _lastFrame;
    private bool _animating;

    private bool WaveActive => IsSeekBar && !Vertical && IsEnabled && Waveform is { Length: > 1 };

    private void Stir(double at, bool strong)
    {
        if (!WaveActive)
            return;
        var now = Environment.TickCount64;
        _hoverAt = at;
        if (strong || double.IsNaN(_lastRippleAt) || Math.Abs(at - _lastRippleAt) > 7 || now - _lastRippleTime > 140)
        {
            _ripples.Add(new Ripple(at, now, strong ? 1 : 0.5));
            if (_ripples.Count > 8)
                _ripples.RemoveAt(0);
            _lastRippleAt = at;
            _lastRippleTime = now;
        }
        Animate();
    }

    private void Animate()
    {
        if (_animating || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _animating = true;
        top.RequestAnimationFrame(Frame);
    }

    private void Frame(TimeSpan _)
    {
        _animating = false;
        var now = Environment.TickCount64;
        var dt = _lastFrame == 0 ? 1 / 60.0 : Math.Clamp((now - _lastFrame) / 1000.0, 0, 0.1);
        _lastFrame = now;
        var target = _hoverAt is null || !WaveActive ? 0.0 : 1.0;
        _lens += (target - _lens) * (1 - Math.Exp(-dt * 11));
        // Uma onda acaba quando já só tem 1 % da força.
        var life = (long)(1000 * 4.6 / Math.Max(0.3, ThemeManager.Current.Ripple.Decay));
        _ripples.RemoveAll(r => now - r.Born > life);
        InvalidateVisual();
        if (Math.Abs(target - _lens) < 0.005 && _ripples.Count == 0)
        {
            _lens = target;
            _lastFrame = 0;
            return;
        }
        Animate();
    }

    /// <summary>Quanto sobe (px) a barra que está em <paramref name="at"/>, e a força da crista ali (0 a 1).</summary>
    private (double Lift, double Crest) WaveLift(WaveRipple r, double at, long now)
    {
        var lift = 0.0;
        if (_hoverAt is { } hx && _lens > 0.001)
        {
            var d = at - hx;
            lift += r.Lens * _lens * Math.Exp(-d * d / (2 * r.LensWidth * r.LensWidth));
        }
        var crest = 0.0;
        foreach (var w in _ripples)
        {
            var age = (now - w.Born) / 1000.0;
            if (r.Fps > 0)
                age = Math.Floor(age * r.Fps) / r.Fps;
            var k = w.Strength * Math.Exp(-age * r.Decay);
            var d = Math.Abs(at - w.At) - age * r.Speed;
            double shape;
            if (r.Crests > 1)
            {
                // Uma ondulação: várias cristas debaixo de um envelope, como a rebentação.
                var spread = r.Width * r.Crests * 0.8;
                shape = Math.Cos(2 * Math.PI * d / (r.Width * 3.2)) * Math.Exp(-d * d / (2 * spread * spread));
            }
            else
            {
                // Uma crista com uma cava atrás.
                var s2 = 2 * r.Width * r.Width;
                var behind = d + 2.25 * r.Width;
                shape = Math.Exp(-d * d / s2) - 0.45 * Math.Exp(-behind * behind / s2);
            }
            lift += k * r.Lift * shape;
            crest = Math.Max(crest, k * shape);
        }
        return (lift, Math.Clamp(crest, 0, 1));
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
        var full = Bounds.Height;
        if (w <= 0 || full <= 0)
            return;
        // Fundo transparente: assim o rato conta em toda a área, e não só onde há traço.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        _slop = Vertical ? 0 : Math.Clamp(HitPadding, 0, Math.Max(0, (full - 4) / 2));
        var h = full - 2 * _slop;
        var dim = IsEnabled ? 1.0 : 0.45;
        using var opacity = context.PushOpacity(dim);
        using var shift = context.PushTransform(Matrix.CreateTranslation(0, _slop));
        try
        {
            RenderLook(context, t, w, h);
        }
        finally
        {
            if (IsSeekBar && !Vertical && IsEnabled)
            {
                DrawLoopMarks(context, t, w, h);
                DrawTicks(context, t, w, h);
            }
        }
    }

    /// <summary>Saturações (traços curtos em cima, na cor de alerta) e notas (em baixo, na de destaque).</summary>
    private void DrawTicks(DrawingContext context, ThemeDef t, double w, double h)
    {
        var length = w - 2 * Pad;
        if (ClipMarks is { Count: > 0 } clips)
        {
            var brush = new SolidColorBrush(t.Brand);
            foreach (var f in clips)
                context.FillRectangle(brush, new Rect(Pad + Math.Clamp(f, 0, 1) * length - 1, -_slop, 2, _slop + 3));
        }
        if (NoteMarks is { Count: > 0 } notes)
        {
            var brush = new SolidColorBrush(t.Accent);
            foreach (var f in notes)
                context.FillRectangle(brush, new Rect(Pad + Math.Clamp(f, 0, 1) * length - 1.5, h - 3, 3, _slop + 3));
        }
    }

    /// <summary>A margem de clique desta vez (a forma de onda pode crescer para dentro dela).</summary>
    private double _slop;

    /// <summary>As marcas do ciclo A-B: um traço em cada ponta e, ligado, a zona entre elas realçada.</summary>
    private void DrawLoopMarks(DrawingContext context, ThemeDef t, double w, double h)
    {
        if (MarkA is not { } a)
            return;
        var length = w - 2 * Pad;
        var brush = new SolidColorBrush(t.Accent);
        var xa = Pad + a * length;
        if (MarkB is { } b)
        {
            var xb = Pad + b * length;
            context.FillRectangle(new SolidColorBrush(t.Accent, 0.18), new Rect(xa, 0, Math.Max(1, xb - xa), h));
            context.FillRectangle(brush, new Rect(xb - 1, 0, 2, h));
        }
        context.FillRectangle(brush, new Rect(xa - 1, 0, 2, h));
    }

    private void RenderLook(DrawingContext context, ThemeDef t, double w, double h)
    {

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
    /// degraus, como blocos. Com o rato por cima, as barras sobem e ondulam (podem crescer para a
    /// margem de clique), e o troço entre a posição e o rato fica meio aceso.
    /// </summary>
    private void DrawWaveform(DrawingContext context, ThemeDef t, float[] wave, double f, double length, double h)
    {
        var blocky = t.Slider == SliderLook.Block;
        var bar = blocky ? 3.0 : 2.0;
        var gap = blocky ? 1.0 : 1.0;
        var max = h - 4;
        var mid = h / 2;
        var tallest = h + 2 * _slop - 2;
        var played = new SolidColorBrush(t.SliderFill);
        var rest = new SolidColorBrush(t.SliderTrack);
        var ahead = new SolidColorBrush(t.SliderFill, 0.45);
        var head = Pad + f * length;
        var ripple = t.Ripple;
        var step = blocky ? Math.Max(2, ripple.Step) : ripple.Step;
        var moving = _lens > 0.001 || _ripples.Count > 0;
        var now = Environment.TickCount64;
        double? hover = _hoverAt is { } hx && _lens > 0.05 ? Pad + Math.Clamp(hx, 0, length) : null;
        for (var x = 0.0; x + bar <= length + 0.01; x += bar + gap)
        {
            var i = (int)(x / length * wave.Length);
            var to = Math.Max(i + 1, (int)((x + bar) / length * wave.Length));
            var v = 0f;
            for (var j = i; j < Math.Min(to, wave.Length); j++)
                v = Math.Max(v, wave[j]);
            var height = v * max;
            var crest = 0.0;
            if (moving)
            {
                var (lift, c) = WaveLift(ripple, x + bar / 2, now);
                height += lift;
                crest = c;
            }
            height = Math.Clamp(height, blocky ? 2 : 1.5, tallest);
            if (step > 0)
                height = Math.Max(step, Math.Round(height / step) * step);
            var r = new Rect(Pad + x, mid - height / 2, bar, height);
            var centre = Pad + x + bar / 2;
            var brush = centre <= head ? played : hover is { } hv && centre <= hv ? ahead : rest;
            if (crest > 0.04)
                brush = new SolidColorBrush(Lerp(brush.Color, ripple.Crest, crest), brush.Opacity + (1 - brush.Opacity) * crest);
            context.FillRectangle(brush, r);
        }
        if (hover is { } line)
            context.FillRectangle(new SolidColorBrush(t.Text, 0.55 * _lens), new Rect(line - 0.5, 1, 1, h - 2));
        var thick = _hover || _dragging ? 3.0 : 2.0;
        context.FillRectangle(new SolidColorBrush(_hover || _dragging ? t.Text : t.SliderThumb),
            new Rect(head - thick / 2, 1, thick, h - 2));
    }

    private static Color Lerp(Color a, Color b, double k) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * k), (byte)(a.R + (b.R - a.R) * k),
        (byte)(a.G + (b.G - a.G) * k), (byte)(a.B + (b.B - a.B) * k));

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
