using System.Text;
using Fita.Library;

namespace Fita.Tests;

public sealed class CueSheetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fita-tests-" + Guid.NewGuid().ToString("N"));

    public CueSheetTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch { }
    }

    private string Touch(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "");
        return path;
    }

    private const string Disc = """
        REM GENRE Jazz
        REM DATE 1959
        REM REPLAYGAIN_ALBUM_GAIN -6.20 dB
        PERFORMER "Miles Davis"
        TITLE "Kind of Blue"
        FILE "Kind of Blue.wav" WAVE
          TRACK 01 AUDIO
            TITLE "So What"
            INDEX 01 00:00:00
          TRACK 02 AUDIO
            TITLE "Freddie Freeloader"
            INDEX 00 09:20:50
            INDEX 01 09:22:00
          TRACK 03 AUDIO
            TITLE "Blue in Green"
            PERFORMER "Miles Davis & Bill Evans"
            INDEX 01 18:56:37
        """;

    [Fact]
    public void Times_are_minutes_seconds_and_cd_frames()
    {
        Assert.Equal(0, CueSheet.ParseTime("00:00:00"));
        Assert.Equal(562.0, CueSheet.ParseTime("09:22:00"));
        Assert.Equal(60 * 105 + 1 + 74 / 75.0, CueSheet.ParseTime("105:01:74")!.Value, 9);
        Assert.Null(CueSheet.ParseTime("00:60:00"));
        Assert.Null(CueSheet.ParseTime("00:00:75"));
        Assert.Null(CueSheet.ParseTime("1:2"));
    }

    [Fact]
    public void Tracks_are_parts_of_the_file_from_index_01_to_the_next()
    {
        // O .cue fala de um .wav, mas o disco foi convertido para FLAC: serve o mesmo nome.
        var flac = Touch("Kind of Blue.flac");
        var tracks = CueSheet.Parse(Disc, _dir);

        Assert.Equal(3, tracks.Count);
        Assert.All(tracks, t => Assert.Equal(flac, t.Path));
        Assert.Equal(["So What", "Freddie Freeloader", "Blue in Green"], tracks.Select(t => t.Title));
        Assert.Equal([1u, 2u, 3u], tracks.Select(t => t.TrackNumber));

        // O intervalo (INDEX 00) fica no fim da faixa anterior.
        Assert.Equal(0, tracks[0].Start);
        Assert.Equal(562.0, tracks[0].End);
        Assert.Equal(562.0, tracks[1].Start);
        Assert.Equal(18 * 60 + 56 + 37 / 75.0, tracks[1].End, 9);
        Assert.Equal(tracks[1].End, tracks[2].Start);
        Assert.Equal(0, tracks[2].End);
        Assert.Equal(562.0, tracks[0].Duration);
        Assert.Equal(0, tracks[2].Duration);
        Assert.All(tracks, t => Assert.True(t.IsPart));
        Assert.Equal(3, tracks.Select(t => t.Key).Distinct().Count());

        Assert.Equal("Miles Davis", tracks[0].Artist);
        Assert.Equal("Miles Davis & Bill Evans", tracks[2].Artist);
        Assert.All(tracks, t => Assert.Equal("Kind of Blue", t.Album));
        Assert.All(tracks, t => Assert.Equal(1959u, t.Year));
        Assert.All(tracks, t => Assert.Equal("Jazz", t.Genre));
        Assert.All(tracks, t => Assert.Equal(-6.2, t.AlbumGain, 6));
    }

    [Fact]
    public void Missing_files_and_data_tracks_are_left_out()
    {
        Touch("a.flac");
        const string cue = """
            FILE "nao-existe.wav" WAVE
              TRACK 01 AUDIO
                INDEX 01 00:00:00
            FILE "a.flac" WAVE
              TRACK 02 AUDIO
                INDEX 01 00:00:00
              TRACK 03 MODE1/2352
                INDEX 01 03:00:00
            FILE "http://example.com/x.mp3" MP3
              TRACK 04 AUDIO
                INDEX 01 00:00:00
            """;
        var tracks = CueSheet.Parse(cue, _dir);
        Assert.Single(tracks);
        Assert.Equal(2u, tracks[0].TrackNumber);
        // Uma faixa sozinha no seu ficheiro é o ficheiro inteiro.
        Assert.False(tracks[0].IsPart);
    }

    [Fact]
    public void Old_cue_files_in_latin1_keep_their_accents()
    {
        Touch("disco.flac");
        var cue = Path.Combine(_dir, "disco.cue");
        File.WriteAllBytes(cue, Encoding.Latin1.GetBytes("TITLE \"Canções\"\nFILE \"disco.flac\" WAVE\n TRACK 01 AUDIO\n  TITLE \"Ópera\"\n  INDEX 01 00:00:00\n TRACK 02 AUDIO\n  INDEX 01 01:00:00\n"));
        var tracks = CueSheet.Read(cue);
        Assert.Equal("Canções", tracks[0].Album);
        Assert.Equal("Ópera", tracks[0].Title);
        Assert.Equal("disco #2", tracks[1].FileName);
    }

    [Fact]
    public void A_folder_with_a_cue_plays_the_cue_tracks_instead_of_the_whole_file()
    {
        Touch("Kind of Blue.flac");
        Touch("bonus.mp3");
        File.WriteAllText(Path.Combine(_dir, "Kind of Blue.cue"), Disc);
        var tracks = PlaylistFiles.Expand([_dir]);
        Assert.Equal(["bonus", "So What", "Freddie Freeloader", "Blue in Green"],
            tracks.Select(t => t.Title ?? Path.GetFileNameWithoutExtension(t.Path)));
    }

    [Fact]
    public void M3u_keeps_one_entry_per_cue_file()
    {
        var flac = Touch("Kind of Blue.flac");
        var tracks = CueSheet.Parse(Disc, _dir);
        var m3u = Path.Combine(_dir, "lista.m3u8");
        PlaylistFiles.Write(m3u, tracks);
        var back = PlaylistFiles.Read(m3u);
        Assert.Single(back);
        Assert.Equal(flac, back[0].Path);
        Assert.False(back[0].IsPart);
    }
}
