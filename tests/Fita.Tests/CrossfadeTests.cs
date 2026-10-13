using Fita.Audio;

namespace Fita.Tests;

public sealed class CrossfadeTests
{
    private static readonly Segment SegA = new(new PlayItem("A", "a"), 0);
    private static readonly Segment SegB = new(new PlayItem("B", "b"), 0);

    /// <summary>Uma faixa de valor constante, em blocos, com a marca de início no primeiro.</summary>
    private static IEnumerable<Chunk> Track(Segment start, float value, int frames, int block = 100)
    {
        for (var at = 0; at < frames; at += block)
        {
            var n = Math.Min(block, frames - at);
            var data = new float[n * 2];
            Array.Fill(data, value);
            yield return new Chunk(1, data, n) { Start = at == 0 ? start : null };
        }
    }

    private static (List<float> Left, List<(int Frame, Segment Segment)> Starts) Run(bool crossfade, int hold, int framesA, int framesB)
    {
        var left = new List<float>();
        var starts = new List<(int, Segment)>();
        var fade = new Crossfade(1, hold, c =>
        {
            if (c.Start is not null)
                starts.Add((left.Count, c.Start));
            for (var i = 0; i < c.Frames; i++)
                left.Add(c.Data[i * 2]);
            return true;
        });
        foreach (var c in Track(SegA, 1f, framesA))
            Assert.True(fade.Emit(c));
        Assert.True(fade.EndTrack(crossfade));
        foreach (var c in Track(SegB, 0.5f, framesB))
            Assert.True(fade.Emit(c));
        Assert.True(fade.EndTrack(false));
        return (left, starts);
    }

    [Fact]
    public void Without_crossfade_everything_comes_out_in_order()
    {
        var (left, starts) = Run(crossfade: false, hold: 300, framesA: 1000, framesB: 800);
        Assert.Equal(1800, left.Count);
        Assert.All(left.Take(1000), v => Assert.Equal(1f, v));
        Assert.All(left.Skip(1000), v => Assert.Equal(0.5f, v));
        Assert.Equal([(0, SegA), (1000, SegB)], starts);
    }

    [Fact]
    public void With_crossfade_the_tracks_overlap_and_b_starts_where_the_mix_starts()
    {
        var (left, starts) = Run(crossfade: true, hold: 300, framesA: 1000, framesB: 800);
        // As duas faixas sobrepõem-se 300 frames.
        Assert.Equal(1500, left.Count);
        Assert.Equal([(0, SegA), (700, SegB)], starts);
        Assert.All(left.Take(700), v => Assert.Equal(1f, v));
        Assert.All(left.Skip(1000), v => Assert.Equal(0.5f, v));
        // Na mistura: começa quase só A, acaba quase só B, e desce sem degraus.
        var mix = left.Skip(700).Take(300).ToList();
        Assert.InRange(mix[0], 0.99f, 1.01f);
        Assert.InRange(mix[^1], 0.49f, 0.51f);
        for (var i = 1; i < mix.Count; i++)
            Assert.True(Math.Abs(mix[i] - mix[i - 1]) < 0.01f);
    }

    [Fact]
    public void Short_tracks_are_not_crossfaded()
    {
        var (left, starts) = Run(crossfade: true, hold: 300, framesA: 500, framesB: 800);
        Assert.Equal(1300, left.Count);
        Assert.Equal([(0, SegA), (500, SegB)], starts);
    }

    [Fact]
    public void A_next_track_shorter_than_the_fade_lets_the_previous_one_fade_out()
    {
        var (left, starts) = Run(crossfade: true, hold: 300, framesA: 1000, framesB: 100);
        // A sai até 700; a seguir os 100 de B misturados e os 200 que faltavam de A a desvanecer.
        Assert.Equal(1000, left.Count);
        Assert.Equal([(0, SegA), (700, SegB)], starts);
        Assert.InRange(left[^1], 0f, 0.02f);
    }
}
