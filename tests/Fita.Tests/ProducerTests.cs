using System.Diagnostics;
using Fita.Audio;
using Fita.Producer;
using Fita.Themes;

namespace Fita.Tests;

public class ProducerTests
{
    private static TrackAnalysis Run(int rate, double seconds, Func<double, (double L, double R)> signal, bool mono = false)
    {
        var stats = new SignalStats(rate, mono);
        var frames = (int)(rate * seconds);
        var block = new float[2048];
        for (var at = 0; at < frames; at += block.Length / 2)
        {
            var n = Math.Min(block.Length / 2, frames - at);
            for (var i = 0; i < n; i++)
            {
                var (l, r) = signal((double)(at + i) / rate);
                block[2 * i] = (float)l;
                block[2 * i + 1] = (float)r;
            }
            stats.Feed(block.AsSpan(0, n * 2));
        }
        var a = new TrackAnalysis();
        stats.Finish(a);
        return a;
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void A_997_hz_sine_at_minus_20_dbfs_on_both_channels_is_minus_20_lufs(int rate)
    {
        // A referência do BS.1770: um seno de 997 Hz a 0 dBFS num canal mede −3,01 LUFS; nos dois
        // canais a −20 dBFS de pico dá −20 LUFS.
        var a = Run(rate, 6, t =>
        {
            var v = 0.1 * Math.Sin(2 * Math.PI * 997 * t);
            return (v, v);
        });
        Assert.InRange(a.OwnIntegrated, -20.15, -19.85);
        Assert.InRange(a.ShortTermMax, -20.2, -19.8);
        Assert.InRange(a.SamplePeak, -20.05, -19.95);
        Assert.Equal(1, a.Correlation, 3);
    }

    [Fact]
    public void A_mono_file_counts_one_channel()
    {
        var a = Run(48000, 4, t =>
        {
            var v = Math.Sin(2 * Math.PI * 997 * t);
            return (v, v);
        }, mono: true);
        Assert.InRange(a.OwnIntegrated, -3.2, -2.8);
    }

    [Fact]
    public void Inverted_phase_shows_as_negative_correlation_and_mono_loss()
    {
        var a = Run(48000, 4, t =>
        {
            var v = 0.3 * Math.Sin(2 * Math.PI * 110 * t);
            return (v, -v);
        });
        Assert.Equal(-1, a.Correlation, 2);
        Assert.All(a.CorrelationTimeline, c => Assert.True(c < -0.99));
        // 110 Hz cai nos graves: lá, em mono, desaparece.
        Assert.True(a.MonoLoss[1] < -40, $"perda em mono {a.MonoLoss[1]}");
        Assert.True(a.BandCorrelation[1] < -0.99);
    }

    [Fact]
    public void Uncorrelated_noise_is_wide_and_loses_3_db_in_mono()
    {
        var rl = new Random(1);
        var rr = new Random(2);
        var a = Run(48000, 6, _ => (rl.NextDouble() - 0.5, rr.NextDouble() - 0.5));
        Assert.InRange(a.Correlation, -0.05, 0.05);
        Assert.All(a.MonoLoss, m => Assert.InRange(m, -3.4, -2.6));
        Assert.All(a.BandWidth, w => Assert.InRange(w, -1, 1));
    }

    [Fact]
    public void A_100_hz_tone_lives_in_the_bass_band()
    {
        var a = Run(44100, 5, t =>
        {
            var v = 0.5 * Math.Sin(2 * Math.PI * 100 * t);
            return (v, v);
        });
        Assert.InRange(a.BandLevels[1], -0.2, 0);
        for (var b = 0; b < Bands.Count; b++)
            if (b != 1)
                Assert.True(a.BandLevels[b] < -30, $"banda {b}: {a.BandLevels[b]}");
        Assert.Equal(Bands.Thirds.Length, a.Curve.Length);
        // A curva tem o máximo no terço de 100 Hz.
        var peak = Array.IndexOf(a.Curve, a.Curve.Where(double.IsFinite).Max());
        Assert.Equal(100, Bands.Thirds[peak]);
    }

    [Fact]
    public void Clipped_bursts_are_counted_one_by_one()
    {
        // Um seno saturado durante 20 ms a cada meio segundo: 4 saturações em 2 s. Uma passagem
        // saturada seguida (a onda quadrada) conta uma vez só.
        var a = Run(48000, 2, t =>
        {
            var burst = t % 0.5 < 0.02;
            var v = burst ? Math.Clamp(3 * Math.Sin(2 * Math.PI * 200 * t), -1, 1) : 0.3 * Math.Sin(2 * Math.PI * 200 * t);
            return (v, v);
        });
        Assert.Equal(4, a.Clips);
        Assert.Equal([0, 0.5, 1, 1.5], a.ClipTimes.Select(c => Math.Round(c, 1)));
        Assert.Equal(0, a.SamplePeak, 2);
        var square = Run(48000, 2, t => Math.Sin(2 * Math.PI * 50 * t) >= 0 ? (1.0, 1.0) : (-1.0, -1.0));
        Assert.Equal(1, square.Clips);
    }

    [Fact]
    public void Silence_at_the_ends_and_dc_offset_are_measured()
    {
        var a = Run(48000, 6, t =>
        {
            var v = t is > 1 and < 4 ? 0.2 * Math.Sin(2 * Math.PI * 440 * t) + 0.01 : 0;
            return (v, v);
        });
        Assert.InRange(a.SilenceStart, 0.95, 1.05);
        Assert.InRange(a.SilenceEnd, 1.95, 2.05);
        // 0,01 durante metade da faixa: média 0,005, cerca de −46 dBFS.
        Assert.InRange(a.DcOffset, -47, -45);
        Assert.Equal(6, a.Duration, 2);
        Assert.Equal(6, a.ShortTerm.Length);
        Assert.True(float.IsNaN(a.ShortTerm[0]));
    }

    [Fact]
    public void Ebur128_summary_is_read()
    {
        const string log = """
            [Parsed_ebur128_1 @ 0x1] Summary:

              Integrated loudness:
                I:          -9.8 LUFS
                Threshold: -20.1 LUFS

              Loudness range:
                LRA:         5.2 LU
                Threshold: -30.0 LUFS
                LRA low:   -13.1 LUFS
                LRA high:   -7.9 LUFS

              True peak:
                Peak:        0.3 dBFS
            """;
        var (i, lra, peak) = Analyzer.ParseSummary(log);
        Assert.Equal(-9.8, i);
        Assert.Equal(5.2, lra);
        Assert.Equal(0.3, peak);
        Assert.Equal((null, null, null), Analyzer.ParseSummary("Error opening input"));
    }

    [Fact]
    public void With_ffmpeg_the_reference_and_our_own_loudness_agree()
    {
        // Só onde houver ffmpeg (no PC de desenvolvimento; no CI pode não haver).
        var ffmpeg = FfmpegLocator.FindFfmpeg(null);
        if (ffmpeg is null)
            return;
        var file = Path.Combine(Path.GetTempPath(), "fita-prod-" + Guid.NewGuid().ToString("N") + ".flac");
        try
        {
            // Ruído rosa estéreo que sobe a meio: dá integrado, LRA e picos que se possam comparar.
            var make = Process.Start(new ProcessStartInfo(ffmpeg,
                ["-v", "error", "-y", "-f", "lavfi", "-i", "anoisesrc=color=pink:duration=12:sample_rate=44100:amplitude=0.3:seed=7",
                 "-af", "volume='if(lt(t,6),0.3,1)':eval=frame,pan=stereo|c0=c0|c1=0.8*c0", file]) { CreateNoWindow = true })!;
            make.WaitForExit();
            var a = Analyzer.Analyze(ffmpeg, file, 0, 0, 44100, 2, CancellationToken.None);
            Assert.NotNull(a);
            Assert.True(a.HasLoudness);
            Assert.InRange(a.Integrated - a.OwnIntegrated, -0.3, 0.3);
            Assert.True(double.IsFinite(a.Range) && a.Range > 3, $"LRA {a.Range}");
            Assert.True(a.TruePeak >= a.SamplePeak - 0.01, $"TP {a.TruePeak} pico {a.SamplePeak}");
            Assert.Equal(12, a.Duration, 1);
            Assert.Equal(44100, a.SampleRate);
            Assert.True(a.Correlation > 0.95);
            // Num troço (como as faixas de um .cue) analisa só esse bocado.
            var part = Analyzer.Analyze(ffmpeg, file, 2, 5, 44100, 2, CancellationToken.None);
            Assert.NotNull(part);
            Assert.Equal(3, part.Duration, 1);
        }
        finally
        {
            File.Delete(file);
        }
    }
}

public class ProducerFindingsTests
{
    private static TrackAnalysis Sample(double integrated = -10, double truePeak = -1.5) => new()
    {
        Integrated = integrated, TruePeak = truePeak, Range = 6, SamplePeak = truePeak - 0.3, Correlation = 0.8,
        BandLevels = [-12, -6, -8, -9, -13, -18], BandCorrelation = [1, 0.95, 0.8, 0.7, 0.6, 0.5],
        MonoLoss = [0, -0.1, -0.5, -0.8, -1, -1.2], BandWidth = [-30, -20, -12, -10, -8, -6],
        Curve = [.. Enumerable.Repeat(-15.0, Bands.Thirds.Length)], Duration = 200, SampleRate = 44100, Channels = 2,
        ShortTerm = [.. Enumerable.Repeat(-11f, 200)], CorrelationTimeline = [.. Enumerable.Repeat(0.8f, 200)],
        DcOffset = -90, MomentaryMax = -7, ShortTermMax = -9,
    };

    [Fact]
    public void Spotify_turns_quiet_tracks_up_only_until_minus_1_dbtp()
    {
        // O exemplo do Spotify: −20 LUFS com pico real de −5 dBTP só sobe 4 dB (fica a −16).
        var spotify = Findings.Platforms[0];
        Assert.Equal(4, spotify.Gain(Sample(-20, -5)));
        Assert.Equal(-4, spotify.Gain(Sample(-10, -0.5)));
        var youtube = Findings.Platforms[1];
        Assert.Equal(0, youtube.Gain(Sample(-20, -5)));
        Assert.Equal(-4, youtube.Gain(Sample(-10, -0.5)));
        Assert.Equal(-6, Findings.Platforms[2].Gain(Sample(-10, -0.5)));
    }

    [Fact]
    public void Findings_flag_peaks_clipping_phase_and_bass_width()
    {
        Loc.Apply(UiLanguage.En, upper: false);
        var a = Sample(truePeak: 0.4);
        a.Clips = 3;
        a.ClipTimes = [62.5, 70, 80];
        a.CorrelationTimeline = [.. Enumerable.Repeat(0.8f, 100), .. Enumerable.Repeat(-0.4f, 5), .. Enumerable.Repeat(0.8f, 95)];
        a.BandCorrelation[1] = 0.3;
        var f = Findings.For(a);
        Assert.Contains(f, x => x.Severity == Severity.Problem && x.Text.Contains("+0.4 dBTP"));
        Assert.Contains(f, x => x.Severity == Severity.Problem && x.Text.Contains("Clipping: 3") && x.Text.Contains("1:02"));
        Assert.Contains(f, x => x.Severity == Severity.Problem && x.Text.Contains("1:40") && x.Text.Contains("1:45"));
        Assert.Contains(f, x => x.Text.StartsWith("Bass not in mono"));
        Assert.DoesNotContain(Findings.For(Sample(-14, -1.5)), x => x.Severity != Severity.Info);
    }

    [Fact]
    public void Tonal_deltas_compare_with_the_references_and_outliers_with_the_median()
    {
        Loc.Apply(UiLanguage.En, upper: false);
        var reference = Sample();
        var mix = Sample();
        mix.BandLevels = [-8, -6, -8, -9, -13, -22];
        var baseline = Findings.Baseline([reference]);
        var deltas = Findings.Deltas(mix, baseline)!;
        Assert.Equal([4, 0, 0, 0, 0, -4], deltas);
        var f = Findings.For(mix, deltas, againstReferences: true, median: -14);
        Assert.Contains(f, x => x.Text == "+4.0 dB in the sub-bass compared with the references");
        Assert.Contains(f, x => x.Text == "−4.0 dB in the highs compared with the references".Replace('−', '-'));
        Assert.Contains(f, x => x.Text.StartsWith("+4.0 LU away"));
        Assert.Equal(-12, Findings.MedianLoudness([Sample(-10), Sample(-12), Sample(-15)]));
    }

    [Fact]
    public void The_report_has_the_summary_the_charts_and_the_csv()
    {
        Loc.Apply(UiLanguage.Pt, upper: false);
        try
        {
            var a = Sample(truePeak: 0.2);
            var rows = new List<ReportTrack>
            {
                new(1, "Artista – Faixa <1>", "/m/1.flac", false, a, [1, 0, -3.5, 0, 0, 2], Findings.For(a), [new Note { Time = 12, Text = "tarola alta" }]),
                new(2, "Referência", "/m/2.flac", true, Sample(-9), [0, 0, 0, 0, 0, 0], [], []),
            };
            var info = new ReportInfo("Relatório de produção", new DateTime(2026, 10, 13, 21, 30, 0), Findings.Baseline([a]),
                Findings.BaselineCurve([a]), true, -10);
            var html = Report.Html(rows, info, Catalog.All[0]);
            Assert.Contains("<!doctype html>", html);
            Assert.Contains("Artista – Faixa &lt;1&gt;", html);
            Assert.Contains("Pico real +0.2 dBTP", html);
            Assert.Contains("tarola alta", html);
            Assert.Equal(2 * 2 + 1, CountOf(html, "<svg class=\"chart\""));
            var csv = Report.Csv(rows);
            var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("#;Faixa;Ficheiro;REF;LUFS", lines[0]);
            Assert.Contains(";-10;", lines[1]);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "fita-relatorio-teste.html"), html);
        }
        finally
        {
            Loc.Apply(UiLanguage.En, upper: false);
        }
    }

    private static int CountOf(string text, string what)
    {
        var n = 0;
        for (var i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + 1, StringComparison.Ordinal))
            n++;
        return n;
    }
}
