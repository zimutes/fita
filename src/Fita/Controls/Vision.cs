namespace Fita.Controls;

/// <summary>
/// Visualização no espírito do MilkDrop, feita de raiz e sem bibliotecas: cada imagem é a
/// anterior torcida (zoom, rotação, remoinho, ondulação) e a desvanecer, com a onda da música
/// desenhada por cima. Os "presets" são só números; de tempos a tempos passa-se para outro,
/// devagar. As batidas (energia dos graves) dão um empurrão ao zoom e ao brilho. Desenha numa
/// grelha pequena (a janela estica-a): a CPU chega bem.
/// </summary>
public sealed class Vision
{
    public sealed record Preset(
        string Name, double Zoom, double Rotation, double Swirl, double Warp, double WarpFrequency,
        double Decay, WaveShape Shape, double WaveScale, double HueSpeed, double Drift);

    public enum WaveShape { Line, Circle, Spiral, Mirror }

    public static readonly Preset[] Presets =
    [
        new("Túnel", 1.035, 0.004, 0.0, 0.006, 6, 0.955, WaveShape.Circle, 0.35, 0.05, 0.02),
        new("Remoinho", 1.01, 0.012, 0.035, 0.004, 4, 0.965, WaveShape.Spiral, 0.30, 0.08, 0.0),
        new("Maré", 0.995, -0.003, 0.0, 0.018, 3, 0.97, WaveShape.Line, 0.45, 0.03, 0.04),
        new("Espelho", 1.02, 0.0, -0.02, 0.01, 8, 0.95, WaveShape.Mirror, 0.40, 0.06, 0.0),
        new("Fumo", 1.006, 0.002, 0.012, 0.025, 2, 0.978, WaveShape.Line, 0.35, 0.02, 0.03),
        new("Pulso", 1.05, -0.006, 0.0, 0.004, 10, 0.93, WaveShape.Circle, 0.50, 0.10, 0.0),
        new("Galáxia", 1.015, 0.008, 0.05, 0.002, 5, 0.972, WaveShape.Spiral, 0.25, 0.04, 0.01),
        new("Cassete", 1.0, 0.0, 0.0, 0.012, 12, 0.96, WaveShape.Mirror, 0.55, 0.07, 0.05),
    ];

    private int _w, _h;
    private float[] _r = [], _g = [], _b = [];
    private float[] _r2 = [], _g2 = [], _b2 = [];
    private Preset _from = Presets[0], _to = Presets[0];
    private double _blend = 1;
    private double _sincePreset;
    private double _bassAverage = 0.01, _beat;
    private double _hue;
    private readonly Random _random = new();

    /// <summary>Segundos em cada preset antes de passar ao seguinte.</summary>
    public double PresetSeconds { get; set; } = 25;
    public string PresetName => _to.Name;
    /// <summary>Cor de partida (o tom do tema), de 0 a 1.</summary>
    public double BaseHue { get; set; }

    public int Width => _w;
    public int Height => _h;

    public void Resize(int w, int h)
    {
        w = Math.Max(16, w);
        h = Math.Max(16, h);
        if (w == _w && h == _h)
            return;
        _w = w;
        _h = h;
        var n = w * h;
        _r = new float[n]; _g = new float[n]; _b = new float[n];
        _r2 = new float[n]; _g2 = new float[n]; _b2 = new float[n];
    }

    /// <summary>Passa a outro preset (o seguinte ao acaso), a fundir durante uns segundos.</summary>
    public void NextPreset()
    {
        var next = _to;
        while (next == _to && Presets.Length > 1)
            next = Presets[_random.Next(Presets.Length)];
        _from = Current();
        _to = next;
        _blend = 0;
        _sincePreset = 0;
    }

    private Preset Current()
    {
        if (_blend >= 1)
            return _to;
        double L(double a, double b) => a + (b - a) * _blend;
        return new Preset(_to.Name, L(_from.Zoom, _to.Zoom), L(_from.Rotation, _to.Rotation), L(_from.Swirl, _to.Swirl),
            L(_from.Warp, _to.Warp), L(_from.WarpFrequency, _to.WarpFrequency), L(_from.Decay, _to.Decay),
            _blend < 0.5 ? _from.Shape : _to.Shape, L(_from.WaveScale, _to.WaveScale), L(_from.HueSpeed, _to.HueSpeed),
            L(_from.Drift, _to.Drift));
    }

    /// <summary>
    /// Uma imagem. <paramref name="samples"/>: as últimas amostras tocadas (mono, as mais
    /// recentes no fim). Escreve BGRA (pré-multiplicado, opaco) em <paramref name="bgra"/>.
    /// </summary>
    public void Render(float[] samples, double dt, double time, Span<byte> bgra)
    {
        dt = Math.Clamp(dt, 0.001, 0.1);
        var frames = dt * 60;
        _sincePreset += dt;
        if (_sincePreset > PresetSeconds)
            NextPreset();
        if (_blend < 1)
            _blend = Math.Min(1, _blend + dt / 3);
        var p = Current();

        // Batida: energia dos graves (média de blocos curtos, filtrada) contra a média longa.
        var bass = Bass(samples);
        _bassAverage += (bass - _bassAverage) * Math.Min(1, dt * 1.5);
        if (bass > _bassAverage * 1.45 && bass > 0.004)
            _beat = 1;
        _beat = Math.Max(0, _beat - dt * 3);
        _hue = (_hue + p.HueSpeed * dt + _beat * 0.01) % 1;

        Warp(p, frames, time);
        DrawWave(p, samples, time);
        ToBgra(bgra);
    }

    private static double Bass(float[] samples)
    {
        // Um passa-baixo de um polo (~150 Hz a 48 kHz) e a energia do que fica, nas últimas ~40 ms.
        var n = Math.Min(samples.Length, 2048);
        double y = 0, sum = 0;
        const double a = 0.02;
        for (var i = samples.Length - n; i < samples.Length; i++)
        {
            y += (samples[i] - y) * a;
            sum += y * y;
        }
        return sum / n;
    }

    /// <summary>A imagem anterior, torcida e a desvanecer (amostragem bilinear, por linhas em paralelo).</summary>
    private void Warp(Preset p, double frames, double time)
    {
        var w = _w;
        var h = _h;
        var aspect = w / (double)h;
        var zoom = Math.Pow(p.Zoom + _beat * 0.025, frames);
        var rotation = p.Rotation * frames;
        var swirl = p.Swirl * frames;
        // O desvanecer do preset, um pouco mais forte: sem isto a luz acumula e lava a imagem.
        var decay = (float)Math.Pow(p.Decay * 0.985, frames);
        var warp = p.Warp;
        var freq = p.WarpFrequency;
        var cx = 0.5 + p.Drift * Math.Sin(time * 0.31);
        var cy = 0.5 + p.Drift * Math.Cos(time * 0.23);
        var (r, g, b) = (_r, _g, _b);
        var (r2, g2, b2) = (_r2, _g2, _b2);
        Parallel.For(0, h, y =>
        {
            var v0 = y / (double)h - cy;
            for (var x = 0; x < w; x++)
            {
                var u0 = (x / (double)w - cx) * aspect;
                var radius = Math.Sqrt(u0 * u0 + v0 * v0);
                var angle = rotation + swirl * radius;
                var cos = Math.Cos(angle);
                var sin = Math.Sin(angle);
                var u = (u0 * cos - v0 * sin) / zoom;
                var v = (u0 * sin + v0 * cos) / zoom;
                u += warp * Math.Sin(v * freq + time * 1.1);
                v += warp * Math.Cos(u * freq + time * 1.3);
                var sx = (u / aspect + cx) * w - 0.5;
                var sy = (v + cy) * h - 0.5;
                var i = y * w + x;
                if (sx < 0 || sy < 0 || sx >= w - 1 || sy >= h - 1)
                {
                    r2[i] = g2[i] = b2[i] = 0;
                    continue;
                }
                var x0 = (int)sx;
                var y0 = (int)sy;
                var fx = (float)(sx - x0);
                var fy = (float)(sy - y0);
                var j = y0 * w + x0;
                float Sample(float[] c) =>
                    (c[j] * (1 - fx) + c[j + 1] * fx) * (1 - fy) + (c[j + w] * (1 - fx) + c[j + w + 1] * fx) * fy;
                r2[i] = Sample(r) * decay;
                g2[i] = Sample(g) * decay;
                b2[i] = Sample(b) * decay;
            }
        });
        (_r, _r2) = (_r2, _r);
        (_g, _g2) = (_g2, _g);
        (_b, _b2) = (_b2, _b);
    }

    private void DrawWave(Preset p, float[] samples, double time)
    {
        var (cr, cg, cb) = Hsv(BaseHue + _hue, 0.65, 1);
        var bright = 0.22f + 0.35f * (float)_beat;
        var count = 256;
        var step = Math.Max(1, Math.Min(samples.Length, 2048) / count);
        var start = samples.Length - count * step;
        if (start < 0)
            return;
        var aspect = _w / (double)_h;
        double prevX = 0, prevY = 0;
        for (var k = 0; k < count; k++)
        {
            var s = samples[start + k * step] * p.WaveScale * 2;
            var t = k / (double)(count - 1);
            double x, y;
            switch (p.Shape)
            {
                case WaveShape.Circle:
                {
                    var angle = t * Math.PI * 2 + time * 0.2;
                    var radius = 0.18 + s * 0.5 + _beat * 0.03;
                    x = 0.5 + Math.Cos(angle) * radius / aspect;
                    y = 0.5 + Math.Sin(angle) * radius;
                    break;
                }
                case WaveShape.Spiral:
                {
                    var angle = t * Math.PI * 6 + time * 0.5;
                    var radius = 0.05 + t * 0.35 + s * 0.25;
                    x = 0.5 + Math.Cos(angle) * radius / aspect;
                    y = 0.5 + Math.Sin(angle) * radius;
                    break;
                }
                case WaveShape.Mirror:
                    x = t;
                    y = 0.5 + s * (k % 2 == 0 ? 1 : -1) * 0.6;
                    break;
                default:
                    x = t;
                    y = 0.5 + s;
                    break;
            }
            if (k > 0)
                Line(prevX * _w, prevY * _h, x * _w, y * _h, cr * bright, cg * bright, cb * bright);
            prevX = x;
            prevY = y;
        }
    }

    /// <summary>Uma linha somada à imagem (o brilho acumula, como num ecrã de fósforo).</summary>
    private void Line(double x0, double y0, double x1, double y1, float r, float g, float b)
    {
        var steps = (int)Math.Max(1, Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0))));
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var x = (int)(x0 + (x1 - x0) * t);
            var y = (int)(y0 + (y1 - y0) * t);
            if (x < 0 || y < 0 || x >= _w || y >= _h)
                continue;
            var j = y * _w + x;
            _r[j] = Math.Min(4, _r[j] + r);
            _g[j] = Math.Min(4, _g[j] + g);
            _b[j] = Math.Min(4, _b[j] + b);
        }
    }

    private void ToBgra(Span<byte> bgra)
    {
        var n = Math.Min(_w * _h, bgra.Length / 4);
        for (var i = 0; i < n; i++)
        {
            bgra[i * 4] = Byte(_b[i]);
            bgra[i * 4 + 1] = Byte(_g[i]);
            bgra[i * 4 + 2] = Byte(_r[i]);
            bgra[i * 4 + 3] = 255;
        }

        // Curva suave: o que passa de 1 satura devagar em vez de cortar.
        static byte Byte(float v) => (byte)(255 * (1 - Math.Exp(-1.15 * v)));
    }

    public static (float R, float G, float B) Hsv(double h, double s, double v)
    {
        h = (h % 1 + 1) % 1 * 6;
        var i = (int)h;
        var f = h - i;
        double p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return ((float)r, (float)g, (float)b);
    }
}
