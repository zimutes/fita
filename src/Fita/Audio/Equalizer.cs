using System.Numerics;

namespace Fita.Audio;

public enum EqBandType { Peak, LowShelf, HighShelf, LowCut, HighCut }

/// <summary>Uma banda do equalizador. Nos cortes (LowCut/HighCut) o ganho não conta.</summary>
public sealed class EqBand
{
    public double Frequency { get; set; }
    public double Gain { get; set; }
    public double Q { get; set; } = EqSettings.DefaultQ;
    public EqBandType Type { get; set; }

    public EqBand Clone() => (EqBand)MemberwiseClone();

    public bool UsesGain => Type is EqBandType.Peak or EqBandType.LowShelf or EqBandType.HighShelf;
}

/// <summary>Estado do equalizador tal como se grava nas definições.</summary>
public sealed class EqSettings
{
    public const double DefaultQ = 1.41;
    public const double MaxGain = 12;
    public const double MinFrequency = 20;
    public const double MaxFrequency = 20000;

    /// <summary>As 10 bandas ISO de oitava, de 31 Hz a 16 kHz.</summary>
    public static readonly double[] IsoFrequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public bool Enabled { get; set; } = true;
    public double Preamp { get; set; }
    /// <summary>Baixa o pré-amplificador o suficiente para os reforços não saturarem.</summary>
    public bool AutoPreamp { get; set; } = true;
    public string Preset { get; set; } = "Flat";
    public List<EqBand> Bands { get; set; } = DefaultBands();

    public static List<EqBand> DefaultBands() =>
        IsoFrequencies.Select(f => new EqBand { Frequency = f, Q = DefaultQ, Type = EqBandType.Peak }).ToList();

    public EqSettings Clone()
    {
        var copy = (EqSettings)MemberwiseClone();
        copy.Bands = Bands.Select(b => b.Clone()).ToList();
        return copy;
    }

    /// <summary>Corrige o que vier estragado de um ficheiro de definições.</summary>
    public void Sanitize()
    {
        if (Bands is not { Count: > 0 and <= EqMath.MaxBands })
            Bands = DefaultBands();
        foreach (var b in Bands)
        {
            b.Frequency = Math.Clamp(double.IsFinite(b.Frequency) ? b.Frequency : 1000, MinFrequency, MaxFrequency);
            b.Gain = Math.Clamp(double.IsFinite(b.Gain) ? b.Gain : 0, -MaxGain, MaxGain);
            b.Q = Math.Clamp(double.IsFinite(b.Q) ? b.Q : DefaultQ, 0.2, 12);
            if (!Enum.IsDefined(b.Type))
                b.Type = EqBandType.Peak;
        }
        Preamp = Math.Clamp(double.IsFinite(Preamp) ? Preamp : 0, -MaxGain, MaxGain);
        Preset ??= "";
    }
}

/// <summary>Uma predefinição: um ganho por banda ISO.</summary>
public sealed record EqPreset(string Name, double[] Gains, bool BuiltIn = true);

public static class EqPresets
{
    public static readonly IReadOnlyList<EqPreset> BuiltIn =
    [
        new("Flat", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        new("Rock", [5, 4, 2, -1, -2, -1, 2, 4, 5, 5]),
        new("Pop", [-1, 1, 3, 4, 3, 0, -1, -1, 1, 2]),
        new("Jazz", [3, 2, 1, 2, -1, -1, 0, 1, 2, 3]),
        new("Classical", [0, 0, 0, 0, 0, 0, -2, -3, -3, -5]),
        new("Electronic", [6, 5, 2, 0, -1, 1, 2, 3, 4, 4]),
        new("Hip-hop", [5, 5, 3, 1, -1, -1, 1, 0, 2, 3]),
        new("Vocal", [-2, -3, -2, 1, 4, 4, 3, 1, 0, -1]),
        new("BassBoost", [7, 6, 4, 2, 0, 0, 0, 0, 0, 0]),
        new("TrebleBoost", [0, 0, 0, 0, 0, 1, 3, 5, 6, 7]),
        new("Loudness", [5, 4, 0, -1, -2, -1, 0, 0, 3, 5]),
        new("LateNight", [-3, -2, 0, 1, 2, 2, 1, 0, -2, -4]),
    ];

    /// <summary>Aplica uma predefinição: volta às 10 bandas ISO (frequência, Q e tipo de origem).</summary>
    public static void Apply(EqSettings settings, EqPreset preset)
    {
        settings.Bands = EqSettings.DefaultBands();
        for (var i = 0; i < settings.Bands.Count && i < preset.Gains.Length; i++)
            settings.Bands[i].Gain = preset.Gains[i];
        settings.Preset = preset.Name;
    }
}

/// <summary>Coeficientes de um biquad (RBJ Audio EQ Cookbook), já normalizados por a0.</summary>
public readonly record struct Biquad(double B0, double B1, double B2, double A1, double A2)
{
    public static readonly Biquad Identity = new(1, 0, 0, 0, 0);

    public static Biquad For(EqBand band, double sampleRate)
    {
        var f = Math.Clamp(band.Frequency, 10, sampleRate * 0.49);
        var q = Math.Max(0.05, band.Q);
        var w0 = 2 * Math.PI * f / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var a = Math.Pow(10, band.Gain / 40);
        double b0, b1, b2, a0, a1, a2;

        switch (band.Type)
        {
            case EqBandType.Peak:
                if (Math.Abs(band.Gain) < 0.01)
                    return Identity;
                b0 = 1 + alpha * a; b1 = -2 * cos; b2 = 1 - alpha * a;
                a0 = 1 + alpha / a; a1 = -2 * cos; a2 = 1 - alpha / a;
                break;
            case EqBandType.LowShelf:
            {
                if (Math.Abs(band.Gain) < 0.01)
                    return Identity;
                var s = 2 * Math.Sqrt(a) * alpha;
                b0 = a * (a + 1 - (a - 1) * cos + s);
                b1 = 2 * a * (a - 1 - (a + 1) * cos);
                b2 = a * (a + 1 - (a - 1) * cos - s);
                a0 = a + 1 + (a - 1) * cos + s;
                a1 = -2 * (a - 1 + (a + 1) * cos);
                a2 = a + 1 + (a - 1) * cos - s;
                break;
            }
            case EqBandType.HighShelf:
            {
                if (Math.Abs(band.Gain) < 0.01)
                    return Identity;
                var s = 2 * Math.Sqrt(a) * alpha;
                b0 = a * (a + 1 + (a - 1) * cos + s);
                b1 = -2 * a * (a - 1 + (a + 1) * cos);
                b2 = a * (a + 1 + (a - 1) * cos - s);
                a0 = a + 1 - (a - 1) * cos + s;
                a1 = 2 * (a - 1 - (a + 1) * cos);
                a2 = a + 1 - (a - 1) * cos - s;
                break;
            }
            case EqBandType.LowCut:
                b0 = (1 + cos) / 2; b1 = -(1 + cos); b2 = (1 + cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
            default: // HighCut
                b0 = (1 - cos) / 2; b1 = 1 - cos; b2 = (1 - cos) / 2;
                a0 = 1 + alpha; a1 = -2 * cos; a2 = 1 - alpha;
                break;
        }
        return new Biquad(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    /// <summary>Ganho (dB) deste filtro numa frequência.</summary>
    public double MagnitudeDb(double frequency, double sampleRate)
    {
        var w = 2 * Math.PI * frequency / sampleRate;
        var z1 = Complex.FromPolarCoordinates(1, -w);
        var z2 = z1 * z1;
        var h = (B0 + B1 * z1 + B2 * z2) / (1 + A1 * z1 + A2 * z2);
        return 20 * Math.Log10(Math.Max(1e-9, h.Magnitude));
    }
}

public static class EqMath
{
    public const int MaxBands = 16;

    /// <summary>Resposta total das bandas (sem pré-amplificador), em dB.</summary>
    public static double ResponseDb(IReadOnlyList<Biquad> filters, double frequency, double sampleRate)
    {
        var sum = 0.0;
        foreach (var f in filters)
            if (f != Biquad.Identity)
                sum += f.MagnitudeDb(frequency, sampleRate);
        return sum;
    }

    public static Biquad[] Filters(EqSettings settings, double sampleRate) =>
        settings.Bands.Take(MaxBands).Select(b => Biquad.For(b, sampleRate)).ToArray();

    /// <summary>O maior reforço da curva entre 20 Hz e 20 kHz (0 se só houver cortes).</summary>
    public static double MaxBoostDb(IReadOnlyList<Biquad> filters, double sampleRate)
    {
        var max = 0.0;
        var top = Math.Min(20000, sampleRate * 0.48);
        for (var i = 0; i <= 240; i++)
        {
            var f = 20 * Math.Pow(top / 20, i / 240.0);
            max = Math.Max(max, ResponseDb(filters, f, sampleRate));
        }
        return max;
    }

    /// <summary>Correção automática do pré-amplificador (≤ 0 dB).</summary>
    public static double AutoPreampDb(EqSettings settings, double sampleRate) =>
        settings.AutoPreamp ? -MaxBoostDb(Filters(settings, sampleRate), sampleRate) : 0;
}

/// <summary>
/// Processa o equalizador na thread de saída. A configuração chega por troca atómica de um
/// estado imutável, por isso mexer num controlo nunca bloqueia o som.
/// </summary>
internal sealed class EqProcessor
{
    private sealed record State(Biquad[] Filters, float Preamp, bool Enabled);

    private volatile State _state = new([], 1f, false);
    // Memória dos filtros: 2 valores por canal por banda (forma direta II transposta).
    private readonly double[] _z = new double[EqMath.MaxBands * 4];
    private int _lastCount;

    public void Configure(EqSettings settings, double sampleRate)
    {
        var filters = EqMath.Filters(settings, sampleRate);
        var preampDb = settings.Preamp + (settings.AutoPreamp ? -EqMath.MaxBoostDb(filters, sampleRate) : 0);
        _state = new State(filters, (float)Math.Pow(10, preampDb / 20), settings.Enabled);
    }

    public void Reset() => Array.Clear(_z);

    public void Process(float[] buffer, int frames)
    {
        var state = _state;
        if (!state.Enabled)
            return;
        var filters = state.Filters;
        if (filters.Length != _lastCount)
        {
            Array.Clear(_z);
            _lastCount = filters.Length;
        }

        var samples = frames * 2;
        var pre = state.Preamp;
        if (pre != 1f)
            for (var i = 0; i < samples; i++)
                buffer[i] *= pre;

        for (var k = 0; k < filters.Length; k++)
        {
            var f = filters[k];
            var o = k * 4;
            if (f == Biquad.Identity)
            {
                // Banda a zero: memória limpa, para não estalar quando voltar a ser usada.
                Array.Clear(_z, o, 4);
                continue;
            }
            double zl1 = _z[o], zl2 = _z[o + 1], zr1 = _z[o + 2], zr2 = _z[o + 3];
            for (var i = 0; i < samples; i += 2)
            {
                double x = buffer[i];
                var y = f.B0 * x + zl1;
                zl1 = f.B1 * x - f.A1 * y + zl2;
                zl2 = f.B2 * x - f.A2 * y;
                buffer[i] = (float)y;

                x = buffer[i + 1];
                y = f.B0 * x + zr1;
                zr1 = f.B1 * x - f.A1 * y + zr2;
                zr2 = f.B2 * x - f.A2 * y;
                buffer[i + 1] = (float)y;
            }
            // Números subnormais deixam o processador lento no silêncio: corta-se a zero.
            _z[o] = Math.Abs(zl1) < 1e-25 ? 0 : zl1;
            _z[o + 1] = Math.Abs(zl2) < 1e-25 ? 0 : zl2;
            _z[o + 2] = Math.Abs(zr1) < 1e-25 ? 0 : zr1;
            _z[o + 3] = Math.Abs(zr2) < 1e-25 ? 0 : zr2;
        }
    }
}
