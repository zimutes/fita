using System.Text.Json.Serialization;

namespace Fita.Producer;

/// <summary>As bandas do balanço tonal (as mesmas do solo de banda e do relatório).</summary>
public static class Bands
{
    public static readonly (double Low, double High)[] Edges =
        [(20, 60), (60, 250), (250, 800), (800, 2500), (2500, 6000), (6000, 20000)];

    /// <summary>Chaves dos nomes em <see cref="Loc"/> (curto: "Band.Sub", comprido: "BandLong.Sub").</summary>
    public static readonly string[] Keys = ["Sub", "Bass", "LowMid", "Mid", "HighMid", "Air"];

    public static int Count => Edges.Length;

    public static string Name(int band) => Loc.T("Band." + Keys[band]);

    public static string LongName(int band) => Loc.T("BandLong." + Keys[band]);

    /// <summary>Os centros das bandas de terço de oitava, de 25 Hz a 20 kHz (a curva do relatório).</summary>
    public static readonly double[] Thirds =
        [25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000, 1250, 1600, 2000,
         2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000, 20000];
}

/// <summary>
/// O que o modo producer sabe de uma faixa. O volume integrado, a gama (LRA) e o pico real vêm
/// do filtro ebur128 do ffmpeg (a medida de referência); o resto é calculado pela Fita na mesma
/// passagem (<see cref="SignalStats"/>). Fica em cache no analysis.json.
/// </summary>
public sealed class TrackAnalysis
{
    /// <summary>Muda quando o cálculo muda: as análises antigas refazem-se.</summary>
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public long Size { get; set; }
    public DateTime Modified { get; set; }

    public double Duration { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }

    /// <summary>Volume integrado (LUFS). NaN se a faixa é silêncio.</summary>
    public double Integrated { get; set; } = double.NaN;
    /// <summary>Gama de volume, LRA (LU).</summary>
    public double Range { get; set; } = double.NaN;
    /// <summary>Pico real (dBTP), sobreamostrado pelo ffmpeg.</summary>
    public double TruePeak { get; set; } = double.NaN;
    /// <summary>O integrado pela conta da Fita (para conferir com o do ffmpeg e para quando ele falta).</summary>
    public double OwnIntegrated { get; set; } = double.NaN;

    public double SamplePeak { get; set; } = double.NaN;
    public double ShortTermMax { get; set; } = double.NaN;
    public double MomentaryMax { get; set; } = double.NaN;
    /// <summary>Desvio DC do pior canal (dBFS).</summary>
    public double DcOffset { get; set; } = double.NaN;

    /// <summary>Saturações: três ou mais amostras seguidas no máximo. Os primeiros tempos ficam em ClipTimes.</summary>
    public int Clips { get; set; }
    public List<double> ClipTimes { get; set; } = [];

    /// <summary>Silêncio no início e no fim (s).</summary>
    public double SilenceStart { get; set; }
    public double SilenceEnd { get; set; }

    /// <summary>Energia de cada banda em relação à faixa inteira (dB), no meio (L+R).</summary>
    public double[] BandLevels { get; set; } = [];
    /// <summary>A curva em terços de oitava (dB em relação à faixa inteira).</summary>
    public double[] Curve { get; set; } = [];

    /// <summary>Correlação entre os canais (−1 a 1): 1 é mono, 0 é largo, negativo é fase invertida.</summary>
    public double Correlation { get; set; } = double.NaN;
    public double[] BandCorrelation { get; set; } = [];
    /// <summary>Quanto se perde (dB) ao somar em mono, por banda.</summary>
    public double[] MonoLoss { get; set; } = [];
    /// <summary>Lado em relação ao meio (dB), por banda: muito negativo é estreito.</summary>
    public double[] BandWidth { get; set; } = [];

    /// <summary>Volume de curta duração (3 s) a cada segundo (LUFS; NaN no silêncio).</summary>
    public float[] ShortTerm { get; set; } = [];
    /// <summary>Correlação a cada segundo (NaN no silêncio).</summary>
    public float[] CorrelationTimeline { get; set; } = [];

    /// <summary>PLR: pico real menos volume integrado. Pequeno quer dizer muito comprimido.</summary>
    [JsonIgnore]
    public double Plr => TruePeak - Integrated;

    [JsonIgnore]
    public bool HasLoudness => double.IsFinite(Integrated);
}
