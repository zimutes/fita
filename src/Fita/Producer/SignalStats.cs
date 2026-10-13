namespace Fita.Producer;

/// <summary>
/// As contas de uma faixa, numa só passagem pelo áudio (float estéreo intercalado):
/// volume K-ponderado (ITU-R BS.1770) em blocos de 100 ms, espectro médio por FFT de 8192 pontos
/// (janela de Hann, a 50 %) com o espectro cruzado entre os canais, picos, desvio DC,
/// saturações, silêncio no início e no fim e a correlação a cada segundo.
/// </summary>
public sealed class SignalStats
{
    private const int FftSize = 8192;
    private const int Hop = FftSize / 2;
    /// <summary>Três ou mais amostras seguidas a este nível contam como saturação.</summary>
    private const float ClipLevel = 0.9995f;
    private const int ClipRun = 3;
    private const int MaxClipTimes = 64;
    /// <summary>Abaixo de −60 dBFS é silêncio (para o início e o fim da faixa).</summary>
    private const float SilenceLevel = 0.001f;
    /// <summary>Um segundo mais baixo do que isto (−50 dBFS) não conta para a correlação.</summary>
    private const double QuietSecond = 1e-5;

    private readonly int _rate;
    private readonly bool _mono;
    private readonly int _blockFrames;

    // K-ponderação (dois biquads, os coeficientes do libebur128 para qualquer frequência).
    private readonly double _pb0, _pb1, _pb2, _pa1, _pa2, _ra1, _ra2;
    private double _pl1, _pl2, _rl1, _rl2, _pr1, _pr2, _rr1, _rr2;
    private double _blockSum;
    private int _blockFill;
    /// <summary>Energia K-ponderada média de cada bloco de 100 ms (soma dos canais).</summary>
    private readonly List<double> _blocks = [];

    private double _secLR, _secLL, _secRR;
    private int _secFill;
    private readonly List<float> _correlation = [];
    private double _totLR, _totLL, _totRR;

    private readonly float[] _histL = new float[FftSize];
    private readonly float[] _histR = new float[FftSize];
    private int _histFill;
    private int _fftFrames;
    private readonly double[] _window = new double[FftSize];
    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];
    private readonly double[] _ell = new double[FftSize / 2 + 1];
    private readonly double[] _err = new double[FftSize / 2 + 1];
    private readonly double[] _elr = new double[FftSize / 2 + 1];

    private double _peak;
    private double _sumL, _sumR;
    private int _runL, _runR;
    private int _clips;
    private long _lastClip = long.MinValue / 2;
    private readonly List<double> _clipTimes = [];
    private long _frame;
    private long _firstLoud = -1, _lastLoud = -1;

    public SignalStats(int sampleRate, bool mono = false)
    {
        _rate = Math.Max(8000, sampleRate);
        _mono = mono;
        _blockFrames = (int)Math.Round(_rate / 10.0);

        var f0 = 1681.974450955533;
        var g = 3.999843853973347;
        var q = 0.7071752369554196;
        var k = Math.Tan(Math.PI * f0 / _rate);
        var vh = Math.Pow(10, g / 20);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1 + k / q + k * k;
        _pb0 = (vh + vb * k / q + k * k) / a0;
        _pb1 = 2 * (k * k - vh) / a0;
        _pb2 = (vh - vb * k / q + k * k) / a0;
        _pa1 = 2 * (k * k - 1) / a0;
        _pa2 = (1 - k / q + k * k) / a0;
        f0 = 38.13547087602444;
        q = 0.5003270373238773;
        k = Math.Tan(Math.PI * f0 / _rate);
        _ra1 = 2 * (k * k - 1) / (1 + k / q + k * k);
        _ra2 = (1 - k / q + k * k) / (1 + k / q + k * k);

        for (var i = 0; i < FftSize; i++)
            _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / FftSize);
    }

    public int SampleRate => _rate;

    /// <summary>Mais áudio: pares L/R intercalados.</summary>
    public void Feed(ReadOnlySpan<float> interleaved)
    {
        var frames = interleaved.Length / 2;
        for (var i = 0; i < frames; i++)
        {
            var l = interleaved[2 * i];
            var r = _mono ? l : interleaved[2 * i + 1];
            var al = MathF.Abs(l);
            var ar = MathF.Abs(r);
            var big = MathF.Max(al, ar);
            if (big > _peak)
                _peak = big;
            _sumL += l;
            _sumR += r;

            _runL = al >= ClipLevel ? _runL + 1 : 0;
            _runR = ar >= ClipLevel ? _runR + 1 : 0;
            if (_runL == ClipRun || _runR == ClipRun)
                Clip();
            if (big > SilenceLevel)
            {
                if (_firstLoud < 0)
                    _firstLoud = _frame;
                _lastLoud = _frame;
            }

            // K-ponderação, canal a canal (forma direta II transposta).
            var x = (double)l;
            var y = _pb0 * x + _pl1;
            _pl1 = _pb1 * x - _pa1 * y + _pl2;
            _pl2 = _pb2 * x - _pa2 * y;
            x = y;
            y = x + _rl1;
            _rl1 = -2 * x - _ra1 * y + _rl2;
            _rl2 = x - _ra2 * y;
            var energy = y * y;
            if (!_mono)
            {
                x = r;
                y = _pb0 * x + _pr1;
                _pr1 = _pb1 * x - _pa1 * y + _pr2;
                _pr2 = _pb2 * x - _pa2 * y;
                x = y;
                y = x + _rr1;
                _rr1 = -2 * x - _ra1 * y + _rr2;
                _rr2 = x - _ra2 * y;
                energy += y * y;
            }
            _blockSum += energy;
            if (++_blockFill == _blockFrames)
            {
                _blocks.Add(_blockSum / _blockFrames);
                _blockSum = 0;
                _blockFill = 0;
            }

            _secLR += (double)l * r;
            _secLL += (double)l * l;
            _secRR += (double)r * r;
            if (++_secFill == _rate)
                EndSecond();

            _histL[_histFill] = l;
            _histR[_histFill] = r;
            if (++_histFill == FftSize)
            {
                Spectrum();
                Array.Copy(_histL, Hop, _histL, 0, FftSize - Hop);
                Array.Copy(_histR, Hop, _histR, 0, FftSize - Hop);
                _histFill = FftSize - Hop;
            }
            _frame++;
        }
    }

    private void Clip()
    {
        // As duas pontas do mesmo pico, ou picos a menos de 50 ms, são uma saturação só.
        if (_frame - _lastClip < _rate / 20)
        {
            _lastClip = _frame;
            return;
        }
        _lastClip = _frame;
        _clips++;
        if (_clipTimes.Count < MaxClipTimes)
            _clipTimes.Add(Math.Round((double)_frame / _rate, 2));
    }

    private void EndSecond()
    {
        var level = (_secLL + _secRR) / (2.0 * _secFill);
        var denominator = Math.Sqrt(_secLL * _secRR);
        _correlation.Add(level < QuietSecond || denominator <= 0 ? float.NaN : (float)Math.Clamp(_secLR / denominator, -1, 1));
        _totLR += _secLR;
        _totLL += _secLL;
        _totRR += _secRR;
        _secLR = _secLL = _secRR = 0;
        _secFill = 0;
    }

    /// <summary>
    /// Uma janela da FFT. Os dois canais vão juntos numa FFT complexa (L na parte real, R na
    /// imaginária) e separam-se depois pela simetria: metade do trabalho.
    /// </summary>
    private void Spectrum()
    {
        for (var i = 0; i < FftSize; i++)
        {
            _re[i] = _histL[i] * _window[i];
            _im[i] = _histR[i] * _window[i];
        }
        Fft.Transform(_re, _im);
        for (var k = 1; k < FftSize / 2; k++)
        {
            double a = _re[k], b = _im[k], c = _re[FftSize - k], d = _im[FftSize - k];
            _ell[k] += ((a + c) * (a + c) + (b - d) * (b - d)) / 4;
            _err[k] += ((b + d) * (b + d) + (a - c) * (a - c)) / 4;
            _elr[k] += ((a + c) * (b + d) - (b - d) * (a - c)) / 4;
        }
        _fftFrames++;
    }

    /// <summary>Acaba as contas e escreve-as na análise (menos o que vem do ffmpeg).</summary>
    public void Finish(TrackAnalysis a)
    {
        if (_secFill > _rate / 4)
            EndSecond();
        else
        {
            _totLR += _secLR;
            _totLL += _secLL;
            _totRR += _secRR;
        }
        if (_fftFrames == 0 && _histFill > 0)
        {
            // Faixa mais curta do que uma janela: completa-se com silêncio.
            Array.Clear(_histL, _histFill, FftSize - _histFill);
            Array.Clear(_histR, _histFill, FftSize - _histFill);
            Spectrum();
        }

        a.SampleRate = _rate;
        a.Duration = (double)_frame / _rate;
        a.SamplePeak = Db(_peak);
        a.DcOffset = _frame > 0 ? Db(Math.Max(Math.Abs(_sumL), Math.Abs(_sumR)) / _frame) : double.NaN;
        a.Clips = _clips;
        a.ClipTimes = [.. _clipTimes];
        if (_firstLoud < 0)
        {
            a.SilenceStart = a.Duration;
            a.SilenceEnd = 0;
        }
        else
        {
            a.SilenceStart = Math.Round((double)_firstLoud / _rate, 2);
            a.SilenceEnd = Math.Round((double)(_frame - 1 - _lastLoud) / _rate, 2);
        }

        Loudness(a);

        var corr = Math.Sqrt(_totLL * _totRR);
        a.Correlation = corr > 0 ? Math.Clamp(_totLR / corr, -1, 1) : 1;
        a.CorrelationTimeline = [.. _correlation];

        Tonal(a);
    }

    private void Loudness(TrackAnalysis a)
    {
        var n = _blocks.Count;
        // Momentâneo: 400 ms (4 blocos), de 100 em 100 ms: são os blocos do integrado (75 % de sobreposição).
        var momentary = new List<double>();
        double sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += _blocks[i];
            if (i >= 4)
                sum -= _blocks[i - 4];
            if (i >= 3)
                momentary.Add(Math.Max(0, sum / 4));
        }
        a.MomentaryMax = momentary.Count > 0 ? Lufs(momentary.Max()) : double.NaN;

        // Curto: 3 s (30 blocos). A cada segundo, para a linha do tempo; o máximo de 100 em 100 ms.
        var shortTerm = new List<float>();
        var stMax = 0.0;
        sum = 0;
        for (var i = 0; i < n; i++)
        {
            sum += _blocks[i];
            if (i >= 30)
                sum -= _blocks[i - 30];
            var count = Math.Min(i + 1, 30);
            var z = Math.Max(0, sum / count);
            if (i >= 29 && z > stMax)
                stMax = z;
            if (i % 10 == 9)
                shortTerm.Add(z > 1e-10 ? (float)Lufs(z) : float.NaN);
        }
        if (n < 30 && n > 0)
            stMax = Math.Max(0, sum / n);
        a.ShortTermMax = stMax > 0 ? Lufs(stMax) : double.NaN;
        a.ShortTerm = [.. shortTerm];

        // Integrado (BS.1770-4): porta absoluta a −70 LUFS e relativa a −10 LU.
        var gated = momentary.Where(z => Lufs(z) > -70).ToList();
        if (gated.Count == 0)
        {
            a.OwnIntegrated = double.NaN;
            return;
        }
        var relative = Lufs(gated.Average()) - 10;
        var kept = gated.Where(z => Lufs(z) > relative).ToList();
        a.OwnIntegrated = kept.Count > 0 ? Math.Round(Lufs(kept.Average()), 2) : double.NaN;
    }

    private void Tonal(TrackAnalysis a)
    {
        var binHz = (double)_rate / FftSize;
        var top = Math.Min(20000, _rate / 2.0 - binHz);
        (double L, double R, double LR) Sum(double from, double to)
        {
            double l = 0, r = 0, lr = 0;
            var k0 = Math.Max(1, (int)Math.Ceiling(from / binHz));
            var k1 = Math.Min(FftSize / 2 - 1, (int)Math.Floor(Math.Min(to, top) / binHz));
            for (var k = k0; k <= k1; k++)
            {
                l += _ell[k];
                r += _err[k];
                lr += _elr[k];
            }
            return (l, r, lr);
        }
        static double Mid((double L, double R, double LR) e) => (e.L + e.R + 2 * e.LR) / 4;
        static double Side((double L, double R, double LR) e) => (e.L + e.R - 2 * e.LR) / 4;

        var total = Mid(Sum(20, 20000));
        if (total <= 0)
            total = 1e-30;
        var bands = Bands.Count;
        a.BandLevels = new double[bands];
        a.BandCorrelation = new double[bands];
        a.MonoLoss = new double[bands];
        a.BandWidth = new double[bands];
        for (var b = 0; b < bands; b++)
        {
            var (lo, hi) = Bands.Edges[b];
            var e = Sum(lo, hi);
            a.BandLevels[b] = Math.Round(RelDb(Mid(e), total), 2);
            var c = Math.Sqrt(e.L * e.R);
            a.BandCorrelation[b] = c > 1e-30 ? Math.Round(Math.Clamp(e.LR / c, -1, 1), 3) : 1;
            var both = e.L + e.R;
            a.MonoLoss[b] = both > 1e-30 ? Math.Round(Math.Max(-60, 10 * Math.Log10(Math.Max(1e-6, (both + 2 * e.LR) / (2 * both)))), 2) : 0;
            var mid = Mid(e);
            a.BandWidth[b] = mid > 1e-30 ? Math.Round(Math.Clamp(10 * Math.Log10(Math.Max(1e-12, Side(e)) / mid), -60, 60), 2) : -60;
        }
        a.Curve = new double[Bands.Thirds.Length];
        var step = Math.Pow(2, 1 / 6.0);
        for (var i = 0; i < Bands.Thirds.Length; i++)
        {
            var c = Bands.Thirds[i];
            a.Curve[i] = c / step > top ? double.NaN : Math.Round(RelDb(Mid(Sum(c / step, c * step)), total), 2);
        }
    }

    private static double RelDb(double e, double total) => e <= 0 ? -120 : Math.Max(-120, 10 * Math.Log10(e / total));

    public static double Lufs(double meanSquare) => -0.691 + 10 * Math.Log10(Math.Max(1e-30, meanSquare));

    private static double Db(double linear) => linear <= 0 ? -120 : Math.Max(-120, Math.Round(20 * Math.Log10(linear), 2));
}

/// <summary>FFT complexa de base 2, no lugar.</summary>
internal static class Fft
{
    public static void Transform(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            double wr = Math.Cos(angle), wi = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (var k = 0; k < len / 2; k++)
                {
                    var a = i + k;
                    var b = a + len / 2;
                    var tr = re[b] * cr - im[b] * ci;
                    var ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr;
                    im[b] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                    var next = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = next;
                }
            }
        }
    }
}
