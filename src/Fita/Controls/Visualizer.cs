using Fita.Audio;
using Fita.Themes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Fita.Controls;

/// <summary>
/// O visualizador do mostrador: espectro (barras, LEDs, barras finas ou onda, conforme o tema)
/// ou osciloscópio. Os dados vêm do <see cref="SpectrumAnalyzer"/>, atualizado pela janela.
/// </summary>
public sealed class Visualizer : Control
{
    public SpectrumAnalyzer? Analyzer { get; set; }
    public VisMode Mode { get; set; } = VisMode.Spectrum;

    public Visualizer()
    {
        ThemeManager.Changed += InvalidateVisual;
        ClipToBounds = true;
    }

    /// <summary>Quantas barras cabem, para o analisador as calcular.</summary>
    public int WantedBars()
    {
        var w = Bounds.Width;
        return ThemeManager.Current.Vis switch
        {
            VisLook.Segments => (int)(w / 6),
            VisLook.Thin => (int)(w / 4),
            VisLook.Wave => (int)(w / 5),
            _ => (int)(w / 5),
        };
    }

    public override void Render(DrawingContext context)
    {
        var a = Analyzer;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (a is null || w <= 0 || h <= 0 || Mode == VisMode.Off)
            return;
        var t = ThemeManager.Current;
        if (Mode == VisMode.Scope)
        {
            DrawScope(context, a, t, w, h);
            return;
        }
        switch (t.Vis)
        {
            case VisLook.Segments: DrawSegments(context, a, t, w, h); break;
            case VisLook.Wave: DrawWave(context, a, t, w, h); break;
            default: DrawBars(context, a, t, w, h, thin: t.Vis == VisLook.Thin); break;
        }
    }

    private static IBrush Vertical(Color bottom, Color top, double h) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, h, RelativeUnit.Absolute),
        EndPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
        GradientStops = { new GradientStop(bottom, 0), new GradientStop(top, 1) },
    };

    private static void DrawBars(DrawingContext c, SpectrumAnalyzer a, ThemeDef t, double w, double h, bool thin)
    {
        var bars = a.Bars;
        var n = bars.Length;
        var slot = w / n;
        var gap = thin ? Math.Max(1.5, slot * 0.45) : Math.Max(1, slot * 0.2);
        var bw = Math.Max(1, slot - gap);
        var brush = Vertical(t.VisLow, t.VisHigh, h);
        var peak = new SolidColorBrush(t.VisPeak);
        for (var i = 0; i < n; i++)
        {
            var x = i * slot + gap / 2;
            var bh = bars[i] * h;
            if (bh >= 0.5)
                c.FillRectangle(brush, new Rect(x, h - bh, bw, bh), thin ? 0 : (float)Math.Min(1.5, bw / 3));
            var py = h - a.Peaks[i] * h;
            if (a.Peaks[i] > 0.01)
                c.FillRectangle(peak, new Rect(x, Math.Min(h - 1.5, py - 1.5), bw, thin ? 1.5 : 2));
        }
    }

    private static void DrawSegments(DrawingContext c, SpectrumAnalyzer a, ThemeDef t, double w, double h)
    {
        var bars = a.Bars;
        var n = bars.Length;
        var slot = w / n;
        var bw = Math.Max(1, slot - 1);
        const double seg = 2;
        const double segGap = 1;
        var rows = (int)(h / (seg + segGap));
        var low = new SolidColorBrush(t.VisLow);
        var mid = new SolidColorBrush(t.VisMid.A > 0 ? t.VisMid : t.VisLow);
        var high = new SolidColorBrush(t.VisHigh);
        var off = new SolidColorBrush(Color.FromArgb(22, t.VisLow.R, t.VisLow.G, t.VisLow.B));
        var peak = new SolidColorBrush(t.VisPeak);
        for (var i = 0; i < n; i++)
        {
            var x = i * slot;
            var lit = (int)Math.Round(bars[i] * rows);
            var peakRow = (int)Math.Round(a.Peaks[i] * rows) - 1;
            for (var r = 0; r < rows; r++)
            {
                var y = h - (r + 1) * (seg + segGap) + segGap;
                IBrush b;
                if (r < lit)
                {
                    var f = (double)r / rows;
                    b = f < 0.6 ? low : f < 0.85 ? mid : high;
                }
                else if (r == peakRow && peakRow >= 0)
                    b = peak;
                else
                    b = off;
                c.FillRectangle(b, new Rect(x, y, bw, seg));
            }
        }
    }

    private static void DrawWave(DrawingContext c, SpectrumAnalyzer a, ThemeDef t, double w, double h)
    {
        var bars = a.Bars;
        var n = bars.Length;
        if (n < 2)
            return;
        var step = w / (n - 1);
        Point P(int i, float[] v) => new(i * step, h - v[i] * (h - 2) - 1);

        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(0, h), true);
            g.LineTo(P(0, bars));
            for (var i = 1; i < n; i++)
            {
                var p0 = P(i - 1, bars);
                var p1 = P(i, bars);
                var mx = (p0.X + p1.X) / 2;
                g.CubicBezierTo(new Point(mx, p0.Y), new Point(mx, p1.Y), p1);
            }
            g.LineTo(new Point(w, h));
            g.EndFigure(true);
        }
        c.DrawGeometry(Vertical(t.VisLow, Color.FromArgb(150, t.VisHigh.R, t.VisHigh.G, t.VisHigh.B), h), null, fill);

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(P(0, bars), false);
            for (var i = 1; i < n; i++)
            {
                var p0 = P(i - 1, bars);
                var p1 = P(i, bars);
                var mx = (p0.X + p1.X) / 2;
                g.CubicBezierTo(new Point(mx, p0.Y), new Point(mx, p1.Y), p1);
            }
            g.EndFigure(false);
        }
        c.DrawGeometry(null, new Pen(new SolidColorBrush(t.VisHigh), 1.5), line);

        var peaks = new StreamGeometry();
        using (var g = peaks.Open())
        {
            g.BeginFigure(P(0, a.Peaks), false);
            for (var i = 1; i < n; i++)
                g.LineTo(P(i, a.Peaks));
            g.EndFigure(false);
        }
        c.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(170, t.VisPeak.R, t.VisPeak.G, t.VisPeak.B)), 1), peaks);
    }

    private static void DrawScope(DrawingContext c, SpectrumAnalyzer a, ThemeDef t, double w, double h)
    {
        var samples = a.Samples;
        const int count = 768;
        var start = samples.Length - count;
        var mid = h / 2;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var x = 0; x <= (int)w; x++)
            {
                var i = start + (int)((double)x / w * (count - 1));
                var y = mid - Math.Clamp(samples[i], -1f, 1f) * (h / 2 - 1);
                if (x == 0)
                    g.BeginFigure(new Point(0, y), false);
                else
                    g.LineTo(new Point(x, y));
            }
            g.EndFigure(false);
        }
        c.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(40, t.Lcd.R, t.Lcd.G, t.Lcd.B)), 1), new Point(0, mid), new Point(w, mid));
        c.DrawGeometry(null, new Pen(new SolidColorBrush(t.Vis == VisLook.Segments ? t.VisLow : t.VisHigh), 1.3), geometry);
    }
}
