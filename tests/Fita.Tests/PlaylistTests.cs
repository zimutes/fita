using Fita.Library;

namespace Fita.Tests;

public class PlaylistTests
{
    private static Playlist Make(int count, out List<Track> tracks)
    {
        var p = new Playlist();
        tracks = Enumerable.Range(1, count).Select(i => new Track($"/m/{i:00}.mp3")).ToList();
        p.Add(tracks);
        return p;
    }

    [Fact]
    public void Next_and_previous_follow_the_list()
    {
        var p = Make(3, out var t);
        Assert.Equal(t[1], p.After(t[0], automatic: true));
        Assert.Null(p.After(t[2], automatic: true));
        Assert.Equal(t[1], p.Before(t[2]));
        Assert.Equal(t[0], p.Before(t[0]));
    }

    [Fact]
    public void Repeat_all_wraps_and_repeat_one_stays()
    {
        var p = Make(3, out var t);
        p.Repeat = RepeatMode.All;
        Assert.Equal(t[0], p.After(t[2], automatic: true));
        p.Repeat = RepeatMode.One;
        Assert.Equal(t[1], p.After(t[1], automatic: true));
        // Carregar em Seguinte passa à frente mesmo a repetir uma.
        Assert.Equal(t[2], p.After(t[1], automatic: false));
    }

    [Fact]
    public void Removed_track_continues_from_where_it_was()
    {
        var p = Make(4, out var t);
        p.Remove([t[1]]);
        Assert.Equal(t[2], p.After(t[1], automatic: true));
    }

    [Fact]
    public void Shuffle_plays_every_track_once()
    {
        var p = Make(20, out _);
        p.Shuffle = true;
        var seen = new List<Track>();
        var current = p.After(null, automatic: true);
        while (current is not null && seen.Count < 50)
        {
            seen.Add(current);
            current = p.After(current, automatic: true);
        }
        Assert.Equal(20, seen.Count);
        Assert.Equal(20, seen.Distinct().Count());
    }

    [Fact]
    public void Move_puts_tracks_before_the_target()
    {
        var p = Make(5, out var t);
        p.Move([t[3], t[4]], 1);
        Assert.Equal(new[] { t[0], t[3], t[4], t[1], t[2] }, p.Items);
        Assert.Equal(Enumerable.Range(1, 5), p.Items.Select(x => x.Number));
    }

    [Fact]
    public void Remove_duplicates_keeps_the_first()
    {
        var p = new Playlist();
        var a = new Track("/m/a.mp3");
        p.Add([a, new Track("/m/b.mp3"), new Track("/m/a.mp3")]);
        Assert.Equal(1, p.RemoveDuplicates());
        Assert.Same(a, p.Items[0]);
    }

    [Fact]
    public void Sort_by_artist_then_album_then_track()
    {
        var p = new Playlist();
        p.Add([
            new Track("/3") { Artist = "B", Album = "X", TrackNumber = 1 },
            new Track("/2") { Artist = "A", Album = "Y", TrackNumber = 2 },
            new Track("/1") { Artist = "A", Album = "Y", TrackNumber = 1 },
        ]);
        p.Sort(SortKey.Artist);
        Assert.Equal(new[] { "/1", "/2", "/3" }, p.Items.Select(x => x.Path));
    }

    [Theory]
    [InlineData("2 - b", "10 - a", -1)]
    [InlineData("track 01", "track 1", 0)]
    [InlineData("Abc", "abd", -1)]
    public void Natural_order(string a, string b, int sign)
    {
        Assert.Equal(sign, Math.Sign(NaturalComparer.Instance.Compare(a, b)));
    }

    [Fact]
    public void Display_falls_back_to_the_file_name()
    {
        var t = new Track("/music/01 Something.flac");
        Assert.Equal("01 Something", t.Display);
        t.Title = "Song";
        Assert.Equal("Song", t.Display);
        t.Artist = "Band";
        Assert.Equal("Band – Song", t.Display);
    }

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(65.9, "1:05")]
    [InlineData(3725, "1:02:05")]
    public void Time_format(double seconds, string text) => Assert.Equal(text, Format.Time(seconds));
}
