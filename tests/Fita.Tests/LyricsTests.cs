using Fita.Library;

namespace Fita.Tests;

public sealed class LyricsTests
{
    [Fact]
    public void Lrc_lines_come_up_at_their_time()
    {
        var lyrics = Lyrics.Parse("""
            [ar:Alguém]
            [00:05.50]Primeira linha
            [00:10.00][01:10.00]Refrão
            [00:20.123]<00:20.123>Com <00:21.00>palavras
            """)!;
        Assert.Equal(4, lyrics.Count);
        Assert.Equal("", lyrics.LineAt(1));
        Assert.Equal("Primeira linha", lyrics.LineAt(5.5));
        Assert.Equal("Primeira linha", lyrics.LineAt(9.99));
        Assert.Equal("Refrão", lyrics.LineAt(10));
        Assert.Equal("Com palavras", lyrics.LineAt(30));
        Assert.Equal("Refrão", lyrics.LineAt(75));
    }

    [Fact]
    public void Offset_moves_the_lines_earlier_or_later()
    {
        var earlier = Lyrics.Parse("[offset:+500]\n[00:10.00]Linha")!;
        Assert.Equal("Linha", earlier.LineAt(9.6));
        var later = Lyrics.Parse("[offset:-500]\n[00:10.00]Linha")!;
        Assert.Equal("", later.LineAt(10.2));
    }

    [Fact]
    public void Plain_lyrics_without_times_are_not_shown()
    {
        Assert.Null(Lyrics.Parse("Só texto\nsem tempos"));
    }
}
