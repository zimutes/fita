using Fita.Audio;

namespace Fita.Tests;

public class MonitorTests
{
    private const int Rate = 48000;

    private static float[] Tone(double hz, double seconds, double left = 0.1, double right = 0.1)
    {
        var frames = (int)(Rate * seconds);
        var block = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var v = Math.Sin(2 * Math.PI * hz * i / Rate);
            block[2 * i] = (float)(left * v);
            block[2 * i + 1] = (float)(right * v);
        }
        return block;
    }

    private static void Run(MonitorProcessor m, float[] audio)
    {
        // Em blocos, como a thread de saída.
        var block = new float[2048];
        for (var at = 0; at < audio.Length; at += block.Length)
        {
            var n = Math.Min(block.Length, audio.Length - at);
            Array.Copy(audio, at, block, 0, n);
            m.Process(block, n / 2, Rate);
            Array.Copy(block, 0, audio, at, n);
        }
    }

    private static double Rms(float[] audio, int channel, int skipFrames)
    {
        double sum = 0;
        var n = 0;
        for (var i = skipFrames * 2 + channel; i < audio.Length; i += 2)
        {
            sum += audio[i] * (double)audio[i];
            n++;
        }
        return 20 * Math.Log10(Math.Sqrt(sum / n));
    }

    [Fact]
    public void Mono_sums_and_side_cancels_what_is_equal()
    {
        var m = new MonitorProcessor { Mode = MonitorMode.Mono };
        var block = new float[] { 1, 0, 0.2f, 0.6f };
        m.Process(block, 2, Rate);
        Assert.Equal([0.5f, 0.5f, 0.4f, 0.4f], block);
        m.Mode = MonitorMode.Side;
        block = [0.3f, 0.3f, 0.5f, -0.5f];
        m.Process(block, 2, Rate);
        Assert.Equal([0, 0, 0.5f, -0.5f], block);
        m.Mode = MonitorMode.Right;
        block = [0.3f, 0.7f];
        m.Process(block, 1, Rate);
        Assert.Equal([0.7f, 0.7f], block);
    }

    [Fact]
    public void Soloing_the_bass_band_keeps_the_bass_and_drops_the_rest()
    {
        var m = new MonitorProcessor { Solo = 1 };
        var bass = Tone(120, 1);
        var reference = Rms(bass, 0, Rate / 2);
        Run(m, bass);
        Assert.InRange(Rms(bass, 0, Rate / 2) - reference, -1.5, 0.3);
        var mid = Tone(2000, 1);
        reference = Rms(mid, 0, Rate / 2);
        m.Solo = 1;
        Run(m, mid);
        Assert.True(Rms(mid, 0, Rate / 2) - reference < -40);
        // Fora do solo, nada muda.
        m.Solo = -1;
        var plain = Tone(2000, 0.2);
        var copy = (float[])plain.Clone();
        Run(m, plain);
        Assert.Equal(copy, plain);
    }

    [Fact]
    public void The_meter_reads_a_minus_20_dbfs_sine_as_minus_20_lufs()
    {
        var m = new MonitorProcessor { MeterOn = true };
        Run(m, Tone(997, 4));
        var r = m.Reading;
        Assert.InRange(r.ShortTerm, -20.2, -19.8);
        Assert.InRange(r.Momentary, -20.2, -19.8);
        Assert.Equal(1, r.Correlation, 2);
        Run(m, Tone(997, 1, 0.1, -0.1));
        Assert.True(m.Reading.Correlation < -0.9);
    }
}
