using System.Globalization;
using Fita.Library;

namespace Fita.Producer;

public enum Severity { Problem, Warning, Info }

/// <summary>Um aviso sobre uma faixa, já escrito na língua da interface.</summary>
public sealed record Finding(Severity Severity, string Text);

/// <summary>Uma plataforma de streaming e a forma como normaliza o volume (valores públicos, aproximados).</summary>
public sealed record Platform(string Name, double Target, bool Raises)
{
    /// <summary>
    /// Quanto a plataforma mexe no volume (dB). As que sobem as faixas baixas (o Spotify) só sobem
    /// até o pico real chegar a −1 dBTP.
    /// </summary>
    public double Gain(TrackAnalysis a)
    {
        if (!a.HasLoudness)
            return 0;
        var gain = Target - a.Integrated;
        if (gain <= 0)
            return Math.Round(gain, 1);
        if (!Raises)
            return 0;
        var room = double.IsFinite(a.TruePeak) ? Math.Max(0, -1 - a.TruePeak) : 0;
        return Math.Round(Math.Min(gain, room), 1);
    }
}

/// <summary>As regras dos avisos e as comparações (com as referências, com o resto da lista).</summary>
public static class Findings
{
    public static readonly Platform[] Platforms =
    [
        new("Spotify", -14, Raises: true),
        new("YouTube", -14, Raises: false),
        new("Apple Music", -16, Raises: false),
        new("Tidal", -14, Raises: false),
    ];

    /// <summary>
    /// O balanço tonal de comparação: a média das referências, ou, sem referências, a média de
    /// todas as faixas. Null se não houver nada analisado.
    /// </summary>
    public static double[]? Baseline(IEnumerable<TrackAnalysis> references)
    {
        var list = references.Where(a => a.BandLevels.Length == Bands.Count).ToList();
        if (list.Count == 0)
            return null;
        var mean = new double[Bands.Count];
        for (var b = 0; b < mean.Length; b++)
            mean[b] = list.Average(a => a.BandLevels[b]);
        return mean;
    }

    /// <summary>A curva de comparação (média, em terços de oitava).</summary>
    public static double[]? BaselineCurve(IEnumerable<TrackAnalysis> references)
    {
        var list = references.Where(a => a.Curve.Length == Bands.Thirds.Length).ToList();
        if (list.Count == 0)
            return null;
        var mean = new double[Bands.Thirds.Length];
        for (var i = 0; i < mean.Length; i++)
        {
            var values = list.Select(a => a.Curve[i]).Where(double.IsFinite).ToList();
            mean[i] = values.Count > 0 ? values.Average() : double.NaN;
        }
        return mean;
    }

    public static double[]? Deltas(TrackAnalysis a, double[]? baseline) =>
        baseline is null || a.BandLevels.Length != baseline.Length
            ? null
            : a.BandLevels.Select((v, b) => Math.Round(v - baseline[b], 1)).ToArray();

    /// <summary>O volume do meio da lista (a mediana do integrado), para ver quem foge.</summary>
    public static double? MedianLoudness(IEnumerable<TrackAnalysis> analyses)
    {
        var values = analyses.Where(a => a.HasLoudness).Select(a => a.Integrated).OrderBy(v => v).ToList();
        if (values.Count < 3)
            return null;
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }

    /// <summary>Troços seguidos de correlação negativa, com pelo menos <paramref name="minSeconds"/> segundos.</summary>
    public static List<(int From, int To)> NegativeRuns(TrackAnalysis a, int minSeconds = 2)
    {
        var runs = new List<(int, int)>();
        var start = -1;
        var timeline = a.CorrelationTimeline;
        for (var i = 0; i <= timeline.Length; i++)
        {
            var negative = i < timeline.Length && timeline[i] < 0;
            if (negative && start < 0)
                start = i;
            else if (!negative && start >= 0)
            {
                if (i - start >= minSeconds)
                    runs.Add((start, i));
                start = -1;
            }
        }
        return runs;
    }

    /// <summary>
    /// Os avisos de uma faixa, do mais grave para o menos. <paramref name="deltas"/>: o balanço em
    /// relação à comparação (por banda); <paramref name="median"/>: o volume do meio da lista.
    /// </summary>
    public static List<Finding> For(TrackAnalysis a, double[]? deltas = null, bool againstReferences = false, double? median = null)
    {
        var list = new List<Finding>();
        var c = CultureInfo.InvariantCulture;
        string N(double v, string format = "0.0") => v.ToString(format, c);

        if (!a.HasLoudness)
        {
            list.Add(new(Severity.Info, Loc.T("F.Silent")));
            return list;
        }
        if (a.TruePeak > 0)
            list.Add(new(Severity.Problem, Loc.F("F.TruePeakOver", N(a.TruePeak, "+0.0;-0.0"))));
        else if (a.TruePeak > -1)
            list.Add(new(Severity.Warning, Loc.F("F.TruePeakHigh", N(a.TruePeak, "+0.0;-0.0"))));
        if (a.Clips > 0)
            list.Add(new(Severity.Problem, Loc.F("F.Clipping", a.Clips, Format.Time(a.ClipTimes.FirstOrDefault()))));
        foreach (var (from, to) in NegativeRuns(a))
            list.Add(new(Severity.Problem, Loc.F("F.PhaseRun", Format.Time(from), Format.Time(to))));
        if (a.BandCorrelation.Length == Bands.Count)
        {
            var low = Math.Min(a.BandCorrelation[0], a.BandCorrelation[1]);
            if (low < 0.7)
                list.Add(new(Severity.Warning, Loc.F("F.BassNotMono", N(low, "+0.00;-0.00"))));
            for (var b = 0; b < Bands.Count; b++)
                if (a.MonoLoss[b] < -3)
                    list.Add(new(Severity.Warning, Loc.F("F.MonoLoss", N(-a.MonoLoss[b]), Bands.LongName(b))));
        }
        if (a.Correlation < 0.2 && NegativeRuns(a).Count == 0)
            list.Add(new(Severity.Warning, Loc.F("F.VeryWide", N(a.Correlation, "+0.00;-0.00"))));
        if (double.IsFinite(a.Plr) && a.Plr < 7)
            list.Add(new(Severity.Warning, Loc.F("F.Crushed", N(a.Plr))));
        if (a.DcOffset > -50)
            list.Add(new(Severity.Warning, Loc.F("F.Dc", N(a.DcOffset))));
        if (median is { } m && Math.Abs(a.Integrated - m) > 2)
            list.Add(new(Severity.Warning, Loc.F("F.Outlier", N(a.Integrated - m, "+0.0;-0.0"))));
        if (deltas is not null)
            for (var b = 0; b < deltas.Length; b++)
                if (Math.Abs(deltas[b]) >= 3)
                    list.Add(new(Severity.Info, Loc.F(againstReferences ? "F.TonalRef" : "F.TonalList",
                        N(deltas[b], "+0.0;-0.0"), Bands.LongName(b))));
        var spotify = Platforms[0].Gain(a);
        if (spotify <= -4)
            list.Add(new(Severity.Info, Loc.F("F.Loud", N(-spotify))));
        if (a.SilenceStart > 2)
            list.Add(new(Severity.Info, Loc.F("F.LeadIn", N(a.SilenceStart))));
        if (a.SilenceEnd > 5)
            list.Add(new(Severity.Info, Loc.F("F.Tail", N(a.SilenceEnd))));
        return list;
    }
}
