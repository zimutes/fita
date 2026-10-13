namespace Fita.Audio;

/// <summary>Como se ouve, no modo producer: os dois canais, a soma, a diferença, ou um canal só.</summary>
public enum MonitorMode { Stereo, Mono, Side, Left, Right }

/// <summary>Um valor do medidor ao vivo (do que se ouve, antes do volume).</summary>
public readonly record struct MeterReading(double Momentary, double ShortTerm, double Correlation);

/// <summary>
/// O andar do modo producer na thread de saída, logo depois do EQ: o modo de escuta, o solo de
/// uma banda (filtros Linkwitz-Riley de 4.ª ordem nas bordas das bandas do balanço tonal) e o
/// medidor (volume K-ponderado de 400 ms e 3 s, correlação nos últimos ~300 ms).
/// </summary>
internal sealed class MonitorProcessor
{
    private volatile int _mode;
    private volatile int _solo = -1;
    private int _soloBuilt = -2;
    private int _rateBuilt;
    // Até 4 biquads (2 passa-alto + 2 passa-baixo) por canal, forma direta II transposta.
    private Biquad[] _filters = [];
    private readonly double[] _z = new double[16];

    public volatile bool MeterOn;
    private double _kpl1, _kpl2, _krl1, _krl2, _kpr1, _kpr2, _krr1, _krr2;
    private double _pb0, _pb1, _pb2, _pa1, _pa2, _ra1, _ra2;
    private int _meterRate;
    private readonly double[] _blocks = new double[30];
    private int _blockIndex, _blockCount, _blockFill, _blockFrames;
    private double _blockSum;
    private double _cLR, _cLL, _cRR;
    private MeterReading _reading = new(double.NaN, double.NaN, double.NaN);
    private readonly object _readingLock = new();

    public MonitorMode Mode { get => (MonitorMode)_mode; set => _mode = (int)value; }

    /// <summary>A banda (0 a 5, as de <see cref="Producer.Bands"/>) que se ouve sozinha, ou −1.</summary>
    public int Solo { get => _solo; set => _solo = value is >= 0 and < 6 ? value : -1; }

    public MeterReading Reading
    {
        get
        {
            lock (_readingLock)
                return _reading;
        }
    }

    public void Process(float[] block, int frames, int rate)
    {
        var mode = (MonitorMode)_mode;
        if (mode != MonitorMode.Stereo)
        {
            for (var i = 0; i < frames; i++)
            {
                float l = block[2 * i], r = block[2 * i + 1];
                (block[2 * i], block[2 * i + 1]) = mode switch
                {
                    MonitorMode.Mono => ((l + r) * 0.5f, (l + r) * 0.5f),
                    MonitorMode.Side => ((l - r) * 0.5f, (r - l) * 0.5f),
                    MonitorMode.Left => (l, l),
                    _ => (r, r),
                };
            }
        }
        SoloBand(block, frames, rate);
        if (MeterOn)
            Meter(block, frames, rate);
    }

    private void SoloBand(float[] block, int frames, int rate)
    {
        var solo = _solo;
        if (solo != _soloBuilt || rate != _rateBuilt)
        {
            _soloBuilt = solo;
            _rateBuilt = rate;
            Array.Clear(_z);
            var list = new List<Biquad>();
            if (solo >= 0)
            {
                var (lo, hi) = Producer.Bands.Edges[solo];
                if (lo > 20)
                    for (var k = 0; k < 2; k++)
                        list.Add(Biquad.For(new EqBand { Frequency = lo, Q = 0.7071, Type = EqBandType.LowCut }, rate));
                if (hi < 20000)
                    for (var k = 0; k < 2; k++)
                        list.Add(Biquad.For(new EqBand { Frequency = hi, Q = 0.7071, Type = EqBandType.HighCut }, rate));
                if (lo <= 20)
                    // Sub: corta também abaixo de 20 Hz, como as outras bandas cortam nas bordas.
                    list.Add(Biquad.For(new EqBand { Frequency = 20, Q = 0.7071, Type = EqBandType.LowCut }, rate));
            }
            _filters = [.. list];
        }
        var filters = _filters;
        for (var k = 0; k < filters.Length; k++)
        {
            var f = filters[k];
            var o = k * 4;
            double zl1 = _z[o], zl2 = _z[o + 1], zr1 = _z[o + 2], zr2 = _z[o + 3];
            for (var i = 0; i < frames; i++)
            {
                double x = block[2 * i];
                var y = f.B0 * x + zl1;
                zl1 = f.B1 * x - f.A1 * y + zl2;
                zl2 = f.B2 * x - f.A2 * y;
                block[2 * i] = (float)y;
                x = block[2 * i + 1];
                y = f.B0 * x + zr1;
                zr1 = f.B1 * x - f.A1 * y + zr2;
                zr2 = f.B2 * x - f.A2 * y;
                block[2 * i + 1] = (float)y;
            }
            _z[o] = Math.Abs(zl1) < 1e-25 ? 0 : zl1;
            _z[o + 1] = Math.Abs(zl2) < 1e-25 ? 0 : zl2;
            _z[o + 2] = Math.Abs(zr1) < 1e-25 ? 0 : zr1;
            _z[o + 3] = Math.Abs(zr2) < 1e-25 ? 0 : zr2;
        }
    }

    private void Meter(float[] block, int frames, int rate)
    {
        if (rate != _meterRate)
        {
            _meterRate = rate;
            // Os mesmos coeficientes da análise (BS.1770, à maneira do libebur128).
            var k = Math.Tan(Math.PI * 1681.974450955533 / rate);
            var q = 0.7071752369554196;
            var vh = Math.Pow(10, 3.999843853973347 / 20);
            var vb = Math.Pow(vh, 0.4996667741545416);
            var a0 = 1 + k / q + k * k;
            _pb0 = (vh + vb * k / q + k * k) / a0;
            _pb1 = 2 * (k * k - vh) / a0;
            _pb2 = (vh - vb * k / q + k * k) / a0;
            _pa1 = 2 * (k * k - 1) / a0;
            _pa2 = (1 - k / q + k * k) / a0;
            k = Math.Tan(Math.PI * 38.13547087602444 / rate);
            q = 0.5003270373238773;
            _ra1 = 2 * (k * k - 1) / (1 + k / q + k * k);
            _ra2 = (1 - k / q + k * k) / (1 + k / q + k * k);
            _blockFrames = Math.Max(1, rate / 10);
            _blockCount = _blockIndex = _blockFill = 0;
            _blockSum = 0;
            _kpl1 = _kpl2 = _krl1 = _krl2 = _kpr1 = _kpr2 = _krr1 = _krr2 = 0;
        }
        var decay = Math.Exp(-1.0 / (0.3 * rate));
        for (var i = 0; i < frames; i++)
        {
            double l = block[2 * i], r = block[2 * i + 1];
            _cLR = _cLR * decay + l * r;
            _cLL = _cLL * decay + l * l;
            _cRR = _cRR * decay + r * r;

            var y = _pb0 * l + _kpl1;
            _kpl1 = _pb1 * l - _pa1 * y + _kpl2;
            _kpl2 = _pb2 * l - _pa2 * y;
            var x = y;
            y = x + _krl1;
            _krl1 = -2 * x - _ra1 * y + _krl2;
            _krl2 = x - _ra2 * y;
            var e = y * y;
            y = _pb0 * r + _kpr1;
            _kpr1 = _pb1 * r - _pa1 * y + _kpr2;
            _kpr2 = _pb2 * r - _pa2 * y;
            x = y;
            y = x + _krr1;
            _krr1 = -2 * x - _ra1 * y + _krr2;
            _krr2 = x - _ra2 * y;
            _blockSum += e + y * y;
            if (++_blockFill < _blockFrames)
                continue;
            _blocks[_blockIndex] = _blockSum / _blockFrames;
            _blockIndex = (_blockIndex + 1) % _blocks.Length;
            _blockCount = Math.Min(_blockCount + 1, _blocks.Length);
            _blockSum = 0;
            _blockFill = 0;

            double m = 0, s = 0;
            for (var j = 0; j < _blockCount; j++)
            {
                var v = _blocks[(_blockIndex - 1 - j + _blocks.Length) % _blocks.Length];
                if (j < 4)
                    m += v;
                s += v;
            }
            var momentary = Lufs(m / Math.Min(4, _blockCount));
            var shortTerm = Lufs(s / _blockCount);
            var den = Math.Sqrt(_cLL * _cRR);
            var corr = den > 1e-9 ? Math.Clamp(_cLR / den, -1, 1) : double.NaN;
            lock (_readingLock)
                _reading = new MeterReading(momentary, shortTerm, corr);
        }
    }

    /// <summary>Depois de parar ou de saltar: o medidor recomeça do zero.</summary>
    public void ResetMeter()
    {
        _meterRate = 0;
        _cLR = _cLL = _cRR = 0;
        lock (_readingLock)
            _reading = new MeterReading(double.NaN, double.NaN, double.NaN);
    }

    private static double Lufs(double z) => z > 1e-12 ? -0.691 + 10 * Math.Log10(z) : double.NaN;
}
