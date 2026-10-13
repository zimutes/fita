using System.Diagnostics;
using Fita.Audio;
using Fita.Library;

namespace Fita.Tests;

public sealed class LoudnessTests
{
    private const string Summary = """
        [Parsed_ebur128_0 @ 000001] Summary:

          Integrated loudness:
            I:         -22.3 LUFS
            Threshold: -32.3 LUFS

          Loudness range:
            LRA:         0.0 LU

          True peak:
            Peak:      -18.5 dBFS
        """;

    [Fact]
    public void The_ebur128_summary_gives_loudness_and_true_peak()
    {
        var r = LoudnessScanner.ParseSummary(Summary);
        Assert.NotNull(r);
        Assert.Equal(-22.3, r.Value.Lufs, 6);
        Assert.Equal(-18.5, r.Value.PeakDb, 6);
    }

    [Fact]
    public void Silence_or_no_summary_gives_nothing()
    {
        Assert.Null(LoudnessScanner.ParseSummary("Error opening input"));
        Assert.Null(LoudnessScanner.ParseSummary("Summary:\n  I:  -70.0 LUFS\n  Peak: -inf dBFS"));
    }

    [Fact]
    public void A_sine_at_minus_20_dbfs_measures_about_minus_20_lufs()
    {
        // Só onde houver ffmpeg (no PC de desenvolvimento; no CI pode não haver).
        var ffmpeg = FfmpegLocator.FindFfmpeg(null);
        if (ffmpeg is null)
            return;
        var file = Path.Combine(Path.GetTempPath(), "fita-r128-" + Guid.NewGuid().ToString("N") + ".flac");
        try
        {
            // O seno do ffmpeg tem 1/8 de amplitude; ×0,8 dá 0,1 (−20 dBFS de pico). A 1 kHz, nos
            // dois canais, o volume integrado fica praticamente igual ao pico: cerca de −20 LUFS.
            var make = Process.Start(new ProcessStartInfo(ffmpeg,
                ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=1000:duration=6:sample_rate=48000",
                 "-af", "volume=0.8,pan=stereo|c0=c0|c1=c0", file]) { CreateNoWindow = true })!;
            make.WaitForExit();
            var r = LoudnessScanner.Measure(ffmpeg, file, 0, 0, CancellationToken.None);
            Assert.NotNull(r);
            Assert.InRange(r.Value.Lufs, -20.6, -19.4);
            Assert.InRange(r.Value.PeakDb, -20.5, -19.5);
            // Num troço (como as faixas de um .cue) mede só esse bocado.
            Assert.NotNull(LoudnessScanner.Measure(ffmpeg, file, 2, 4, CancellationToken.None));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Upcoming_follows_the_play_order_without_reshuffling()
    {
        var playlist = new Playlist();
        var tracks = Enumerable.Range(1, 6).Select(i => new Track($"/m/{i}.mp3")).ToList();
        playlist.Items.ReplaceAll(tracks);
        Assert.Equal(tracks.Skip(2).Take(3), playlist.Upcoming(tracks[1], 3));
        Assert.Equal(tracks.Skip(4), playlist.Upcoming(tracks[3], 5));
        playlist.Repeat = RepeatMode.All;
        Assert.Equal([tracks[5], tracks[0], tracks[1]], playlist.Upcoming(tracks[4], 3));
    }
}
