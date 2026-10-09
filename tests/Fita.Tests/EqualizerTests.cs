using Fita.Audio;

namespace Fita.Tests;

public class EqualizerTests
{
    private const double Rate = 48000;

    [Theory]
    [InlineData(1000, 6)]
    [InlineData(62, -9)]
    [InlineData(8000, 12)]
    public void Peak_band_gives_its_gain_at_its_frequency(double frequency, double gain)
    {
        var filter = Biquad.For(new EqBand { Frequency = frequency, Gain = gain, Q = 1.41, Type = EqBandType.Peak }, Rate);
        Assert.Equal(gain, filter.MagnitudeDb(frequency, Rate), 1);
        // Longe da banda, quase nada.
        var far = frequency * 16 > 20000 ? frequency / 16 : frequency * 16;
        Assert.InRange(filter.MagnitudeDb(far, Rate), -0.5, 0.5);
    }

    [Fact]
    public void Flat_band_is_identity()
    {
        Assert.Equal(Biquad.Identity, Biquad.For(new EqBand { Frequency = 1000, Gain = 0 }, Rate));
    }

    [Fact]
    public void Shelves_reach_their_gain_at_the_far_end()
    {
        var low = Biquad.For(new EqBand { Frequency = 200, Gain = 6, Q = 0.71, Type = EqBandType.LowShelf }, Rate);
        var high = Biquad.For(new EqBand { Frequency = 4000, Gain = -6, Q = 0.71, Type = EqBandType.HighShelf }, Rate);
        Assert.Equal(6, low.MagnitudeDb(20, Rate), 0);
        Assert.Equal(0, low.MagnitudeDb(10000, Rate), 0);
        Assert.Equal(-6, high.MagnitudeDb(18000, Rate), 0);
        Assert.Equal(0, high.MagnitudeDb(100, Rate), 0);
    }

    [Fact]
    public void Low_cut_removes_rumble()
    {
        var cut = Biquad.For(new EqBand { Frequency = 80, Q = 0.71, Type = EqBandType.LowCut }, Rate);
        Assert.True(cut.MagnitudeDb(20, Rate) < -20);
        Assert.InRange(cut.MagnitudeDb(1000, Rate), -0.2, 0.2);
    }

    [Fact]
    public void Auto_preamp_cancels_the_biggest_boost()
    {
        var eq = new EqSettings();
        EqPresets.Apply(eq, EqPresets.BuiltIn.First(p => p.Name == "BassBoost"));
        var auto = EqMath.AutoPreampDb(eq, Rate);
        var max = EqMath.MaxBoostDb(EqMath.Filters(eq, Rate), Rate);
        Assert.True(auto < -6);
        Assert.Equal(-max, auto, 6);

        eq.AutoPreamp = false;
        Assert.Equal(0, EqMath.AutoPreampDb(eq, Rate));
    }

    [Fact]
    public void Processor_boosts_a_sine_at_the_band()
    {
        var eq = new EqSettings { AutoPreamp = false };
        eq.Bands[5].Gain = 6; // 1 kHz
        var processor = new EqProcessor();
        processor.Configure(eq, Rate);

        const int frames = 48000;
        var buffer = new float[frames * 2];
        for (var i = 0; i < frames; i++)
            buffer[2 * i] = buffer[2 * i + 1] = 0.25f * MathF.Sin(2 * MathF.PI * 1000 * i / (float)Rate);
        processor.Process(buffer, frames);

        // Depois de assentar, a amplitude passa de 0,25 para ~0,5 (+6 dB).
        var peak = 0f;
        for (var i = frames / 2; i < frames; i++)
            peak = Math.Max(peak, Math.Abs(buffer[2 * i]));
        Assert.InRange(peak, 0.47f, 0.53f);
    }

    [Fact]
    public void Disabled_processor_leaves_audio_untouched()
    {
        var eq = new EqSettings { Enabled = false, Preamp = 6 };
        eq.Bands[0].Gain = 12;
        var processor = new EqProcessor();
        processor.Configure(eq, Rate);
        var buffer = new[] { 0.1f, -0.2f, 0.3f, -0.4f };
        processor.Process(buffer, 2);
        Assert.Equal(new[] { 0.1f, -0.2f, 0.3f, -0.4f }, buffer);
    }

    [Fact]
    public void Sanitize_repairs_broken_settings()
    {
        var eq = new EqSettings
        {
            Preamp = double.NaN,
            Bands = [new EqBand { Frequency = -5, Gain = 99, Q = 0, Type = (EqBandType)42 }],
        };
        eq.Sanitize();
        Assert.Equal(0, eq.Preamp);
        var b = eq.Bands[0];
        Assert.Equal(EqSettings.MinFrequency, b.Frequency);
        Assert.Equal(EqSettings.MaxGain, b.Gain);
        Assert.Equal(0.2, b.Q);
        Assert.Equal(EqBandType.Peak, b.Type);
    }

    [Fact]
    public void Every_preset_has_ten_bands()
    {
        Assert.All(EqPresets.BuiltIn, p => Assert.Equal(EqSettings.IsoFrequencies.Length, p.Gains.Length));
    }
}
