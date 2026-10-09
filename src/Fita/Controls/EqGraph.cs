using System.Globalization;
using Fita.Audio;
using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Fita.Controls;

/// <summary>
/// Gráfico do equalizador: a curva de resposta real (soma dos filtros), o espectro ao vivo por
/// trás e um ponto por banda. Arrastar muda ganho e frequência; roda muda a largura (Q);
/// duplo clique põe a 0 dB; botão direito pede os detalhes da banda.
/// </summary>
public sealed class EqGraph : Control
{
    private const double MinHz = 20;
    private const double MaxHz = 20000;
    private const double RangeDb = 15;
    private const double HandleRadius = 5.5;

    private EqSettings? _settings;
    private Biquad[] _filters = [];
    private double _sampleRate = 48000;
    private int _hover = -1;
    private int _drag = -1;
    private Point _dragStart;
    private double _dragStartFreq;
    private bool _freqUnlocked;

    public SpectrumAnalyzer? Analyzer { get; set; }
    public int Selected { get; private set; } = -1;

    /// <summary>Uma banda mudou pela mão do utilizador.</summary>
    public event Action<int>? BandChanged;
    /// <summary>Botão direito numa banda: mostrar os detalhes (índice, posição no controlo).</summary>
    public event Action<int, Point>? DetailsRequested;

    public EqGraph()
    {
        ThemeManager.Changed += InvalidateVisual;
        ClipToBounds = true;
    }

    public void SetSettings(EqSettings settings, double sampleRate)
    {
        _settings = settings;
        _sampleRate = sampleRate > 0 ? sampleRate : 48000;
        _filters = EqMath.Filters(settings, _sampleRate);
        InvalidateVisual();
    }

    private Rect Plot => new(4, 6, Math.Max(1, Bounds.Width - 8), Math.Max(1, Bounds.Height - 20));

    private double X(double hz) => Plot.X + Math.Log(hz / MinHz) / Math.Log(MaxHz / MinHz) * Plot.Width;
    private double Hz(double x) => MinHz * Math.Pow(MaxHz / MinHz, Math.Clamp((x - Plot.X) / Plot.Width, 0, 1));
    private double Y(double db) => Plot.Y + (RangeDb - db) / (2 * RangeDb) * Plot.Height;
    private double Db(double y) => RangeDb - (y - Plot.Y) / Plot.Height * 2 * RangeDb;

    private Point HandlePoint(EqBand b) => new(X(b.Frequency), Y(b.UsesGain ? b.Gain : 0));

    private int HitTest(Point p)
    {
        if (_settings is null)
            return -1;
        var best = -1;
        var bestDistance = 10.0;
        for (var i = 0; i < _settings.Bands.Count; i++)
        {
            var d = Point.Distance(p, HandlePoint(_settings.Bands[i]));
            if (d < bestDistance)
            {
                best = i;
                bestDistance = d;
            }
        }
        return best;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag >= 0 && _settings is not null)
        {
            var band = _settings.Bands[_drag];
            // A frequência só se mexe depois de um empurrão de lado: subir e descer um ponto
            // não o deve arrastar sem querer para outra frequência.
            if (!_freqUnlocked && Math.Abs(p.X - _dragStart.X) > 6)
                _freqUnlocked = true;
            if (_freqUnlocked)
                band.Frequency = Math.Round(Math.Clamp(Hz(p.X), MinHz, MaxHz), band.Frequency < 1000 ? 0 : -1);
            else
                band.Frequency = _dragStartFreq;
            if (band.UsesGain)
                band.Gain = Math.Round(Math.Clamp(Db(p.Y), -EqSettings.MaxGain, EqSettings.MaxGain) * 2) / 2;
            BandChanged?.Invoke(_drag);
            return;
        }
        var hit = HitTest(p);
        if (hit != _hover)
        {
            _hover = hit;
            Cursor = hit >= 0 ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        var hit = HitTest(point.Position);
        if (hit < 0 || _settings is null)
            return;
        Selected = hit;
        e.Handled = true;
        if (point.Properties.IsRightButtonPressed)
        {
            DetailsRequested?.Invoke(hit, HandlePoint(_settings.Bands[hit]));
            InvalidateVisual();
            return;
        }
        if (e.ClickCount == 2)
        {
            _settings.Bands[hit].Gain = 0;
            BandChanged?.Invoke(hit);
            return;
        }
        _drag = hit;
        _dragStart = point.Position;
        _dragStartFreq = _settings.Bands[hit].Frequency;
        _freqUnlocked = false;
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0)
            return;
        _drag = -1;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover >= 0 && _drag < 0)
        {
            _hover = -1;
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var hit = _drag >= 0 ? _drag : HitTest(e.GetPosition(this));
        if (hit < 0 || _settings is null)
            return;
        var band = _settings.Bands[hit];
        var factor = e.Delta.Y > 0 ? 1.12 : 1 / 1.12;
        band.Q = Math.Round(Math.Clamp(band.Q * factor, 0.2, 12), 2);
        Selected = hit;
        BandChanged?.Invoke(hit);
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var t = ThemeManager.Current;
        var plot = Plot;
        var grid = new Pen(new SolidColorBrush(t.GraphGrid), 1);
        var muted = new SolidColorBrush(t.TextMuted);
        var typeface = new Typeface(t.FontMono);

        // Grelha: frequências e dB.
        foreach (var hz in new[] { 50.0, 100, 200, 500, 1000, 2000, 5000, 10000 })
        {
            var x = Math.Round(X(hz)) + 0.5;
            context.DrawLine(grid, new Point(x, plot.Y), new Point(x, plot.Bottom));
            var label = hz >= 1000 ? (hz / 1000).ToString(CultureInfo.InvariantCulture) + "k" : hz.ToString(CultureInfo.InvariantCulture);
            var text = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, 9, muted);
            context.DrawText(text, new Point(x - text.Width / 2, plot.Bottom + 2));
        }
        foreach (var db in new[] { -12.0, -6, 6, 12 })
        {
            var y = Math.Round(Y(db)) + 0.5;
            context.DrawLine(grid, new Point(plot.X, y), new Point(plot.Right, y));
        }
        var zero = Math.Round(Y(0)) + 0.5;
        context.DrawLine(new Pen(new SolidColorBrush(t.GraphGrid), 1.5), new Point(plot.X, zero), new Point(plot.Right, zero));

        // Espectro ao vivo, de baixo para cima.
        if (Analyzer is { } a)
        {
            var fine = a.Fine;
            var spectrum = new StreamGeometry();
            using (var g = spectrum.Open())
            {
                g.BeginFigure(new Point(plot.X, plot.Bottom), true);
                for (var i = 0; i < fine.Length; i++)
                {
                    var x = X(SpectrumAnalyzer.FineFrequency(i, fine.Length));
                    g.LineTo(new Point(x, plot.Bottom - fine[i] * plot.Height * 0.95));
                }
                g.LineTo(new Point(plot.Right, plot.Bottom));
                g.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(t.GraphSpectrum), null, spectrum);
        }

        if (_settings is null)
            return;
        var enabled = _settings.Enabled;
        using var opacity = context.PushOpacity(enabled ? 1 : 0.4);

        // Curva de resposta.
        var points = new List<Point>();
        for (var x = plot.X; x <= plot.Right; x += 2)
        {
            var db = Math.Clamp(EqMath.ResponseDb(_filters, Hz(x), _sampleRate), -RangeDb - 3, RangeDb + 3);
            points.Add(new Point(x, Y(db)));
        }
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, zero), true);
            foreach (var p in points)
                g.LineTo(p);
            g.LineTo(new Point(points[^1].X, zero));
            g.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(t.GraphFill), null, area);
        var curve = new StreamGeometry();
        using (var g = curve.Open())
        {
            g.BeginFigure(points[0], false);
            for (var i = 1; i < points.Count; i++)
                g.LineTo(points[i]);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(new SolidColorBrush(t.GraphCurve), 1.8, lineJoin: PenLineJoin.Round), curve);

        // Pontos das bandas.
        var curveBrush = new SolidColorBrush(t.GraphCurve);
        var baseBrush = new SolidColorBrush(t.InsetBg.A > 200 ? t.InsetBg : t.Base);
        for (var i = 0; i < _settings.Bands.Count; i++)
        {
            var p = HandlePoint(_settings.Bands[i]);
            var active = i == _hover || i == _drag || i == Selected;
            var r = active ? HandleRadius + 1 : HandleRadius;
            context.DrawEllipse(active ? curveBrush : baseBrush, new Pen(curveBrush, 1.5), p, r, r);
        }

        // Legenda da banda sob o rato (ou a ser arrastada).
        var shown = _drag >= 0 ? _drag : _hover;
        if (shown >= 0)
        {
            var b = _settings.Bands[shown];
            var label = $"{FormatHz(b.Frequency)}  ·  {(b.UsesGain ? b.Gain.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB  ·  " : "")}Q {b.Q.ToString("0.00", CultureInfo.InvariantCulture)}  ·  {Loc.T(b.Type.ToString())}";
            var text = new FormattedText(label, Loc.Culture, FlowDirection.LeftToRight, typeface, 10, new SolidColorBrush(t.Text));
            var bg = new Rect(plot.X + 4, plot.Y + 2, text.Width + 10, text.Height + 4);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, t.MenuBg.R, t.MenuBg.G, t.MenuBg.B)), null, new RoundedRect(bg, 3));
            context.DrawText(text, new Point(bg.X + 5, bg.Y + 2));
        }
    }

    public static string FormatHz(double hz) =>
        hz >= 1000 ? (hz / 1000).ToString(hz % 1000 == 0 ? "0" : "0.0#", CultureInfo.InvariantCulture) + " kHz"
            : hz.ToString("0", CultureInfo.InvariantCulture) + " Hz";
}
