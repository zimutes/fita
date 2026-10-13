using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fita.Library;

/// <summary>Grava a lista (com as etiquetas já lidas, para abrir logo) em playlist.json.</summary>
public static class PlaylistStore
{
    public sealed class Entry
    {
        public string Path { get; set; } = "";
        /// <summary>Troço de um .cue (segundos no ficheiro).</summary>
        public double? Start { get; set; }
        public double? End { get; set; }
        public string? Title { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public uint TrackNumber { get; set; }
        public uint Year { get; set; }
        public string? Genre { get; set; }
        public double Duration { get; set; }
        public int Bitrate { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int Bits { get; set; }
        public string? Codec { get; set; }
        public double? TrackGain { get; set; }
        public double? AlbumGain { get; set; }
        public double? TrackPeak { get; set; }
        public double? AlbumPeak { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public bool Loaded { get; set; }
        /// <summary>O ganho foi medido pela Fita (não veio das etiquetas).</summary>
        public bool Scanned { get; set; }
    }

    public sealed class Data
    {
        public int Version { get; set; } = 1;
        public int Current { get; set; } = -1;
        public double Position { get; set; }
        public List<Entry> Items { get; set; } = [];
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string FilePath => System.IO.Path.Combine(SettingsStore.Folder, "playlist.json");

    public static void Save(IReadOnlyList<Track> tracks, Track? current, double position)
    {
        var data = new Data
        {
            Current = current is null ? -1 : IndexOf(tracks, current),
            Position = position,
            Items = tracks.Select(ToEntry).ToList(),
        };
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Write("Falha a gravar a lista: " + ex.Message);
        }
    }

    public static (List<Track> Tracks, int Current, double Position) Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath), Options);
                if (data is not null)
                    return (data.Items.Where(e => !string.IsNullOrWhiteSpace(e.Path)).Select(FromEntry).ToList(),
                        data.Current, double.IsFinite(data.Position) ? Math.Max(0, data.Position) : 0);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Falha a ler a lista: " + ex.Message);
        }
        return ([], -1, 0);
    }

    private static int IndexOf(IReadOnlyList<Track> tracks, Track track)
    {
        for (var i = 0; i < tracks.Count; i++)
            if (ReferenceEquals(tracks[i], track))
                return i;
        return -1;
    }

    private static double? Nan(double v) => double.IsNaN(v) ? null : v;

    private static Entry ToEntry(Track t) => new()
    {
        Path = t.Path, Start = t.Start > 0 ? t.Start : null, End = t.End > 0 ? t.End : null, Title = t.Title, Artist = t.Artist, Album = t.Album, TrackNumber = t.TrackNumber,
        Year = t.Year, Genre = t.Genre, Duration = t.Duration, Bitrate = t.Bitrate, SampleRate = t.SampleRate,
        Channels = t.Channels, Bits = t.BitsPerSample, Codec = t.Codec, TrackGain = Nan(t.TrackGain),
        AlbumGain = Nan(t.AlbumGain), TrackPeak = Nan(t.TrackPeak), AlbumPeak = Nan(t.AlbumPeak),
        Size = t.FileSize, Modified = t.Modified, Loaded = t.MetadataLoaded, Scanned = t.LoudnessScanned,
    };

    private static Track FromEntry(Entry e)
    {
        var t = new Track(e.Path)
        {
            Start = e.Start is > 0 and < double.MaxValue ? e.Start.Value : 0,
            End = e.End is > 0 and < double.MaxValue ? e.End.Value : 0,
            Title = e.Title, Artist = e.Artist, Album = e.Album, TrackNumber = e.TrackNumber, Year = e.Year,
            Genre = e.Genre, Duration = e.Duration, Bitrate = e.Bitrate, SampleRate = e.SampleRate,
            Channels = e.Channels, BitsPerSample = e.Bits, Codec = e.Codec ?? "",
            TrackGain = e.TrackGain ?? double.NaN, AlbumGain = e.AlbumGain ?? double.NaN,
            TrackPeak = e.TrackPeak ?? double.NaN, AlbumPeak = e.AlbumPeak ?? double.NaN,
            FileSize = e.Size, Modified = e.Modified, MetadataLoaded = e.Loaded, LoudnessScanned = e.Scanned,
        };
        return t;
    }
}
