using Fita.Library;

namespace Fita.Tests;

public sealed class PlaylistFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fita-tests-" + Guid.NewGuid().ToString("N"));

    public PlaylistFilesTests() => Directory.CreateDirectory(Path.Combine(_dir, "sub"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch { }
    }

    private string Touch(string relative)
    {
        var path = Path.Combine(_dir, relative);
        File.WriteAllText(path, "");
        return path;
    }

    [Fact]
    public void Folders_expand_recursively_in_natural_order_and_skip_other_files()
    {
        Touch("10 b.mp3");
        Touch("2 a.flac");
        Touch("cover.jpg");
        Touch(Path.Combine("sub", "1.ogg"));
        var tracks = PlaylistFiles.Expand([_dir]);
        Assert.Equal(new[] { "2 a.flac", "10 b.mp3", "1.ogg" }, tracks.Select(t => Path.GetFileName(t.Path)));
    }

    [Fact]
    public void M3u_round_trip_with_relative_paths_and_titles()
    {
        var a = Touch("a.mp3");
        var b = Touch(Path.Combine("sub", "b.flac"));
        var list = Path.Combine(_dir, "list.m3u8");
        PlaylistFiles.Write(list, [new Track(a) { Artist = "X", Title = "Ah", Duration = 61 }, new Track(b)]);

        var text = File.ReadAllText(list);
        Assert.Contains("#EXTINF:61,X – Ah", text);
        Assert.Contains("sub" + Path.DirectorySeparatorChar + "b.flac", text);

        var back = PlaylistFiles.Read(list);
        Assert.Equal(new[] { a, b }, back.Select(t => t.Path));
        Assert.Equal(61, back[0].Duration);
    }

    [Fact]
    public void Pls_round_trip()
    {
        var a = Touch("a.mp3");
        var list = Path.Combine(_dir, "list.pls");
        PlaylistFiles.Write(list, [new Track(a) { Title = "Ah", Duration = 10 }]);
        var back = PlaylistFiles.Read(list);
        Assert.Single(back);
        Assert.Equal(a, back[0].Path);
        Assert.Equal("Ah", back[0].Title);
    }

    [Fact]
    public void Urls_and_unknown_files_are_ignored()
    {
        var list = Path.Combine(_dir, "web.m3u");
        File.WriteAllLines(list, ["#EXTM3U", "http://radio.example/stream", "notes.txt", "song.mp3"]);
        var back = PlaylistFiles.Read(list);
        Assert.Single(back);
        Assert.EndsWith("song.mp3", back[0].Path);
    }
}
