namespace Fita.Audio;

/// <summary>
/// Analisador de espectro para o visualizador e para o gráfico do EQ: FFT de 2048 pontos com
/// janela de Hann, barras em escala logarítmica e picos que caem com "gravidade".
/// Corre na thread da interface, uma vez por fotograma.
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int Size = 2048;
    private const double FloorDb = -66;
    private const double RangeDb = 60;

    private readonly float[] _window = new float[Size];
    private readonly float[] _re = new float[Size];
    private readonly float[] _im = new float[Size];
    private readonly int[] _reverse = new int[Size];
    private readonly float[] _cos = new float[Size / 2];
    private readonly float[] _sin = new float[Size / 2];
    private readonly float[] _magnitudes = new float[Size / 2];
    private float[] _peakHold = [];
    private float[] _peakSpeed = [];
    private double _edgesRate;
    private double[] _edges = [];

    /// <summary>Últimas amostras tocadas (mono), preenchidas pelo motor antes de <see cref="Update"/>.</summary>
    public float[] Samples { get; } = new float[Size];
    /// <summary>Altura de cada barra, de 0 a 1.</summary>
    public float[] Bars { get; private set; } = new float[32];
    public float[] Peaks { get; private set; } = new float[32];
    /// <summary>Espectro fino (dB normalizado 0..1 por posição logarítmica), para o gráfico do EQ.</summary>
    public float[] Fine { get; } = new float[96];
    private readonly float[] _fineSmooth = new float[96];

    public SpectrumAnalyzer()
    {
        var bits = (int)Math.Log2(Size);
        for (var i = 0; i < Size; i++)
        {
            _window[i] = 0.5f - 0.5f * MathF.Cos(2 * MathF.PI * i / (Size - 1));
            var r = 0;
            for (var b = 0; b < bits; b++)
                r |= ((i >> b) & 1) << (bits - 1 - b);
            _reverse[i] = r;
        }
        for (var i = 0; i < Size / 2; i++)
        {
            _cos[i] = MathF.Cos(2 * MathF.PI * i / Size);
            _sin[i] = -MathF.Sin(2 * MathF.PI * i / Size);
        }
    }

    public int BarCount => Bars.Length;

    public void SetBarCount(int count)
    {
        count = Math.Clamp(count, 4, 256);
        if (count == Bars.Length)
            return;
        Bars = new float[count];
        Peaks = new float[count];
        _peakHold = new float[count];
        _peakSpeed = new float[count];
        _edgesRate = 0;
    }

    /// <param name="active">Falso em pausa ou parado: as barras descem até zero.</param>
    public void Update(double sampleRate, double dt, bool active)
    {
        dt = Math.Clamp(dt, 0, 0.1);
        if (_peakHold.Length != Bars.Length)
        {
            _peakHold = new float[Bars.Length];
            _peakSpeed = new float[Bars.Length];
        }
        if (active)
            Transform(sampleRate);

        var fall = (float)(1.4 * dt);
        for (var b = 0; b < Bars.Length; b++)
        {
            var target = active ? BandLevel(b, sampleRate) : 0f;
            var bar = Bars[b];
            bar = target > bar ? bar + (target - bar) * 0.65f : Math.Max(target, bar - fall);
            Bars[b] = bar;

            if (bar >= Peaks[b])
            {
                Peaks[b] = bar;
                _peakHold[b] = 0.45f;
                _peakSpeed[b] = 0;
            }
            else if (_peakHold[b] > 0)
                _peakHold[b] -= (float)dt;
            else
            {
                _peakSpeed[b] += (float)(2.2 * dt);
                Peaks[b] = Math.Max(bar, Peaks[b] - _peakSpeed[b] * (float)dt);
            }
        }

        for (var i = 0; i < Fine.Length; i++)
        {
            var target = active ? FineLevel(i, sampleRate) : 0f;
            _fineSmooth[i] = target > _fineSmooth[i] ? target : Math.Max(target, _fineSmooth[i] - fall * 0.8f);
            Fine[i] = _fineSmooth[i];
        }
    }

    public bool IsSilent
    {
        get
        {
            foreach (var p in Peaks)
                if (p > 0.002f)
                    return false;
            foreach (var f in Fine)
                if (f > 0.002f)
                    return false;
            return true;
        }
    }

    private void Transform(double sampleRate)
    {
        for (var i = 0; i < Size; i++)
        {
            _re[_reverse[i]] = Samples[i] * _window[i];
            _im[_reverse[i]] = 0;
        }
        for (var len = 2; len <= Size; len <<= 1)
        {
            var half = len >> 1;
            var step = Size / len;
            for (var start = 0; start < Size; start += len)
            {
                for (var k = 0; k < half; k++)
                {
                    var wr = _cos[k * step];
                    var wi = _sin[k * step];
                    var a = start + k;
                    var b = a + half;
                    var tr = _re[b] * wr - _im[b] * wi;
                    var ti = _re[b] * wi + _im[b] * wr;
                    _re[b] = _re[a] - tr;
                    _im[b] = _im[a] - ti;
                    _re[a] += tr;
                    _im[a] += ti;
                }
            }
        }
        // Amplitude de um seno de escala completa = 1 (a janela de Hann tem ganho coerente 0,5).
        const float scale = 4f / Size;
        for (var i = 0; i < Size / 2; i++)
            _magnitudes[i] = MathF.Sqrt(_re[i] * _re[i] + _im[i] * _im[i]) * scale;

        if (_edgesRate != sampleRate || _edges.Length != Bars.Length + 1)
        {
            _edgesRate = sampleRate;
            var top = Math.Min(16000, sampleRate * 0.47);
            _edges = new double[Bars.Length + 1];
            for (var i = 0; i <= Bars.Length; i++)
                _edges[i] = 32 * Math.Pow(top / 32, (double)i / Bars.Length);
        }
    }

    private float BandLevel(int band, double sampleRate) =>
        Level(_edges[band], _edges[band + 1], sampleRate);

    /// <summary>Posição i de <see cref="Fine"/> = 20 Hz a 20 kHz em escala logarítmica.</summary>
    public static double FineFrequency(int i, int count) => 20 * Math.Pow(1000, (i + 0.5) / count);

    private float FineLevel(int i, double sampleRate)
    {
        var lo = 20 * Math.Pow(1000, (double)i / Fine.Length);
        var hi = 20 * Math.Pow(1000, (double)(i + 1) / Fine.Length);
        return hi > sampleRate / 2 ? 0 : Level(lo, hi, sampleRate);
    }

    private float Level(double lo, double hi, double sampleRate)
    {
        var binHz = sampleRate / Size;
        var center = Math.Sqrt(lo * hi);
        float mag;
        var a = (int)Math.Floor(lo / binHz);
        var b = (int)Math.Ceiling(hi / binHz);
        if (b - a <= 1)
        {
            // Banda mais estreita do que uma risca da FFT: interpola-se entre as vizinhas.
            var x = center / binHz;
            var i = Math.Clamp((int)Math.Floor(x), 0, Size / 2 - 2);
            var t = (float)(x - i);
            mag = _magnitudes[i] * (1 - t) + _magnitudes[i + 1] * t;
        }
        else
        {
            mag = 0;
            for (var i = Math.Max(1, a); i < Math.Min(Size / 2, b); i++)
                mag = Math.Max(mag, _magnitudes[i]);
        }
        // +3 dB por oitava (a partir de 1 kHz): a música tem menos energia nos agudos e, sem
        // esta inclinação, a metade direita do visualizador ficava quase sempre vazia.
        var db = 20 * Math.Log10(mag + 1e-9) + 3 * Math.Log2(center / 1000);
        return (float)Math.Clamp((db - FloorDb) / RangeDb, 0, 1);
    }
}
